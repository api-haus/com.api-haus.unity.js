namespace UnityJS.Integration.QuantumConsole.EditModeTests
{
  using System;
  using System.Collections;
  using System.IO;
  using NUnit.Framework;
  using QFSW.QC;
  using StoredPrefs;
  using Unity.Collections;
  using Unity.Entities;
  using UnityEngine.TestTools;
  using UnityJS.Entities.EditModeTests;
  using UnityJS.Runtime;

  public class TweakCmdE2ETests
  {
    const int INIT_FRAMES = 16;

    [UnityTest]
    public IEnumerator Param_NumberEnum_RegistersAndInvokes()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      var key = new FixedString32Bytes("e2e.numEnum");
      Assert.IsTrue(PrefsStore.TryGet(in key, out _), "e2e.numEnum must be registered by param()");

      PrefsStore.SetNumber(in key, 0);
      var result = (string)QuantumConsoleProcessor.InvokeCommand("e2e.numEnum 2");
      Assert.That(result, Does.Contain("2"), $"Expected confirmation, got: {result}");
      Assert.AreEqual(2, PrefsStore.GetNumber(in key), 0.001, "Value must be 2 after QC invoke");

      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Param_StringEnum_RegistersAndInvokes()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      var key = new FixedString32Bytes("e2e.strEnum");
      Assert.IsTrue(PrefsStore.TryGet(in key, out _), "e2e.strEnum must be registered by param()");

      var alpha = new FixedString64Bytes("alpha");
      PrefsStore.SetString(in key, in alpha);

      var result = (string)QuantumConsoleProcessor.InvokeCommand("e2e.strEnum beta");
      Assert.That(result, Does.Contain("beta"), $"Expected confirmation, got: {result}");
      Assert.AreEqual("beta", PrefsStore.GetString(in key).ToString());

      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Param_Range_InvokesAndRejects()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      var key = new FixedString32Bytes("e2e.range");
      Assert.IsTrue(PrefsStore.TryGet(in key, out _), "e2e.range must be registered by param()");

      var result = (string)QuantumConsoleProcessor.InvokeCommand("e2e.range 50");
      Assert.That(result, Does.Contain("50"), $"Expected confirmation, got: {result}");
      Assert.AreEqual(50, PrefsStore.GetNumber(in key), 0.001);

      var reject = (string)QuantumConsoleProcessor.InvokeCommand("e2e.range 999");
      Assert.That(reject, Does.Contain("must be between"), $"Expected range error, got: {reject}");
      Assert.AreEqual(50, PrefsStore.GetNumber(in key), 0.001, "Value must not change on rejected input");

      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Param_NumberEnum_RejectsInvalidValue()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      var key = new FixedString32Bytes("e2e.numEnum");
      Assert.IsTrue(PrefsStore.TryGet(in key, out _), "e2e.numEnum must be registered by param()");

      PrefsStore.SetNumber(in key, 1);
      var reject = (string)QuantumConsoleProcessor.InvokeCommand("e2e.numEnum 99");
      Assert.That(reject, Does.Contain("must be one of"), $"Expected enum error, got: {reject}");
      Assert.AreEqual(1, PrefsStore.GetNumber(in key), 0.001, "Value must not change on rejected input");

      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Param_Reregister_UpdatesConstraint()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      // Initial state: e2e.reload.update accepts [0, 1]
      var key = new FixedString32Bytes("e2e.reload.update");
      Assert.IsTrue(PrefsStore.TryGet(in key, out _), "e2e.reload.update must be registered");

      // Value 3 should be rejected with original constraint
      PrefsStore.SetNumber(in key, 0);
      var reject = (string)QuantumConsoleProcessor.InvokeCommand("e2e.reload.update 3");
      Assert.That(reject, Does.Contain("must be one of"), "3 should be rejected by [0,1] constraint");

      // Hot reload: mutate script to expand constraint to [0, 1, 2, 3]
      var jsPath = GetCompiledFixturePath("e2e_param_reload");
      var original = File.ReadAllText(jsPath);
      try
      {
        var mutated = original.Replace(
          "param('e2e.reload.update', [0, 1]",
          "param('e2e.reload.update', [0, 1, 2, 3]");
        File.WriteAllText(jsPath, mutated);

        var vm = JsRuntimeManager.Instance;
        var source = File.ReadAllText(jsPath);
        vm.ReloadScript("script:e2e_param_reload", source, jsPath);

        yield return null;

        // Now 3 should be accepted
        PrefsStore.SetNumber(in key, 0);
        var result = (string)QuantumConsoleProcessor.InvokeCommand("e2e.reload.update 3");
        Assert.That(result, Does.Contain("3"), $"3 should be accepted after constraint update, got: {result}");
        Assert.AreEqual(3, PrefsStore.GetNumber(in key), 0.001);

        // e2e.reload.keep should still work
        var keepKey = new FixedString32Bytes("e2e.reload.keep");
        PrefsStore.SetNumber(in keepKey, 0);
        var keepResult = (string)QuantumConsoleProcessor.InvokeCommand("e2e.reload.keep 1");
        Assert.That(keepResult, Does.Contain("1"), $"keep param should still work, got: {keepResult}");
      }
      finally
      {
        File.WriteAllText(jsPath, original);
      }

      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Param_DoubleRegister_NoError()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      var key = new FixedString32Bytes("e2e.reload.keep");
      Assert.IsTrue(PrefsStore.TryGet(in key, out _), "e2e.reload.keep must be registered");

      // Reload same script unchanged
      var jsPath = GetCompiledFixturePath("e2e_param_reload");
      var source = File.ReadAllText(jsPath);
      var vm = JsRuntimeManager.Instance;
      vm.ReloadScript("script:e2e_param_reload", source, jsPath);

      yield return null;

      // Must still work without double-registration errors
      PrefsStore.SetNumber(in key, 0);
      var result = (string)QuantumConsoleProcessor.InvokeCommand("e2e.reload.keep 1");
      Assert.That(result, Does.Contain("1"), $"Param must work after double registration, got: {result}");

      Assert.IsEmpty(vm.CapturedExceptions,
        $"No JS exceptions after reload: {string.Join("\n", vm.CapturedExceptions)}");

      yield return new ExitPlayMode();
    }

    static string GetCompiledFixturePath(string name)
    {
      var tscBuild = Path.Combine("Library", "TscBuild",
        "Packages", "com.api-haus.unity.js", "Fixtures~", "scripts", name + ".js");
      if (File.Exists(tscBuild))
        return Path.GetFullPath(tscBuild);
      return Path.GetFullPath(Path.Combine(
        SceneFixture.GetPackageFixturesSourcePath(), "scripts", name + ".js"));
    }
  }
}
