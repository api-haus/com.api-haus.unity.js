namespace UnityJS.Entities.EditModeTests
{
  using System.Collections;
  using System.Text.RegularExpressions;
  using NUnit.Framework;
  using Unity.Entities;
  using UnityEngine;
  using UnityEngine.TestTools;
  using UnityJS.Entities.Tests;
  using UnityJS.Runtime;

  public class TranspileErrorE2ETests
  {
    const string PROBE = "components/e2e_hot_reload_probe";
    const int INIT_FRAMES = 12;

    [UnityTest]
    public IEnumerator Transpile_BrokenTs_ErrorTrackedPerFile()
    {
      LogAssert.ignoreFailingMessages = true;
      yield return new EnterPlayMode();
      LogAssert.ignoreFailingMessages = false;

      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);

      scene.Spawn(PROBE);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(scene.AllFulfilled(), "Probe must fulfill");
      Assert.IsTrue(JsTranspiler.IsInitialized, "Transpiler must be initialized");

      // Transpile broken source — error must be tracked for the file
      LogAssert.Expect(LogType.Error, new Regex("\\[JsTranspiler\\]"));
      var broken = JsTranspiler.Transpile("export default class BROKEN {{{{{", "test_broken.ts");
      Assert.IsNull(broken, "Transpilation must fail for broken source");
      Assert.Greater(JsTranspiler.ErrorCount, 0, "Error count must be > 0");
      Assert.IsTrue(JsTranspiler.Errors.ContainsKey("test_broken.ts"), "Error must be tracked for file");

      LogAssert.ignoreFailingMessages = true;
      yield return new ExitPlayMode();
      LogAssert.ignoreFailingMessages = false;
    }

    [UnityTest]
    public IEnumerator Transpile_FixBrokenTs_ErrorClears()
    {
      LogAssert.ignoreFailingMessages = true;
      yield return new EnterPlayMode();
      LogAssert.ignoreFailingMessages = false;

      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);

      scene.Spawn(PROBE);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(scene.AllFulfilled(), "Probe must fulfill");

      // Transpile broken source
      LogAssert.Expect(LogType.Error, new Regex("\\[JsTranspiler\\]"));
      JsTranspiler.Transpile("export default class BROKEN {{{{{", "test_lifecycle.ts");
      Assert.AreEqual(1, JsTranspiler.ErrorCount, "One error tracked");
      Assert.IsTrue(JsTranspiler.Errors.ContainsKey("test_lifecycle.ts"));

      // Fix the file — transpile valid source for the same path
      var valid = JsTranspiler.Transpile("export const x: number = 42;", "test_lifecycle.ts");
      Assert.IsNotNull(valid, "Valid source must transpile");
      Assert.AreEqual(0, JsTranspiler.ErrorCount, "Error must clear after fix");
      Assert.IsFalse(JsTranspiler.Errors.ContainsKey("test_lifecycle.ts"), "File must be removed from errors");

      LogAssert.ignoreFailingMessages = true;
      yield return new ExitPlayMode();
      LogAssert.ignoreFailingMessages = false;
    }

    [UnityTest]
    public IEnumerator Transpile_PlayModeError_FixAndRecover()
    {
      LogAssert.ignoreFailingMessages = true;
      yield return new EnterPlayMode();
      LogAssert.ignoreFailingMessages = false;

      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);

      scene.Spawn(PROBE);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(scene.AllFulfilled(), "Probe must fulfill");

      var vm = JsRuntimeManager.Instance;
      Assert.IsNotNull(vm, "VM must exist in play mode");

      // Inject error for two different files
      LogAssert.Expect(LogType.Error, new Regex("\\[JsTranspiler\\]"));
      JsTranspiler.Transpile("syntax error {{{{", "file_a.ts");
      LogAssert.Expect(LogType.Error, new Regex("\\[JsTranspiler\\]"));
      JsTranspiler.Transpile("another broken ]]]]", "file_b.ts");
      Assert.AreEqual(2, JsTranspiler.ErrorCount, "Two files with errors");

      // Fix file_a only
      var fixedA = JsTranspiler.Transpile("export const a: number = 1;", "file_a.ts");
      Assert.IsNotNull(fixedA);
      Assert.AreEqual(1, JsTranspiler.ErrorCount, "Only file_b error remains");
      Assert.IsFalse(JsTranspiler.Errors.ContainsKey("file_a.ts"));
      Assert.IsTrue(JsTranspiler.Errors.ContainsKey("file_b.ts"));

      // Fix file_b
      var fixedB = JsTranspiler.Transpile("export const b: number = 2;", "file_b.ts");
      Assert.IsNotNull(fixedB);
      Assert.AreEqual(0, JsTranspiler.ErrorCount, "All errors cleared");

      LogAssert.ignoreFailingMessages = true;
      yield return new ExitPlayMode();
      LogAssert.ignoreFailingMessages = false;
    }
  }
}
