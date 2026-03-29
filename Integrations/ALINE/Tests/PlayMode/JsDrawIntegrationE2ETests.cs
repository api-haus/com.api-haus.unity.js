namespace UnityJS.Integration.ALINE.PlayModeTests
{
  using System.Collections;
  using NUnit.Framework;
  using UnityEngine.TestTools;
  using UnityJS.Runtime;

  /// <summary>
  /// E2E test verifying ALINE draw integration produces no JS errors.
  /// </summary>
  [TestFixture]
  public class JsDrawIntegrationE2ETests
  {
    [UnityTest]
    public IEnumerator PlayModeCycle_DrawBridge_NoErrors()
    {
      var vm = JsRuntimeManager.Instance;
      Assert.IsNotNull(vm, "JsRuntimeManager should exist in play mode");

      yield return null;

      if (vm != null)
        Assert.IsEmpty(vm.CapturedExceptions, "No JS exceptions after draw bridge frame");
    }
  }
}
