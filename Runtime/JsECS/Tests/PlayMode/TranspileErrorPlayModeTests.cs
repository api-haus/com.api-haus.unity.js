namespace UnityJS.Entities.PlayModeTests
{
  using System.Collections;
  using System.Text.RegularExpressions;
  using NUnit.Framework;
  using UnityEngine;
  using UnityEngine.TestTools;
  using UnityJS.Runtime;

  public class TranspileErrorPlayModeTests
  {
    [UnityTest]
    public IEnumerator Transpile_BrokenTs_ErrorTrackedPerFile()
    {
      yield return null; // let runtime initialize

      Assert.IsTrue(JsTranspiler.IsInitialized, "Transpiler must be initialized in play mode");

      LogAssert.Expect(LogType.Error, new Regex("\\[JsTranspiler\\]"));
      var broken = JsTranspiler.Transpile("export default class BROKEN {{{{{", "pm_test_broken.ts");
      Assert.IsNull(broken, "Transpilation must fail for broken source");
      Assert.Greater(JsTranspiler.ErrorCount, 0, "Error count must be > 0");
      Assert.IsTrue(JsTranspiler.Errors.ContainsKey("pm_test_broken.ts"), "Error must be tracked for file");
    }

    [UnityTest]
    public IEnumerator Transpile_FixClears_ErrorCount()
    {
      yield return null;

      // Transpile broken source
      LogAssert.Expect(LogType.Error, new Regex("\\[JsTranspiler\\]"));
      JsTranspiler.Transpile("syntax error {{{{", "pm_lifecycle_a.ts");
      LogAssert.Expect(LogType.Error, new Regex("\\[JsTranspiler\\]"));
      JsTranspiler.Transpile("another broken ]]]]", "pm_lifecycle_b.ts");

      var errCount = JsTranspiler.ErrorCount;
      Assert.GreaterOrEqual(errCount, 2, "At least two errors tracked");

      // Fix first file
      var fixedA = JsTranspiler.Transpile("export const a: number = 1;", "pm_lifecycle_a.ts");
      Assert.IsNotNull(fixedA);
      Assert.IsFalse(JsTranspiler.Errors.ContainsKey("pm_lifecycle_a.ts"), "Fixed file must be removed from errors");

      // Fix second file
      var fixedB = JsTranspiler.Transpile("export const b: number = 2;", "pm_lifecycle_b.ts");
      Assert.IsNotNull(fixedB);
      Assert.IsFalse(JsTranspiler.Errors.ContainsKey("pm_lifecycle_b.ts"), "Fixed file must be removed from errors");
    }
  }
}
