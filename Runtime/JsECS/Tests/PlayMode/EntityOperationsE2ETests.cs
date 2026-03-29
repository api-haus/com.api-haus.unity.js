namespace UnityJS.Entities.PlayModeTests
{
  using System.Collections;
  using NUnit.Framework;
  using Unity.Entities;
  using Unity.Mathematics;
  using UnityEngine.TestTools;
  using UnityJS.Entities.Tests;

  public class EntityOperationsE2ETests
  {
    SceneFixture m_Scene;

    [SetUp]
    public void SetUp()
    {
      m_Scene = new SceneFixture(World.DefaultGameObjectInjectionWorld);
    }

    [TearDown]
    public void TearDown()
    {
      m_Scene?.Dispose();
    }

    const string SCRIPT = "components/e2e_entity_ops";
    const int INIT_FRAMES = 12;

    [UnityTest]
    public IEnumerator Create_ReturnsValidId()
    {
      var entity = m_Scene.Spawn(SCRIPT);
      var eid = m_Scene.GetEntityId(entity);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(m_Scene.AllFulfilled(), "Script must be fulfilled");

      var error = JsEval.Bool($"!!_e2e_ent[{eid}]?.error");
      Assert.IsFalse(error, $"Fixture error: {JsEval.Int($"_e2e_ent[{eid}]?.error ?? 0")}");

      var createdId = JsEval.Int($"_e2e_ent[{eid}]?.createdId ?? -1");
      Assert.Greater(createdId, 0, $"entities.create() must return positive ID, got {createdId}");

      // Also verify a second entity was created (with position)
      var posId = JsEval.Int($"_e2e_ent[{eid}]?.createdWithPosId ?? -1");
      Assert.Greater(posId, 0, "entities.create(pos) must return positive ID");
      Assert.AreNotEqual(createdId, posId, "Two create() calls must return different IDs");
    }

    [UnityTest]
    public IEnumerator Destroy_EntityActuallyRemoved()
    {
      var entity = m_Scene.Spawn(SCRIPT);
      var eid = m_Scene.GetEntityId(entity);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(m_Scene.AllFulfilled(), "Script must be fulfilled");

      var destroyResult = JsEval.Bool($"_e2e_ent[{eid}]?.destroyResult === true");
      Assert.IsTrue(destroyResult, "entities.destroy() must return true");

      var destroyTargetId = JsEval.Int($"_e2e_ent[{eid}]?.destroyTargetId ?? -1");
      Assert.Greater(destroyTargetId, 0, "Destroy target must have valid ID");

      // Wait for ECB playback
      for (var i = 0; i < 5; i++) yield return null;

      // Verify entity is gone from registry
      var destroyed = Core.JsEntityRegistry.GetEntityFromId(destroyTargetId);
      Assert.IsFalse(World.DefaultGameObjectInjectionWorld.EntityManager.Exists(destroyed),
        $"Destroyed entity (id={destroyTargetId}) must not exist after ECB playback");
    }
  }
}
