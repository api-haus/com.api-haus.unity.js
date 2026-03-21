namespace UnityJS.Entities.PlayModeTests
{
  using System.Runtime.InteropServices;
  using System.Text;
  using Core;
  using NUnit.Framework;
  using QJS;
  using Runtime;
  using Systems;
  using Unity.Entities;
  using UnityEngine;
  using UnityEngine.TestTools;

  [TestFixture]
  public unsafe class JsHotReloadStressTests
  {
    // --------------- helpers ---------------

    static JsRuntimeManager CreateVm()
    {
      var vm = new JsRuntimeManager();
      vm.RegisterBridgeNow(JsECSBridge.RegisterFunctions);
      return vm;
    }

    static int ReadGlobalInt(JsRuntimeManager vm, string name)
    {
      var code = $"globalThis.{name} || 0";
      var src = Encoding.UTF8.GetBytes(code + '\0');
      var file = Encoding.UTF8.GetBytes("<test>\0");
      fixed (
        byte* pSrc = src,
          pFile = file
      )
      {
        var val = QJS.JS_Eval(vm.Context, pSrc, src.Length - 1, pFile, QJS.JS_EVAL_TYPE_GLOBAL);
        int result;
        QJS.JS_ToInt32(vm.Context, &result, val);
        QJS.JS_FreeValue(vm.Context, val);
        return result;
      }
    }

    static int ReadGlobalIntStatic(string name)
    {
      var vm = JsRuntimeManager.Instance;
      if (vm == null || !vm.IsValid)
        return -1;
      return ReadGlobalInt(vm, name);
    }

    static string MakeCounterScript(string globalName) =>
      $"export function onUpdate(state) {{ globalThis.{globalName} = (globalThis.{globalName} || 0) + 1; }}";

    static string MakeVersionScript(int version) =>
      $"export function onUpdate(state) {{ globalThis._version = {version}; }}";

    // --------------- E2E helpers (from JsVmRecreationTests) ---------------

    static World CreateSession(string name)
    {
      var world = new World(name);
      var simGroup = world.GetOrCreateSystemManaged<SimulationSystemGroup>();
      var ecb = world.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();
      var scripting = world.GetOrCreateSystemManaged<JsScriptingSystem>();
      var runner = world.GetOrCreateSystemManaged<JsSystemRunner>();
      simGroup.AddSystemToUpdateList(ecb);
      simGroup.AddSystemToUpdateList(scripting);
      simGroup.AddSystemToUpdateList(runner);
      simGroup.SortSystems();
      return world;
    }

    static void Tick(World world, int frames = 1)
    {
      var sim = world.GetExistingSystemManaged<SimulationSystemGroup>();
      for (var i = 0; i < frames; i++)
        sim.Update();
    }

    // --------------- teardown ---------------

    [TearDown]
    public void TearDown()
    {
      JsRuntimeManager.Instance?.Dispose();
    }

    // --------------- tests ---------------

    [Test]
    public void DirectVm_TenCycles_OnUpdateSucceeds()
    {
      const int cycles = 15;
      const int ticksPerCycle = 5;
      var script = MakeCounterScript("_count");

      for (var i = 0; i < cycles; i++)
      {
        var vm = CreateVm();
        Assert.IsTrue(
          vm.LoadScriptAsModule("system:probe", script, $"probe_{i}.js"),
          $"Cycle {i}: LoadScriptAsModule should succeed"
        );

        var stateRef = vm.CreateEntityState("system:probe", i);
        for (var t = 0; t < ticksPerCycle; t++)
          Assert.IsTrue(
            vm.CallFunction("system:probe", "onUpdate", stateRef),
            $"Cycle {i}, tick {t}: CallFunction should succeed"
          );

        Assert.AreEqual(
          ticksPerCycle,
          ReadGlobalInt(vm, "_count"),
          $"Cycle {i}: counter should be {ticksPerCycle}"
        );

        vm.Dispose();
      }
    }

    [Test]
    public void DirectVm_ScriptModification_BetweenReloads()
    {
      const int cycles = 5;

      for (var i = 0; i < cycles; i++)
      {
        // Cycle A: version 1
        var vm = CreateVm();
        Assert.IsTrue(vm.LoadScriptAsModule("system:ver", MakeVersionScript(1), $"ver_{i}a.js"));
        var stateRef = vm.CreateEntityState("system:ver", i * 2);
        Assert.IsTrue(vm.CallFunction("system:ver", "onUpdate", stateRef));
        Assert.AreEqual(1, ReadGlobalInt(vm, "_version"), $"Cycle {i}a: should be version 1");
        vm.Dispose();

        // Cycle B: version 2
        vm = CreateVm();
        Assert.IsTrue(vm.LoadScriptAsModule("system:ver", MakeVersionScript(2), $"ver_{i}b.js"));
        stateRef = vm.CreateEntityState("system:ver", i * 2 + 1);
        Assert.IsTrue(vm.CallFunction("system:ver", "onUpdate", stateRef));
        Assert.AreEqual(2, ReadGlobalInt(vm, "_version"), $"Cycle {i}b: should be version 2");
        vm.Dispose();
      }
    }

    [Test]
    public void DirectVm_SyntaxError_ThenFixedScript()
    {
      var vm = CreateVm();

      // Bad script — unclosed paren
      const string bad = "export function onUpdate( { }";
      LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("SyntaxError"));
      Assert.IsFalse(
        vm.LoadScriptAsModule("system:fix", bad, "bad.js"),
        "Syntax error script should fail to load"
      );

      // Good script
      var good = MakeCounterScript("_fixed");
      Assert.IsTrue(
        vm.LoadScriptAsModule("system:fix", good, "good.js"),
        "Fixed script should load"
      );

      var stateRef = vm.CreateEntityState("system:fix", 0);
      Assert.IsTrue(vm.CallFunction("system:fix", "onUpdate", stateRef));
      Assert.AreEqual(1, ReadGlobalInt(vm, "_fixed"), "Counter should be 1 after one call");

      vm.Dispose();
    }

    [Test]
    public void DirectVm_MultipleSystemsPartialFailure()
    {
      var vm = CreateVm();

      var scriptA = MakeCounterScript("_counterA");
      var scriptB = "export function onUpdate( { }"; // syntax error
      var scriptC = MakeCounterScript("_counterC");

      Assert.IsTrue(vm.LoadScriptAsModule("system:a", scriptA, "a.js"), "system:a should load");
      LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("SyntaxError"));
      Assert.IsFalse(vm.LoadScriptAsModule("system:b", scriptB, "b.js"), "system:b should fail");
      Assert.IsTrue(vm.LoadScriptAsModule("system:c", scriptC, "c.js"), "system:c should load");

      var stateA = vm.CreateEntityState("system:a", 0);
      var stateC = vm.CreateEntityState("system:c", 1);

      const int ticks = 3;
      for (var t = 0; t < ticks; t++)
      {
        Assert.IsTrue(vm.CallFunction("system:a", "onUpdate", stateA), $"Tick {t}: system:a");
        Assert.IsTrue(vm.CallFunction("system:c", "onUpdate", stateC), $"Tick {t}: system:c");
      }

      Assert.AreEqual(ticks, ReadGlobalInt(vm, "_counterA"), "system:a counter");
      Assert.AreEqual(ticks, ReadGlobalInt(vm, "_counterC"), "system:c counter");

      vm.Dispose();
    }

    [Test]
    public void DirectVm_StaleScriptId_AfterDispose()
    {
      // First VM: load and call successfully
      var vm1 = CreateVm();
      Assert.IsTrue(
        vm1.LoadScriptAsModule("system:probe", MakeCounterScript("_stale"), "stale.js")
      );
      var stateRef = vm1.CreateEntityState("system:probe", 0);
      Assert.IsTrue(vm1.CallFunction("system:probe", "onUpdate", stateRef));
      vm1.Dispose();

      // Second VM: call with the old scriptId before loading anything
      var vm2 = CreateVm();
      var result = vm2.CallFunction("system:probe", "onUpdate", stateRef);
      Assert.IsFalse(result, "Stale scriptId should return false on new VM (script not loaded)");
      vm2.Dispose();
    }

    [Test]
    public void FullE2E_FifteenSessions_AllExecute()
    {
      const int sessions = 15;

      for (var session = 0; session < sessions; session++)
      {
        var world = CreateSession($"Stress{session}");
        Tick(world, 3);
        var count = ReadGlobalIntStatic("_e2eAutoloadCount");
        Assert.Greater(count, 0, $"Session {session}: probe should tick");
        world.Dispose();
      }
    }
  }
}
