namespace UnityJS.Integration.Physics.EditModeTests
{
  using System.Collections;
  using NUnit.Framework;
  using Unity.Entities;
  using UnityEngine.TestTools;
  using UnityJS.Runtime;

  /// <summary>
  /// E2E tests verifying Physics integration survives play mode cycles.
  /// </summary>
  [TestFixture]
  public class JsPhysicsIntegrationE2ETests
  {
    [UnityTest]
    public IEnumerator PlayModeCycle_PhysicsTickSystems_NoErrors()
    {
      yield return new EnterPlayMode();

      var world = World.DefaultGameObjectInjectionWorld;
      Assert.IsNotNull(world, "World should exist in play mode");

      var beforePhysics = world.GetExistingSystemManaged<JsBeforePhysicsTickSystem>();
      var afterPhysics = world.GetExistingSystemManaged<JsAfterPhysicsTickSystem>();
      var fixedTick = world.GetExistingSystemManaged<JsFixedTickSystem>();

      Assert.IsNotNull(beforePhysics, "JsBeforePhysicsTickSystem should exist");
      Assert.IsNotNull(afterPhysics, "JsAfterPhysicsTickSystem should exist");
      Assert.IsNotNull(fixedTick, "JsFixedTickSystem should exist");

      yield return null;

      var vm = JsRuntimeManager.Instance;
      if (vm != null)
        Assert.IsEmpty(vm.CapturedExceptions, "No JS exceptions after physics tick frame");

      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator MultiplePlayModeCycles_PhysicsIntegration_Stable()
    {
      for (var cycle = 0; cycle < 2; cycle++)
      {
        yield return new EnterPlayMode();

        var world = World.DefaultGameObjectInjectionWorld;
        Assert.IsNotNull(world, $"World should exist in cycle {cycle}");

        var fixedTick = world.GetExistingSystemManaged<JsFixedTickSystem>();
        Assert.IsNotNull(fixedTick, $"JsFixedTickSystem should exist in cycle {cycle}");

        yield return null;

        var vm = JsRuntimeManager.Instance;
        if (vm != null)
          Assert.IsEmpty(vm.CapturedExceptions, $"No JS exceptions in cycle {cycle}");

        yield return new ExitPlayMode();
      }
    }
  }
}
