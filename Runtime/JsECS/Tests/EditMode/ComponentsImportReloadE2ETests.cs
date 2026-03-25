namespace UnityJS.Entities.EditModeTests
{
  using System.Collections;
  using NUnit.Framework;
  using Runtime;
  using Unity.Entities;
  using Unity.Mathematics;
  using UnityEngine.TestTools;

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

    /// <summary>
    /// Simulates domain reload mid-play by disposing and recreating the VM.
    /// This forces JsComponentInitSystem to detect m_Vm != m_LastVm and
    /// re-initialize all bridges, glue, and scripts from scratch — the exact
    /// code path that produces TDZ errors in production after recompilation.
    ///
    /// [Explicit] — VM recreation breaks ALL running JS systems (not just the
    /// test entity), producing collateral errors that LogAssert cannot suppress
    /// across domain reload boundaries. Run manually to verify the TDZ bug.
    ///
    /// KNOWN BUG: components importing from unity.js/components fail with
    /// "ReferenceError: default is not initialized" after VM recreation.
    /// Once fixed, remove [Explicit] and flip assertions.
    /// </summary>
    [UnityTest, Explicit("Reproduces TDZ bug — run manually, collateral system errors expected")]
    public IEnumerator ForcedVmRecreation_ComponentsImport_TDZ()
    {
      yield return new EnterPlayMode();
      LogAssert.ignoreFailingMessages = true;
      var world = World.DefaultGameObjectInjectionWorld;

      // Phase 1: spawn and fulfill normally
      using (var scene = new SceneFixture(world))
      {
        scene.Spawn(SCRIPT);
        for (var i = 0; i < INIT_FRAMES; i++) yield return null;
        Assert.IsTrue(scene.AllFulfilled(), "Initial fulfillment must succeed");
      }

      // Phase 2: destroy the VM and create a fresh one (simulates domain reload)
      var oldVm = JsRuntimeManager.Instance;
      Assert.IsNotNull(oldVm, "VM must exist before forced recreation");
      oldVm.Dispose();

      // Create fresh VM — JsComponentInitSystem.OnUpdate will detect the change
      var newVm = JsRuntimeManager.GetOrCreate();
      Assert.AreNotSame(oldVm, newVm, "New VM must be a different instance");

      // Phase 3: spawn entity on the fresh VM — triggers TDZ-vulnerable path
      using (var scene2 = new SceneFixture(world))
      {
        scene2.Spawn(SCRIPT);
        for (var i = 0; i < INIT_FRAMES; i++) yield return null;

        // BUG: The component fails to fulfill due to TDZ.
        // Once fixed, change to Assert.IsTrue and remove [Explicit].
        Assert.IsFalse(scene2.AllFulfilled(),
          "BUG: unity.js/components import causes TDZ after VM recreation");

        Assert.IsTrue(newVm.CapturedExceptions.Count > 0,
          "VM must have captured the TDZ exception");
      }

      yield return new ExitPlayMode();
    }

    /// <summary>
    /// Same as above but with mixed fixtures — catches ordering-dependent TDZ
    /// where a unity.js/ecs-only module works but unity.js/components module fails.
    /// </summary>
    [UnityTest, Explicit("Reproduces TDZ bug — run manually, collateral system errors expected")]
    public IEnumerator ForcedVmRecreation_MixedFixtures_NoTDZ()
    {
      yield return new EnterPlayMode();
      LogAssert.ignoreFailingMessages = true;
      var world = World.DefaultGameObjectInjectionWorld;

      // Phase 1: initial fulfillment
      using (var scene = new SceneFixture(world))
      {
        scene.Spawn("components/lifecycle_probe");
        scene.Spawn(SCRIPT);
        for (var i = 0; i < INIT_FRAMES; i++) yield return null;
        Assert.IsTrue(scene.AllFulfilled(), "Initial fulfillment must succeed");
      }

      // Phase 2: force VM recreation
      var oldVm = JsRuntimeManager.Instance;
      oldVm.Dispose();
      var newVm = JsRuntimeManager.GetOrCreate();

      // Phase 3: re-spawn both fixtures on fresh VM
      using (var scene2 = new SceneFixture(world))
      {
        scene2.Spawn("components/lifecycle_probe");
        scene2.Spawn(SCRIPT);
        for (var i = 0; i < INIT_FRAMES; i++) yield return null;

        Assert.IsTrue(scene2.AllFulfilled(),
          "Both scripts must fulfill after VM recreation");

        var moverEid = scene2.GetEntityId(scene2[1]);
        var moved = JsEval.Bool($"!!_e2e_mover[{moverEid}]");
        Assert.IsTrue(moved,
          "e2e_mover must work after VM recreation alongside lifecycle_probe");

        var health = newVm.VerifyModuleHealth();
        Assert.IsNull(health, $"TDZ after VM recreation: {health}");

        Assert.AreEqual(0, newVm.CapturedExceptions.Count,
          $"No JS exceptions expected, got: {string.Join("; ", newVm.CapturedExceptions)}");
      }

      yield return new ExitPlayMode();
    }
  }
}
