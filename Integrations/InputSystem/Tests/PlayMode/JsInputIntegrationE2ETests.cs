namespace UnityJS.Integration.InputSystem.PlayModeTests
{
  using System.Collections;
  using NUnit.Framework;
  using UnityEngine.TestTools;
  using UnityJS.Runtime;

  /// <summary>
  /// E2E tests verifying InputSystem integration works in play mode.
  /// </summary>
  [TestFixture]
  public class JsInputIntegrationE2ETests
  {
    [UnityTest]
    public IEnumerator PlayModeCycle_InputBridge_NoErrors()
    {
      var vm = JsRuntimeManager.Instance;
      Assert.IsNotNull(vm, "JsRuntimeManager should exist in play mode");

      yield return null;

      if (vm != null)
        Assert.IsEmpty(vm.CapturedExceptions, "No JS exceptions after input bridge frame");
    }
  }
}
