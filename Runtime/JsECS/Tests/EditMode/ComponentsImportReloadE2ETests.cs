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
    /// Nukes the VM mid-play and verifies the system recovers with zero errors.
    /// Expected: after VM destruction, nothing updates (no errors, just silence).
    /// After fresh VM is created, the system detects the change, re-registers
    /// bridges, reloads glue, and components resume — including those that
    /// import from unity.js/components (the TDZ-vulnerable path).
    ///
    /// All assertions happen BEFORE ExitPlayMode. LogAssert.ignoreFailingMessages
    /// covers the exit transition where collateral errors from other project
    /// systems (character_input etc.) may fire.
    /// </summary>
    [UnityTest]
    public IEnumerator VmNuke_ComponentsImport_SurvivesAndResumes()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;

      // Phase 1: establish baseline — components work
      using (var scene = new SceneFixture(world))
      {
        scene.Spawn(SCRIPT);
        for (var i = 0; i < INIT_FRAMES; i++) yield return null;
        Assert.IsTrue(scene.AllFulfilled(), "Baseline: e2e_mover must fulfill");
      }

      // Phase 2: NUKE the VM
      var oldVm = JsRuntimeManager.Instance;
      Assert.IsNotNull(oldVm, "VM must exist before nuke");
      oldVm.Dispose();

      // Suppress errors from other project systems that die with the VM.
      // Our assertions use CapturedExceptions, not LogAssert.
      LogAssert.ignoreFailingMessages = true;

      // Let a few frames pass with no VM — nothing should error, just silence
      for (var i = 0; i < 3; i++) yield return null;

      // Phase 3: bring VM back from the dead
      var freshVm = JsRuntimeManager.GetOrCreate();
      Assert.AreNotSame(oldVm, freshVm, "Fresh VM must be a new instance");
      freshVm.ClearCapturedExceptions();

      // Phase 4: spawn on the fresh VM — system must detect m_Vm != m_LastVm,
      // re-register bridges, reload glue, and fulfill WITHOUT TDZ errors
      using (var scene2 = new SceneFixture(world))
      {
        scene2.Spawn(SCRIPT);
        for (var i = 0; i < INIT_FRAMES; i++) yield return null;

        // THE critical assertion: zero exceptions after VM recreation
        Assert.AreEqual(0, freshVm.CapturedExceptions.Count,
          $"VM must have ZERO exceptions after resurrection, got: " +
          $"{string.Join("; ", freshVm.CapturedExceptions)}");

        Assert.IsTrue(scene2.AllFulfilled(),
          "e2e_mover must fulfill after VM resurrection (unity.js/components import)");

        var health = freshVm.VerifyModuleHealth();
        Assert.IsNull(health, $"Module TDZ after resurrection: {health}");
      }

      yield return new ExitPlayMode();
    }

    /// <summary>
    /// Nukes the VM THREE times in a single play session. Each cycle:
    /// dispose → recreate → spawn → tick → assert zero errors.
    /// Catches accumulation bugs: leaked state, stale caches, growing exception lists.
    /// </summary>
    [UnityTest]
    public IEnumerator VmNuke_ThreeCycles_SurvivesRepeatedDestruction()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      LogAssert.ignoreFailingMessages = true;

      // Establish baseline
      using (var scene = new SceneFixture(world))
      {
        scene.Spawn(SCRIPT);
        for (var i = 0; i < INIT_FRAMES; i++) yield return null;
        Assert.IsTrue(scene.AllFulfilled(), "Baseline must fulfill");
      }

      // Nuke and resurrect 3 times
      for (var cycle = 1; cycle <= 3; cycle++)
      {
        var doomed = JsRuntimeManager.Instance;
        doomed?.Dispose();

        // Dead frames — silence, no errors
        for (var i = 0; i < 2; i++) yield return null;

        var risen = JsRuntimeManager.GetOrCreate();
        risen.ClearCapturedExceptions();

        using (var scene = new SceneFixture(world))
        {
          scene.Spawn(SCRIPT);
          for (var i = 0; i < INIT_FRAMES; i++) yield return null;

          Assert.AreEqual(0, risen.CapturedExceptions.Count,
            $"Cycle {cycle}: VM must have ZERO exceptions, got: " +
            $"{string.Join("; ", risen.CapturedExceptions)}");

          Assert.IsTrue(scene.AllFulfilled(),
            $"Cycle {cycle}: e2e_mover must fulfill after nuke");

          var health = risen.VerifyModuleHealth();
          Assert.IsNull(health, $"Cycle {cycle}: TDZ detected: {health}");
        }
      }

      yield return new ExitPlayMode();
    }
  }
}
