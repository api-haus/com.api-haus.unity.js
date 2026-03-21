namespace UnityJS.Integration.InputSystem.EditModeTests
{
  using System.Collections;
  using NUnit.Framework;
  using Unity.Entities;
  using UnityEngine.TestTools;
  using UnityJS.Runtime;

  /// <summary>
  /// E2E tests verifying InputSystem integration survives play mode cycles.
  /// </summary>
  [TestFixture]
  public class JsInputIntegrationE2ETests
  {
    [UnityTest]
    public IEnumerator PlayModeCycle_InputBridge_NoErrors()
    {
      yield return new EnterPlayMode();

      var vm = JsRuntimeManager.Instance;
      Assert.IsNotNull(vm, "JsRuntimeManager should exist in play mode");

      yield return null;

      if (vm != null)
        Assert.IsEmpty(vm.CapturedExceptions, "No JS exceptions after input bridge frame");

      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator MultiplePlayModeCycles_InputIntegration_Stable()
    {
      for (var cycle = 0; cycle < 2; cycle++)
      {
        yield return new EnterPlayMode();

        yield return null;

        var vm = JsRuntimeManager.Instance;
        if (vm != null)
          Assert.IsEmpty(vm.CapturedExceptions, $"No JS exceptions in cycle {cycle}");

        yield return new ExitPlayMode();
      }
    }
  }
}
