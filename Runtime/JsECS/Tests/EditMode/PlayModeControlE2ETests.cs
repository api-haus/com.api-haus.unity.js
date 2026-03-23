namespace UnityJS.Entities.EditModeTests
{
  using System.Collections;
  using System.Text.RegularExpressions;
  using NUnit.Framework;
  using Unity.Entities;
  using UnityEngine;
  using UnityEngine.TestTools;
  using UnityJS.Runtime;

  /// <summary>
  /// Control test: enters play mode with zero test activity and asserts
  /// no JS errors are produced. Any TDZ errors, bridge failures, or
  /// system script exceptions will fail this test.
  /// </summary>
  public class PlayModeControlE2ETests
  {
    [UnityTest]
    public IEnumerator EnterPlayMode_NoJsErrors()
    {
      // Expect no errors at all — if any system script throws,
      // LogAssert will catch it as "unhandled log message"
      yield return new EnterPlayMode();

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

      yield return new ExitPlayMode();
    }
  }
}
