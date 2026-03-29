namespace UnityJS.Entities.PlayModeTests
{
  using System.Collections;
  using System.IO;
  using System.Text.RegularExpressions;
  using NUnit.Framework;
  using Unity.Entities;
  using Unity.Mathematics;
  using UnityEngine;
  using UnityEngine.TestTools;
  using UnityJS.Entities.Tests;
  using UnityJS.Runtime;

  public class HotReloadPlayModeTests
  {
    const string MOVER = "components/e2e_mover";
    const string PROBE = "components/e2e_hot_reload_probe";
    const int INIT_FRAMES = 12;

    static string ProbeTsPath
    {
      get
      {
        var fixturesPath = SceneFixture.GetPackageFixturesSourcePath();
        return fixturesPath != null
          ? Path.Combine(fixturesPath, "components", "e2e_hot_reload_probe.ts")
          : Path.Combine(Application.streamingAssetsPath, "unity.js", "components", "e2e_hot_reload_probe.ts");
      }
    }

    string m_OriginalContent;
    SceneFixture m_Scene;

    [SetUp]
    public void SetUp()
    {
      m_OriginalContent = File.ReadAllText(ProbeTsPath);
      m_Scene = new SceneFixture(World.DefaultGameObjectInjectionWorld);
    }

    [TearDown]
    public void TearDown()
    {
      m_Scene?.Dispose();
      if (m_OriginalContent != null)
        File.WriteAllText(ProbeTsPath, m_OriginalContent);
    }

    [UnityTest]
    public IEnumerator HotReload_ComponentReload_PreservesEntity()
    {
      var entity = m_Scene.Spawn(MOVER);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(m_Scene.AllFulfilled(), "e2e_mover must fulfill");

      var eid = m_Scene.GetEntityId(entity);
      var movedBefore = JsEval.Bool($"!!_e2e_mover[{eid}]");
      Assert.IsTrue(movedBefore, "e2e_mover must have ticked before reload");

      var posBefore = m_Scene.GetPosition(entity);

      var vm = JsRuntimeManager.Instance;
      vm.SimulateHotReload(MOVER);
      vm.ComponentReload(MOVER);
      vm.ClearCapturedExceptions();

      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      var posAfter = m_Scene.GetPosition(entity);
      Assert.Greater(math.distance(posAfter, posBefore), 0.001f,
        "Entity must keep moving after hot reload");

      Assert.AreEqual(0, vm.CapturedExceptions.Count,
        $"Zero exceptions after hot reload: {string.Join("; ", vm.CapturedExceptions)}");

      var health = vm.VerifyModuleHealth();
      Assert.IsNull(health, $"Module health after reload: {health}");
    }

    [UnityTest]
    public IEnumerator HotReload_SyntaxError_OldCodeSurvives()
    {
      m_Scene.Spawn(PROBE);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(m_Scene.AllFulfilled(), "Probe must fulfill");

      var eid = m_Scene.GetEntityId(m_Scene[0]);
      var v1 = JsEval.Int($"_e2e_hot[{eid}]?.version ?? -1");
      Assert.AreEqual(1, v1, "Initial version must be 1");

      // Introduce syntax error
      File.WriteAllText(ProbeTsPath, "export default class BROKEN {{{{{");

      var vm = JsRuntimeManager.Instance;
      vm.ClearCapturedExceptions();
      LogAssert.Expect(LogType.Error, new Regex("\\[JsTranspiler\\]"));
      vm.SimulateHotReload(PROBE);

      // Old code should still work
      for (var i = 0; i < 5; i++) yield return null;

      var vStill = JsEval.Int($"_e2e_hot[{eid}]?.version ?? -1");
      Assert.AreEqual(1, vStill, "Old entity must retain version after failed reload");

      // Fix the error and reload
      File.WriteAllText(ProbeTsPath, m_OriginalContent.Replace(
        "const VERSION = 1", "const VERSION = 3"));
      vm.ClearCapturedExceptions();
      vm.SimulateHotReload(PROBE);

      // Spawn new entity with fixed code
      var entity2 = m_Scene.Spawn(PROBE);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      var eid2 = m_Scene.GetEntityId(entity2);
      var v3 = JsEval.Int($"_e2e_hot[{eid2}]?.version ?? -1");
      Assert.AreEqual(3, v3, "New entity after recovery must have version 3");

      Assert.AreEqual(0, vm.CapturedExceptions.Count,
        $"Zero exceptions after recovery: {string.Join("; ", vm.CapturedExceptions)}");
    }

    [UnityTest]
    public IEnumerator HotReload_RapidReloads_NoExceptions()
    {
      m_Scene.Spawn(MOVER);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(m_Scene.AllFulfilled(), "e2e_mover must fulfill");

      var vm = JsRuntimeManager.Instance;
      vm.ClearCapturedExceptions();

      // Rapid-fire 10 reloads
      for (var round = 0; round < 10; round++)
      {
        vm.SimulateHotReload(MOVER);
        vm.ComponentReload(MOVER);
      }

      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      Assert.AreEqual(0, vm.CapturedExceptions.Count,
        $"Zero exceptions after 10 rapid reloads: {string.Join("; ", vm.CapturedExceptions)}");

      var health = vm.VerifyModuleHealth();
      Assert.IsNull(health, $"Module health after rapid reloads: {health}");

      Assert.IsTrue(m_Scene.AllFulfilled(), "Entity must still be fulfilled");
    }

    [Test]
    public void ScriptsResolveFromTs()
    {
      JsRuntimeManager.GetOrCreate();
      JsScriptSearchPaths.Initialize();
      var fixturesPath = SceneFixture.GetPackageFixturesPath();
      if (fixturesPath != null)
        JsScriptSearchPaths.AddSearchPath(fixturesPath, 0);
      Assert.IsTrue(
        JsScriptSourceRegistry.TryReadScript(PROBE, out _, out var resolvedId),
        $"{PROBE} must be readable"
      );
      Assert.IsTrue(
        resolvedId.EndsWith(".ts"),
        $"Script should resolve from .ts source, got: {resolvedId}"
      );
    }

    [UnityTest]
    [Ignore("SIGSEGV in native JsTranspiler on repeated broken-syntax transpilation — pre-existing")]
    public IEnumerator HotReload_TranspileErrorLifecycle()
    {
      yield return null; // let runtime initialize

      Assert.IsTrue(JsTranspiler.IsInitialized, "Transpiler must be initialized in play mode");

      // Inject errors for two different files
      LogAssert.Expect(LogType.Error, new Regex("\\[JsTranspiler\\]"));
      JsTranspiler.Transpile("syntax error {{{{", "pm_hot_a.ts");
      LogAssert.Expect(LogType.Error, new Regex("\\[JsTranspiler\\]"));
      JsTranspiler.Transpile("another broken ]]]]", "pm_hot_b.ts");

      Assert.GreaterOrEqual(JsTranspiler.ErrorCount, 2, "At least two errors tracked");
      Assert.IsTrue(JsTranspiler.Errors.ContainsKey("pm_hot_a.ts"));
      Assert.IsTrue(JsTranspiler.Errors.ContainsKey("pm_hot_b.ts"));

      // Fix file_a
      var fixedA = JsTranspiler.Transpile("export const a: number = 1;", "pm_hot_a.ts");
      Assert.IsNotNull(fixedA);
      Assert.IsFalse(JsTranspiler.Errors.ContainsKey("pm_hot_a.ts"), "Fixed file must clear");
      Assert.IsTrue(JsTranspiler.Errors.ContainsKey("pm_hot_b.ts"), "Other file still broken");

      // Fix file_b
      var fixedB = JsTranspiler.Transpile("export const b: number = 2;", "pm_hot_b.ts");
      Assert.IsNotNull(fixedB);
      Assert.IsFalse(JsTranspiler.Errors.ContainsKey("pm_hot_b.ts"), "All errors cleared");
    }
  }
}
