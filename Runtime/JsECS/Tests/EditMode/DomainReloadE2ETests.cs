namespace UnityJS.Entities.EditModeTests
{
  using System.Collections;
  using NUnit.Framework;
  using Runtime;
  using Unity.Entities;
  using Unity.Mathematics;
  using UnityEngine.TestTools;

  /// <summary>
  /// E2E tests for domain reload (enter/exit play mode cycles).
  /// Verifies VM survives multiple cycles without TDZ errors or crashes.
  /// </summary>
  public class DomainReloadE2ETests
  {
    const int INIT_FRAMES = 8;

    [UnityTest]
    public IEnumerator TwoCycles_EntitiesFulfilled()
    {
      // Cycle 1
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using (var scene = new SceneFixture(world))
      {
        scene.Spawn("tests/components/lifecycle_probe");
        for (var i = 0; i < INIT_FRAMES; i++) yield return null;
        Assert.IsTrue(scene.AllFulfilled(), "Cycle 1: script must be fulfilled");
      }
      yield return new ExitPlayMode();

      // Cycle 2
      yield return new EnterPlayMode();
      var world2 = World.DefaultGameObjectInjectionWorld;
      using (var scene2 = new SceneFixture(world2))
      {
        scene2.Spawn("tests/components/lifecycle_probe");
        for (var i = 0; i < INIT_FRAMES; i++) yield return null;
        Assert.IsTrue(scene2.AllFulfilled(), "Cycle 2: script must be fulfilled");
      }
      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator NoTDZErrors_AfterCycle()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using (var scene = new SceneFixture(world))
      {
        scene.Spawn("tests/components/lifecycle_probe");
        for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      }
      yield return new ExitPlayMode();

      // Second cycle — check for TDZ errors
      yield return new EnterPlayMode();
      var world2 = World.DefaultGameObjectInjectionWorld;
      using (var scene2 = new SceneFixture(world2))
      {
        scene2.Spawn("tests/components/lifecycle_probe");
        for (var i = 0; i < INIT_FRAMES; i++) yield return null;
        Assert.IsTrue(scene2.AllFulfilled(), "Script must be fulfilled after domain reload");

        var vm = JsRuntimeManager.Instance;
        if (vm != null)
        {
          var health = vm.VerifyModuleHealth();
          Assert.IsNull(health, $"Module health check failed: {health}");
        }
      }
      yield return new ExitPlayMode();
    }
  }
}
