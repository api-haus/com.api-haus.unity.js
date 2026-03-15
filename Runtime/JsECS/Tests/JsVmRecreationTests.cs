namespace UnityJS.Entities.Tests
{
  using System.Runtime.InteropServices;
  using System.Text;
  using NUnit.Framework;
  using QJS;
  using Runtime;
  using Systems;
  using Unity.Entities;

  [TestFixture]
  public unsafe class JsVmRecreationTests
  {
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

    static int ReadGlobalInt(string name)
    {
      var vm = JsRuntimeManager.Instance;
      if (vm == null || !vm.IsValid)
        return -1;
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

    [TearDown]
    public void TearDown()
    {
      // Safety net: ensure no leaked VM singleton
      JsRuntimeManager.Instance?.Dispose();
    }

    [Test]
    public void SingleSession_SystemScriptsExecute()
    {
      var world = CreateSession("Single");
      Tick(world, 3);
      var count = ReadGlobalInt("_e2eAutoloadCount");
      Assert.Greater(count, 0, "e2e_autoload_probe.js should tick");
      world.Dispose();
    }

    [Test]
    public void TwoSessions_SystemScriptsExecuteOnBoth()
    {
      // Session 1
      var world1 = CreateSession("S1");
      Tick(world1, 3);
      var count1 = ReadGlobalInt("_e2eAutoloadCount");
      Assert.Greater(count1, 0, "Session 1: probe should tick");
      world1.Dispose(); // OnDestroy → VM disposed

      // Session 2 — fresh world, fresh VM
      var world2 = CreateSession("S2");
      Tick(world2, 3);
      var count2 = ReadGlobalInt("_e2eAutoloadCount");
      Assert.Greater(count2, 0, "Session 2: probe should tick after VM recreation");
      world2.Dispose();
    }

    [Test]
    public void ThreeSessions_AllExecute()
    {
      for (var session = 0; session < 3; session++)
      {
        var world = CreateSession($"S{session}");
        Tick(world, 3);
        var count = ReadGlobalInt("_e2eAutoloadCount");
        Assert.Greater(count, 0, $"Session {session}: probe should tick");
        world.Dispose();
      }
    }

    [Test]
    public void VmInstance_ChangesAcrossSessions()
    {
      var world1 = CreateSession("S1");
      Tick(world1);
      var vm1 = JsRuntimeManager.Instance;
      Assert.IsNotNull(vm1);
      world1.Dispose();

      Assert.IsNull(JsRuntimeManager.Instance, "VM should be null after world dispose");

      var world2 = CreateSession("S2");
      Tick(world2);
      var vm2 = JsRuntimeManager.Instance;
      Assert.IsNotNull(vm2);
      Assert.AreNotSame(vm1, vm2, "New session should have new VM instance");
      world2.Dispose();
    }
  }
}
