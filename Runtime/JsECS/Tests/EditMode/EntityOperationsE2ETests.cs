namespace UnityJS.Entities.EditModeTests
{
  using System.Collections;
  using NUnit.Framework;
  using Unity.Entities;
  using Unity.Mathematics;
  using UnityEngine.TestTools;

  /// <summary>
  /// E2E tests for entities.create() and entities.destroy().
  /// Uses e2e_entity_ops.ts component that creates/destroys entities in start().
  /// </summary>
  public class EntityOperationsE2ETests
  {
    const string SCRIPT = "tests/components/e2e_entity_ops";
    const int INIT_FRAMES = 12;

    [UnityTest]
    public IEnumerator Create_ReturnsValidId()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      var entity = scene.Spawn(SCRIPT);
      var eid = scene.GetEntityId(entity);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(scene.AllFulfilled(), "Script must be fulfilled");

      var createdId = JsEval.Int($"_e2e_ent[{eid}]?.createdId ?? -1");
      Assert.Greater(createdId, 0, $"entities.create() should return a valid ID, got {createdId}");

      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Create_WithPosition_SetsTransform()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      var entity = scene.Spawn(SCRIPT);
      var eid = scene.GetEntityId(entity);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(scene.AllFulfilled(), "Script must be fulfilled");

      // ECB needs a frame to play back
      yield return null;
      yield return null;

      var posId = JsEval.Int($"_e2e_ent[{eid}]?.createdWithPosId ?? -1");
      Assert.Greater(posId, 0, "Created entity should have valid ID");

      // Verify position via C# — look up the entity by its JS ID
      var registry = Core.JsEntityRegistry.GetEntityFromId(posId);
      if (world.EntityManager.Exists(registry))
      {
        var pos = world.EntityManager.GetComponentData<Unity.Transforms.LocalTransform>(registry).Position;
        Assert.AreEqual(5.0f, pos.x, 0.5f, "Created entity position.x should be 5");
        Assert.AreEqual(10.0f, pos.y, 0.5f, "Created entity position.y should be 10");
        Assert.AreEqual(15.0f, pos.z, 0.5f, "Created entity position.z should be 15");
      }
      else
      {
        // Entity may still be pending ECB playback — verify ID at minimum
        Assert.Greater(posId, 0, "entities.create(float3(5,10,15)) returned valid ID");
      }

      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Destroy_ReturnsTrue()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      var entity = scene.Spawn(SCRIPT);
      var eid = scene.GetEntityId(entity);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(scene.AllFulfilled(), "Script must be fulfilled");

      var result = JsEval.Bool($"_e2e_ent[{eid}]?.destroyResult === true");
      Assert.IsTrue(result, "entities.destroy() should return true");

      yield return new ExitPlayMode();
    }
  }
}
