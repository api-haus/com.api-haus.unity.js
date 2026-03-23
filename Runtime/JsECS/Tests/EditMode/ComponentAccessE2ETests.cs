namespace UnityJS.Entities.EditModeTests
{
  using System.Collections;
  using NUnit.Framework;
  using Unity.Entities;
  using Unity.Mathematics;
  using UnityEngine.TestTools;

  /// <summary>
  /// E2E tests for JS-defined component access (ecs.define/add/get/has/remove).
  /// Uses e2e_component_access.ts which runs all operations in start().
  /// </summary>
  public class ComponentAccessE2ETests
  {
    const string SCRIPT = "components/e2e_component_access";
    const int INIT_FRAMES = 12;

    [UnityTest]
    public IEnumerator Define_CreatesComponent()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      var entity = scene.Spawn(SCRIPT);
      var eid = scene.GetEntityId(entity);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(scene.AllFulfilled(), "Script must be fulfilled");

      Assert.IsTrue(JsEval.Bool($"_e2e_comp[{eid}]?.defined === true"),
        "ecs.define() should succeed");
      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Add_AttachesToEntity()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      var entity = scene.Spawn(SCRIPT);
      var eid = scene.GetEntityId(entity);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(scene.AllFulfilled(), "Script must be fulfilled");

      Assert.IsFalse(JsEval.Bool($"_e2e_comp[{eid}]?.hasBefore === true"),
        "ecs.has() should be false before add");
      Assert.IsTrue(JsEval.Bool($"_e2e_comp[{eid}]?.hasAfter === true"),
        "ecs.has() should be true after add");
      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Get_ReturnsData()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      var entity = scene.Spawn(SCRIPT);
      var eid = scene.GetEntityId(entity);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(scene.AllFulfilled(), "Script must be fulfilled");

      Assert.AreEqual(50, JsEval.Int($"_e2e_comp[{eid}]?.getCurrent ?? -1"),
        "ecs.get() should return current=50");
      Assert.AreEqual(200, JsEval.Int($"_e2e_comp[{eid}]?.getMax ?? -1"),
        "ecs.get() should return max=200");
      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Remove_DetachesFromEntity()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      var entity = scene.Spawn(SCRIPT);
      var eid = scene.GetEntityId(entity);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(scene.AllFulfilled(), "Script must be fulfilled");

      Assert.IsFalse(JsEval.Bool($"_e2e_comp[{eid}]?.hasAfterRemove === true"),
        "ecs.has() should be false after remove");
      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Has_ChecksCSharpComponents()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      var entity = scene.Spawn(SCRIPT);
      var eid = scene.GetEntityId(entity);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(scene.AllFulfilled(), "Script must be fulfilled");

      Assert.IsTrue(JsEval.Bool($"_e2e_comp[{eid}]?.hasTransform === true"),
        "ecs.has() should detect C# LocalTransform component");
      yield return new ExitPlayMode();
    }
  }
}
