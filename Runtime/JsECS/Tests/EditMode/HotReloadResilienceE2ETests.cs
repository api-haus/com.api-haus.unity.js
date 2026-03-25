namespace UnityJS.Entities.EditModeTests
{
  using System.Collections;
  using System.IO;
  using NUnit.Framework;
  using Unity.Entities;
  using Unity.Mathematics;
  using UnityEngine;
  using UnityEngine.TestTools;

  using UnityJS.Runtime;

  /// <summary>
  /// Hot reload resilience tests for the modder scenario:
  /// editing TS files while the game is running.
  /// </summary>
  public class HotReloadResilienceE2ETests
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

    [UnitySetUp]
    public IEnumerator SetUp()
    {
      m_OriginalContent = File.ReadAllText(ProbeTsPath);
      yield break;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
      if (m_OriginalContent != null)
      {
        File.WriteAllText(ProbeTsPath, m_OriginalContent);
        // No recompile needed — transpiled on-demand
      }
      yield break;
    }

    [UnityTest]
    public IEnumerator HotReload_ComponentWithImports_PreservesAndResumes()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);

      var entity = scene.Spawn(MOVER);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(scene.AllFulfilled(), "e2e_mover must fulfill");

      var eid = scene.GetEntityId(entity);
      var movedBefore = JsEval.Bool($"!!_e2e_mover[{eid}]");
      Assert.IsTrue(movedBefore, "e2e_mover must have ticked before reload");

      var posBefore = scene.GetPosition(entity);

      // Hot reload the component (imports unity.js/components)
      var vm = JsRuntimeManager.Instance;
      vm.SimulateHotReload(MOVER);
      vm.ComponentReload(MOVER);
      vm.ClearCapturedExceptions();

      // Let it run more frames — should resume without TDZ
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      var posAfter = scene.GetPosition(entity);
      Assert.Greater(math.distance(posAfter, posBefore), 0.01f,
        "Entity must keep moving after hot reload");

      Assert.AreEqual(0, vm.CapturedExceptions.Count,
        $"Zero exceptions after hot reload: {string.Join("; ", vm.CapturedExceptions)}");

      var health = vm.VerifyModuleHealth();
      Assert.IsNull(health, $"Module health after reload: {health}");

      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator HotReload_SyntaxError_OldCodeSurvives()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);

      scene.Spawn(PROBE);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(scene.AllFulfilled(), "Probe must fulfill");

      var eid = scene.GetEntityId(scene[0]);
      var v1 = JsEval.Int($"_e2e_hot[{eid}]?.version ?? -1");
      Assert.AreEqual(1, v1, "Initial version must be 1");

      // Introduce syntax error
      File.WriteAllText(ProbeTsPath, "export default class BROKEN {{{{{");

      // SimulateHotReload should fail gracefully
      var vm = JsRuntimeManager.Instance;
      vm.ClearCapturedExceptions();
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
      var entity2 = scene.Spawn(PROBE);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      var eid2 = scene.GetEntityId(entity2);
      var v3 = JsEval.Int($"_e2e_hot[{eid2}]?.version ?? -1");
      Assert.AreEqual(3, v3, "New entity after recovery must have version 3");

      Assert.AreEqual(0, vm.CapturedExceptions.Count,
        $"Zero exceptions after recovery: {string.Join("; ", vm.CapturedExceptions)}");

      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator HotReload_TranspileError_DetectedAndRecoverable()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);

      scene.Spawn(PROBE);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(scene.AllFulfilled(), "Probe must fulfill");

      var vm = JsRuntimeManager.Instance;
      Assert.IsTrue(JsTranspiler.IsInitialized, "Transpiler must be initialized");

      // Transpile broken source — must fail and increment error count
      var errBefore = JsTranspiler.ErrorCount;
      var broken = JsTranspiler.Transpile(vm.Context, "export default class BROKEN {{{{{");
      Assert.IsNull(broken, "Transpilation must fail for broken source");
      Assert.Greater(JsTranspiler.ErrorCount, errBefore, "Error count must increase");
      Assert.IsNotNull(JsTranspiler.LastError, "LastError must be set");

      // Transpile valid source — must succeed
      var valid = JsTranspiler.Transpile(vm.Context, m_OriginalContent);
      Assert.IsNotNull(valid, "Transpilation must succeed for valid source");

      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator HotReload_RapidReloads_ComponentWithImports()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);

      scene.Spawn(MOVER);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(scene.AllFulfilled(), "e2e_mover must fulfill");

      var vm = JsRuntimeManager.Instance;
      vm.ClearCapturedExceptions();

      // Rapid-fire 10 reloads of a component with unity.js/components imports
      for (var round = 0; round < 10; round++)
      {
        vm.SimulateHotReload(MOVER);
        vm.ComponentReload(MOVER);
      }

      // Let it stabilize
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      Assert.AreEqual(0, vm.CapturedExceptions.Count,
        $"Zero exceptions after 10 rapid reloads: {string.Join("; ", vm.CapturedExceptions)}");

      var health = vm.VerifyModuleHealth();
      Assert.IsNull(health, $"Module health after rapid reloads: {health}");

      Assert.IsTrue(scene.AllFulfilled(), "Entity must still be fulfilled");

      yield return new ExitPlayMode();
    }
  }
}
