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
  /// E2E test for hot reload. Mutates a TS fixture, recompiles, verifies
  /// new version loads without crashing. Restores original file in teardown.
  /// </summary>
  public class HotReloadE2ETests
  {
    const string SCRIPT = "components/e2e_hot_reload_probe";
    const int INIT_FRAMES = 12;

    static string s_tsPath
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
      m_OriginalContent = File.ReadAllText(s_tsPath);
      yield break;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
      // Always restore original file
      if (m_OriginalContent != null)
      {
        File.WriteAllText(s_tsPath, m_OriginalContent);
        // No recompile needed — transpiled on-demand by JsTranspiler
      }
      yield break;
    }

    [UnityTest]
    public IEnumerator Reload_UpdatesVersion()
    {
      yield return new EnterPlayMode();

      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);

      // Spawn with version 1
      var entity = scene.Spawn(SCRIPT);
      var eid = scene.GetEntityId(entity);

      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(scene.AllFulfilled(), "Script must be fulfilled");

      var v1 = JsEval.Int($"_e2e_hot[{eid}]?.version ?? -1");
      Assert.AreEqual(1, v1, "Initial version should be 1");

      // Mutate the TS file: change VERSION = 1 → VERSION = 2
      var mutated = m_OriginalContent.Replace("const VERSION = 1", "const VERSION = 2");
      Assert.AreNotEqual(m_OriginalContent, mutated, "Mutation must change the file");
      File.WriteAllText(s_tsPath, mutated);

      // Trigger hot reload (transpiled on-demand)
      var vm = JsRuntimeManager.Instance;
      vm?.SimulateHotReload("components/e2e_hot_reload_probe");

      // Spawn a new entity with the reloaded script
      var entity2 = scene.Spawn(SCRIPT);
      var eid2 = scene.GetEntityId(entity2);

      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      var v2 = JsEval.Int($"_e2e_hot[{eid2}]?.version ?? -1");
      Assert.AreEqual(2, v2, "Reloaded version should be 2");

      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Reload_NoExceptions()
    {
      yield return new EnterPlayMode();

      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      scene.Spawn(SCRIPT);

      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(scene.AllFulfilled(), "Script must be fulfilled");

      // Mutate and reload 3 times rapidly
      for (var round = 2; round <= 4; round++)
      {
        var mutated = m_OriginalContent.Replace(
          "const VERSION = 1", $"const VERSION = {round}");
        File.WriteAllText(s_tsPath, mutated);
        // No recompile needed — transpiled on-demand by JsTranspiler
        JsRuntimeManager.Instance?.SimulateHotReload(
          "components/e2e_hot_reload_probe");
        yield return null;
      }

      // Restore original
      File.WriteAllText(s_tsPath, m_OriginalContent);

      var vm = JsRuntimeManager.Instance;
      Assert.IsNotNull(vm, "VM must survive rapid reloads");
      Assert.IsEmpty(vm.CapturedExceptions,
        $"No JS exceptions after rapid reloads: {string.Join("\n", vm.CapturedExceptions)}");

      yield return new ExitPlayMode();
    }

    [Test]
    public void ScriptsResolveFromTs()
    {
      JsScriptSearchPaths.Initialize();
      Assert.IsTrue(
        JsScriptSourceRegistry.TryReadScript(SCRIPT, out _, out var resolvedId),
        $"{SCRIPT} must be readable"
      );
      Assert.IsTrue(
        resolvedId.EndsWith(".ts"),
        $"Script should resolve from .ts source, got: {resolvedId}"
      );
    }

    [UnityTest]
    public IEnumerator TouchTs_EnterPlayMode_NoTdzErrors()
    {
      // Append comment to fixture .ts before entering play
      File.WriteAllText(s_tsPath, m_OriginalContent + "\n// touch-test\n");

      yield return new EnterPlayMode();

      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);

      var entity = scene.Spawn(SCRIPT);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(scene.AllFulfilled(), "Script must be fulfilled after touch");

      var vm = JsRuntimeManager.Instance;
      Assert.IsNotNull(vm, "VM should exist");

      var tdzErrors = new System.Collections.Generic.List<string>();
      foreach (var ex in vm.CapturedExceptions)
        if (ex.Contains("not initialized") || ex.Contains("not defined"))
          tdzErrors.Add(ex);
      Assert.IsEmpty(tdzErrors,
        "No TDZ errors after touching .ts:\n" + string.Join("\n", tdzErrors));

      yield return new ExitPlayMode();
    }
  }
}
