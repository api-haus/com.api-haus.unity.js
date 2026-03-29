namespace UnityJS.Integration.Physics.PlayModeTests
{
  using System.Collections;
  using NUnit.Framework;
  using Unity.Entities;
  using UnityEngine.TestTools;
  using UnityJS.Runtime;

  /// <summary>
  /// E2E tests verifying Physics integration works in play mode.
  /// </summary>
  [TestFixture]
  public class JsPhysicsIntegrationE2ETests
  {
    [UnityTest]
    public IEnumerator PlayModeCycle_PhysicsTickSystems_NoErrors()
    {
      var world = World.DefaultGameObjectInjectionWorld;
      Assert.IsNotNull(world, "World should exist in play mode");

      var beforePhysics = world.Unmanaged.GetExistingUnmanagedSystem<JsBeforePhysicsTickSystem>();
      var afterPhysics = world.Unmanaged.GetExistingUnmanagedSystem<JsAfterPhysicsTickSystem>();
      var fixedTick = world.Unmanaged.GetExistingUnmanagedSystem<JsFixedTickSystem>();

      Assert.That(beforePhysics != SystemHandle.Null, "JsBeforePhysicsTickSystem should exist");
      Assert.That(afterPhysics != SystemHandle.Null, "JsAfterPhysicsTickSystem should exist");
      Assert.That(fixedTick != SystemHandle.Null, "JsFixedTickSystem should exist");

      yield return null;

      var vm = JsRuntimeManager.Instance;
      if (vm != null)
        Assert.IsEmpty(vm.CapturedExceptions, "No JS exceptions after physics tick frame");
    }
  }
}
