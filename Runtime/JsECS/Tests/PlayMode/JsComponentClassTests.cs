namespace UnityJS.Entities.PlayModeTests
{
  using System.IO;
  using Core;
  using NUnit.Framework;
  using QJS;
  using Runtime;
  using UnityEngine;
  using UnityEngine.TestTools;

  [TestFixture]
  public unsafe class JsComponentClassTests : JsBridgeTestFixture
  {
    static readonly string s_testsPath = System.IO.Path.Combine(
      Application.streamingAssetsPath,
      "unity.js",
      "tests"
    );

    public override void SetUp()
    {
      base.SetUp();

      // Register component store (provides ecs.add/define/etc.)
      JsComponentStore.Register(Ctx);

      // Load the query builder + component glue
      m_Manager.LoadScriptFromString(
        "__ecs_query_builder",
        Systems.JsSystemRunner.QueryBuilderSourceForTests
      );
      m_Manager.LoadScriptFromString(
        "__ecs_component_glue",
        Systems.JsSystemRunner.ComponentGlueSourceForTests
      );

      JsScriptSearchPaths.AddSearchPath(s_testsPath, 0);
    }

    public override void TearDown()
    {
      JsScriptSearchPaths.RemoveSearchPath(s_testsPath);
      JsComponentStore.Shutdown();
      base.TearDown();
    }

    [Test]
    public void Component_BaseClass_ExistsOnEcs()
    {
      var result = EvalGlobalBool("typeof ecs.Component === 'function'");
      Assert.IsTrue(result, "ecs.Component should be a function");
    }

    [Test]
    public void Component_Subclass_InheritsJsComp()
    {
      EvalGlobalVoid(
        @"
        class MyComp extends ecs.Component {
          constructor() { super(); this.val = 10; }
        }
      "
      );
      var isJsComp = EvalGlobalBool("MyComp.__jsComp === true");
      Assert.IsTrue(isJsComp, "Subclass should inherit __jsComp");
    }

    [Test]
    public void Component_Name_DerivedFromClassName()
    {
      EvalGlobalVoid(
        @"
        class FancyComp extends ecs.Component {}
      "
      );
      var name = EvalGlobalBool("FancyComp.__name === 'FancyComp'");
      Assert.IsTrue(name, "__name should equal class name");
    }

    [Test]
    public void AddFromTS_StoresInstance()
    {
      EvalGlobalVoid(
        @"
        class TestStore extends ecs.Component {
          constructor() { super(); this.value = 99; }
        }
        ecs.add(1, TestStore);
      "
      );
      var val = EvalGlobalInt("TestStore.get(1).value");
      Assert.AreEqual(99, val);
    }

    [Test]
    public void AddFromTS_SetsEntity()
    {
      EvalGlobalVoid(
        @"
        class TestEntity extends ecs.Component {
          constructor() { super(); }
        }
        var _inst = ecs.add(1, TestEntity);
      "
      );
      var eid = EvalGlobalInt("_inst.entity");
      Assert.AreEqual(1, eid);
    }

    [Test]
    public void Start_CalledAfterAdd()
    {
      EvalGlobalVoid(
        @"
        class TestStart extends ecs.Component {
          constructor() { super(); this.started = false; this.value = 0; }
          start() { this.started = true; this.value = 42; }
        }
        var _startInst = ecs.add(1, TestStart);
      "
      );
      var started = EvalGlobalBool("_startInst.started");
      Assert.IsTrue(started, "start() should have been called");
      var val = EvalGlobalInt("_startInst.value");
      Assert.AreEqual(42, val);
    }

    [Test]
    public void Update_AutoTicked()
    {
      EvalGlobalVoid(
        @"
        class TestTick extends ecs.Component {
          constructor() { super(); this.ticks = 0; }
          update(dt) { this.ticks++; }
        }
        ecs.add(1, TestTick);
      "
      );

      // Simulate 5 ticks
      for (var i = 0; i < 5; i++)
        EvalGlobalVoid("__tickComponents('update', 0.016)");

      var ticks = EvalGlobalInt("TestTick.get(1).ticks");
      Assert.AreEqual(5, ticks);
    }

    [Test]
    public void OnDestroy_CalledViaCleanup()
    {
      EvalGlobalVoid("globalThis._testDestroyFlag = false");
      EvalGlobalVoid(
        @"
        class TestDestroy extends ecs.Component {
          constructor() { super(); }
          onDestroy() { globalThis._testDestroyFlag = true; }
        }
        ecs.add(1, TestDestroy);
      "
      );

      Assert.IsFalse(EvalGlobalBool("_testDestroyFlag"));

      // Simulate cleanup
      EvalGlobalVoid("__cleanupComponentEntity(1)");

      Assert.IsTrue(EvalGlobalBool("_testDestroyFlag"));
    }

    [Test]
    public void TickUnregister_AfterCleanup()
    {
      EvalGlobalVoid(
        @"
        class TestUnreg extends ecs.Component {
          constructor() { super(); this.ticks = 0; }
          update(dt) { this.ticks++; }
        }
        ecs.add(1, TestUnreg);
      "
      );

      // Tick 3 times
      for (var i = 0; i < 3; i++)
        EvalGlobalVoid("__tickComponents('update', 0.016)");

      Assert.AreEqual(3, EvalGlobalInt("TestUnreg.get(1).ticks"));

      // Cleanup — should unregister from tick list
      EvalGlobalVoid("__unregisterComponentTick(1)");

      // Tick 2 more times — should NOT increment
      for (var i = 0; i < 2; i++)
        EvalGlobalVoid("__tickComponents('update', 0.016)");

      Assert.AreEqual(3, EvalGlobalInt("TestUnreg.get(1).ticks"));
    }

    [Test]
    public void HotReload_ReDefine_NoError()
    {
      // Adding same component class twice should not error
      EvalGlobalVoid(
        @"
        class TestReload extends ecs.Component {
          constructor() { super(); this.v = 1; }
        }
        ecs.add(1, TestReload);
        ecs.add(2, TestReload);
      "
      );

      Assert.AreEqual(1, EvalGlobalInt("TestReload.get(1).v"));
      Assert.AreEqual(1, EvalGlobalInt("TestReload.get(2).v"));
    }

    [Test]
    public void AddWithData_OverridesDefaults()
    {
      EvalGlobalVoid(
        @"
        class TestData extends ecs.Component {
          constructor() { super(); this.speed = 2; this.hp = 100; }
        }
        var _dataInst = ecs.add(1, TestData, { speed: 10 });
      "
      );
      var speed = EvalGlobalInt("_dataInst.speed");
      var hp = EvalGlobalInt("_dataInst.hp");
      Assert.AreEqual(10, speed, "speed should be overridden");
      Assert.AreEqual(100, hp, "hp should keep default");
    }

    [Test]
    public void ModuleLoad_ComponentClass_InitWorks()
    {
      var scriptPath = Path.Combine(s_testsPath, "test_component_class.js");
      var source = File.ReadAllText(scriptPath);
      var scriptId = "test_component_class";

      Assert.IsTrue(
        m_Manager.LoadScriptAsModule(scriptId, source, scriptPath),
        "Module should load without errors"
      );

      var handled = m_Manager.TryComponentInit(scriptId, 1);
      Assert.IsTrue(handled, "__componentInit should detect default export Component class");

      // start() is deferred to first TickComponents — trigger it now
      m_Manager.TickComponents("update", 0f);

      var started = EvalGlobalBool("ecs.Component.get.call({name:'TestComp'}, 1).started");
      Assert.IsTrue(started, "start() should have been called on first tick after __componentInit");

      var value = EvalGlobalInt("ecs.Component.get.call({name:'TestComp'}, 1).value");
      Assert.AreEqual(42, value, "start() should have set value to 42");
    }

    [Test]
    public void ModuleLoad_NonComponent_FallsBackToLegacy()
    {
      var source = "export function onInit(state) { globalThis._legacyInitCalled = true; }";
      m_Manager.LoadScriptAsModule("test_legacy", source, "<test_legacy>");
      var handled = m_Manager.TryComponentInit("test_legacy", 1);
      Assert.IsFalse(handled, "Non-component module should not be handled by __componentInit");
    }

    [Test]
    public void ModuleLoad_ComponentUpdate_TicksViaGlobal()
    {
      // Inline the component source to avoid module-cache collision with other tests
      // that load the same file path
      var source =
        @"
import { Component } from 'unity.js/ecs'
export default class TickComp extends Component {
  constructor() { super(); this.ticks = 0; }
  update(dt) { this.ticks++; }
}
";
      Assert.IsTrue(
        m_Manager.LoadScriptAsModule("test_tick_module", source, "<test_tick>"),
        "Module should load without errors"
      );

      var handled = m_Manager.TryComponentInit("test_tick_module", 2);
      Assert.IsTrue(handled, "__componentInit should detect default export Component class");

      m_Manager.TickComponents("update", 0.016f);
      m_Manager.TickComponents("update", 0.016f);
      m_Manager.TickComponents("update", 0.016f);

      var ticks = EvalGlobalInt("ecs.Component.get.call({name:'TickComp'}, 2).ticks");
      Assert.AreEqual(
        3,
        ticks,
        "TickComponents should have ticked the module-loaded instance 3 times"
      );
    }

    [Test]
    public void ModuleLoad_QueryBuilder_WorksDuringModuleInit()
    {
      var source =
        @"
import { query } from 'unity.js/ecs';
const q = query().build();
export function getQuery() { return q !== undefined; }
";
      Assert.IsTrue(
        m_Manager.LoadScriptAsModule("test_toplevel_query", source, "<test>"),
        "Module with top-level query() should load without error"
      );
    }

    [Test]
    public void AutoFlush_DuplicateGet_ReturnsSameObject()
    {
      // Regression: multiple ecs.get() calls for the same (accessor, eid) in one
      // flush window must return the same JS object so modifications accumulate.
      // Before the fix, each get() created a new native-read object and pushed a
      // separate pending entry — the last (stale) entry overwrote valid writes.
      EvalGlobalVoid(
        @"
        // Mock accessor with get/set
        var _mockData = { value: 10 };
        var _mockWritten = null;
        var _mockAccessor = {
          __name: 'Mock',
          get: function(eid) { return { value: _mockData.value }; },
          set: function(eid, d) { _mockWritten = d; }
        };
      "
      );

      // Two get() calls for the same (accessor, eid) — must return same object
      var same = EvalGlobalBool(
        @"
        var a = ecs.get(_mockAccessor, 1);
        var b = ecs.get(_mockAccessor, 1);
        a === b;
      "
      );
      Assert.IsTrue(same, "Duplicate ecs.get() must return the same object reference");
    }

    [Test]
    public void AutoFlush_DuplicateGet_FlushesOnce()
    {
      // Verify that duplicate get() calls produce exactly one set() call on flush.
      EvalGlobalVoid(
        @"
        var _flushCount = 0;
        var _flushAccessor = {
          __name: 'FlushTest',
          get: function(eid) { return { x: 0 }; },
          set: function(eid, d) { _flushCount++; }
        };
      "
      );

      EvalGlobalVoid(
        @"
        ecs.get(_flushAccessor, 1);
        ecs.get(_flushAccessor, 1);
        ecs.get(_flushAccessor, 1);
        __flushRefRw();
      "
      );

      var count = EvalGlobalInt("_flushCount");
      Assert.AreEqual(
        1,
        count,
        "Three get() calls for same (accessor, eid) should produce exactly one set()"
      );
    }

    [Test]
    public void AutoFlush_DuplicateGet_LastModificationWins()
    {
      // The core regression scenario: start() reads but doesn't modify,
      // update() reads and modifies. The modification must survive the flush.
      EvalGlobalVoid(
        @"
        var _lastWritten = null;
        var _modAccessor = {
          __name: 'ModTest',
          get: function(eid) { return { pos: 0 }; },
          set: function(eid, d) { _lastWritten = d; }
        };
      "
      );

      EvalGlobalVoid(
        @"
        // Simulate start() — reads but doesn't modify
        var lt1 = ecs.get(_modAccessor, 1);
        // Simulate update() — reads and modifies
        var lt2 = ecs.get(_modAccessor, 1);
        lt2.pos = 42;
        __flushRefRw();
      "
      );

      var pos = EvalGlobalInt("_lastWritten.pos");
      Assert.AreEqual(42, pos, "Modification from second get() must survive flush");
    }

    [Test]
    public void AutoFlush_DifferentEntities_IndependentEntries()
    {
      // get() for different entity IDs must NOT share objects.
      EvalGlobalVoid(
        @"
        var _indepAccessor = {
          __name: 'Indep',
          get: function(eid) { return { id: eid }; },
          set: function(eid, d) {}
        };
      "
      );

      var independent = EvalGlobalBool(
        @"
        var e1 = ecs.get(_indepAccessor, 1);
        var e2 = ecs.get(_indepAccessor, 2);
        e1 !== e2 && e1.id === 1 && e2.id === 2;
      "
      );
      Assert.IsTrue(independent, "Different entity IDs must produce independent objects");
    }
  }
}
