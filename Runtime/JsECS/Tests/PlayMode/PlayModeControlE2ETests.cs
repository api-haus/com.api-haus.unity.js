namespace UnityJS.Entities.PlayModeTests
{
  using System.Collections;
  using NUnit.Framework;
  using UnityEngine.TestTools;
  using UnityJS.Runtime;

  /// <summary>
  /// Control test: verifies no JS errors are produced on a clean frame.
  /// Any TDZ errors, bridge failures, or system script exceptions will fail this test.
  /// </summary>
  public class PlayModeControlE2ETests
  {
    [UnityTest]
    public IEnumerator NoJsErrors()
    {
      // Let systems stabilize
      for (var i = 0; i < 10; i++)
        yield return null;

      // Explicit check: VM should have no captured exceptions
      var vm = JsRuntimeManager.Instance;
      if (vm != null && vm.CapturedExceptions.Count > 0)
      {
        Assert.Fail(
          $"JS exceptions on play mode entry:\n{string.Join("\n", vm.CapturedExceptions)}");
      }
    }
  }
}
