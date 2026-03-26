namespace UnityJS.Entities.EditModeTests
{
  using System.Collections;
  using NUnit.Framework;
  using Runtime;
  using Unity.Entities;
  using Unity.Mathematics;
  using UnityEngine.TestTools;
  using UnityJS.Entities.Tests;

  /// <summary>
  /// E2E tests for TDZ bug: components importing from unity.js/components
  /// fail on first play mode entry after domain reload.
  /// Uses e2e_mover fixture which imports LocalTransform from unity.js/components.
  /// </summary>
  public class ComponentsImportReloadE2ETests
  {
    const string SCRIPT = "components/e2e_mover";
    const int INIT_FRAMES = 8;

    [UnityTest]
    public IEnumerator TwoCycles_ComponentsImport_Fulfilled()
    {
      // Cycle 1
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using (var scene = new SceneFixture(world))
      {
        scene.Spawn(SCRIPT);
        for (var i = 0; i < INIT_FRAMES; i++) yield return null;
        Assert.IsTrue(scene.AllFulfilled(),
          "Cycle 1: e2e_mover (unity.js/components import) must be fulfilled");

        var vm = JsRuntimeManager.Instance;
        if (vm != null)
        {
          var health = vm.VerifyModuleHealth();
          Assert.IsNull(health, $"Cycle 1 module health failed: {health}");
        }
      }
      yield return new ExitPlayMode();

      // Cycle 2
      yield return new EnterPlayMode();
      var world2 = World.DefaultGameObjectInjectionWorld;
      using (var scene2 = new SceneFixture(world2))
      {
        scene2.Spawn(SCRIPT);
        for (var i = 0; i < INIT_FRAMES; i++) yield return null;
        Assert.IsTrue(scene2.AllFulfilled(),
          "Cycle 2: e2e_mover must be fulfilled after domain reload");

        var vm2 = JsRuntimeManager.Instance;
        if (vm2 != null)
        {
          var health2 = vm2.VerifyModuleHealth();
          Assert.IsNull(health2, $"Cycle 2 module health failed: {health2}");
        }
      }
      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator TwoCycles_ComponentsImport_NoTDZ()
    {
      // Cycle 1 — check module health immediately after fulfillment
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using (var scene = new SceneFixture(world))
      {
        scene.Spawn(SCRIPT);
        for (var i = 0; i < INIT_FRAMES; i++) yield return null;

        Assert.IsTrue(scene.AllFulfilled(),
          "Cycle 1: script must be fulfilled");

        var eid = scene.GetEntityId(scene[0]);
        var moved = JsEval.Bool($"!!_e2e_mover[{eid}]");
        Assert.IsTrue(moved,
          "Cycle 1: e2e_mover update() must have run (proves import resolved)");

        var vm = JsRuntimeManager.Instance;
        Assert.IsNotNull(vm, "VM must exist in cycle 1");
        var health = vm.VerifyModuleHealth();
        Assert.IsNull(health, $"Cycle 1 TDZ detected: {health}");
      }
      yield return new ExitPlayMode();

      // Cycle 2
      yield return new EnterPlayMode();
      var world2 = World.DefaultGameObjectInjectionWorld;
      using (var scene2 = new SceneFixture(world2))
      {
        scene2.Spawn(SCRIPT);
        for (var i = 0; i < INIT_FRAMES; i++) yield return null;

        Assert.IsTrue(scene2.AllFulfilled(),
          "Cycle 2: script must be fulfilled");

        var eid2 = scene2.GetEntityId(scene2[0]);
        var moved2 = JsEval.Bool($"!!_e2e_mover[{eid2}]");
        Assert.IsTrue(moved2,
          "Cycle 2: e2e_mover update() must have run");

        var vm2 = JsRuntimeManager.Instance;
        Assert.IsNotNull(vm2, "VM must exist in cycle 2");
        var health2 = vm2.VerifyModuleHealth();
        Assert.IsNull(health2, $"Cycle 2 TDZ detected: {health2}");
      }
      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator TwoCycles_MixedFixtures_BothFulfilled()
    {
      // Cycle 1 — spawn both lifecycle_probe (no components import) and e2e_mover (with)
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using (var scene = new SceneFixture(world))
      {
        scene.Spawn("components/lifecycle_probe");
        scene.Spawn(SCRIPT);
        for (var i = 0; i < INIT_FRAMES; i++) yield return null;

        Assert.IsTrue(scene.AllFulfilled(),
          "Cycle 1: both scripts must be fulfilled");

        var moverEid = scene.GetEntityId(scene[1]);
        var moved = JsEval.Bool($"!!_e2e_mover[{moverEid}]");
        Assert.IsTrue(moved,
          "Cycle 1: e2e_mover must work alongside lifecycle_probe");

        var vm = JsRuntimeManager.Instance;
        if (vm != null)
        {
          var health = vm.VerifyModuleHealth();
          Assert.IsNull(health, $"Cycle 1 module health failed: {health}");
        }
      }
      yield return new ExitPlayMode();

      // Cycle 2
      yield return new EnterPlayMode();
      var world2 = World.DefaultGameObjectInjectionWorld;
      using (var scene2 = new SceneFixture(world2))
      {
        scene2.Spawn("components/lifecycle_probe");
        scene2.Spawn(SCRIPT);
        for (var i = 0; i < INIT_FRAMES; i++) yield return null;

        Assert.IsTrue(scene2.AllFulfilled(),
          "Cycle 2: both scripts must be fulfilled");

        var moverEid2 = scene2.GetEntityId(scene2[1]);
        var moved2 = JsEval.Bool($"!!_e2e_mover[{moverEid2}]");
        Assert.IsTrue(moved2,
          "Cycle 2: e2e_mover must work alongside lifecycle_probe");

        var vm2 = JsRuntimeManager.Instance;
        if (vm2 != null)
        {
          var health2 = vm2.VerifyModuleHealth();
          Assert.IsNull(health2, $"Cycle 2 module health failed: {health2}");
        }
      }
      yield return new ExitPlayMode();
    }

  }
}
