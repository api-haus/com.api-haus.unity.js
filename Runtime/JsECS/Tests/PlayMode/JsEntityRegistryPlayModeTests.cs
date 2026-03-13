namespace UnityJS.Entities.PlayModeTests
{
  using System.Collections;
  using Core;
  using NUnit.Framework;
  using Unity.Collections;
  using Unity.Entities;
  using Unity.Mathematics;
  using Unity.Transforms;
  using UnityEngine.TestTools;
  using UnityJS.Entities.Components;

  public class JsEntityRegistryPlayModeTests
  {
    World m_World;
    EntityManager m_EntityManager;
    EntityCommandBufferSystem m_ECBSystem;

    [UnitySetUp]
    public IEnumerator SetUp()
    {
      m_World = World.DefaultGameObjectInjectionWorld;
      m_EntityManager = m_World.EntityManager;
      m_ECBSystem = m_World.GetExistingSystemManaged<EndSimulationEntityCommandBufferSystem>();

      if (!JsEntityRegistry.IsCreated)
        JsEntityRegistry.Initialize(16);
      else
        JsEntityRegistry.Clear();

      yield return null;
    }

    EntityCommandBuffer CreateECB() => m_ECBSystem.CreateCommandBuffer();

    [UnityTearDown]
    public IEnumerator TearDown()
    {
      JsEntityRegistry.Clear();
      var query = m_EntityManager.CreateEntityQuery(typeof(JsEntityId));
      m_EntityManager.DestroyEntity(query);
      yield return null;
    }

    #region Create / Pending State

    [UnityTest]
    public IEnumerator Create_ReturnsPositiveId()
    {
      var ecb = CreateECB();
      var id = JsEntityRegistry.Create(float3.zero, ecb);
      Assert.Greater(id, 0);
      yield return null;
    }

    [UnityTest]
    public IEnumerator Create_IdsAreSequential()
    {
      var ecb = CreateECB();
      var id1 = JsEntityRegistry.Create(float3.zero, ecb);
      var id2 = JsEntityRegistry.Create(float3.zero, ecb);
      var id3 = JsEntityRegistry.Create(float3.zero, ecb);
      Assert.AreEqual(id1 + 1, id2);
      Assert.AreEqual(id2 + 1, id3);
      yield return null;
    }

    [UnityTest]
    public IEnumerator Create_EntityIsPendingBeforeCommit()
    {
      var ecb = CreateECB();
      var id = JsEntityRegistry.Create(float3.zero, ecb);
      Assert.IsTrue(JsEntityRegistry.IsPending(id));
      Assert.IsTrue(JsEntityRegistry.Contains(id));
      Assert.AreEqual(0, JsEntityRegistry.Count);
      yield return null;
    }

    [UnityTest]
    public IEnumerator Create_EntityResolvedAfterCommit()
    {
      var ecb = CreateECB();
      var id = JsEntityRegistry.Create(float3.zero, ecb);
      yield return null;
      JsEntityRegistry.CommitPendingCreations(m_EntityManager);
      Assert.IsFalse(JsEntityRegistry.IsPending(id));
      Assert.IsTrue(JsEntityRegistry.Contains(id));
      Assert.AreEqual(1, JsEntityRegistry.Count);
      var entity = JsEntityRegistry.GetEntityFromId(id);
      Assert.AreNotEqual(Entity.Null, entity);
      Assert.IsTrue(m_EntityManager.Exists(entity));
    }

    [UnityTest]
    public IEnumerator Create_MultipleEntitiesInSingleFrame()
    {
      var ecb = CreateECB();
      var ids = new int[10];
      for (var i = 0; i < 10; i++)
        ids[i] = JsEntityRegistry.Create(new float3(i, 0, 0), ecb);
      yield return null;
      JsEntityRegistry.CommitPendingCreations(m_EntityManager);
      Assert.AreEqual(10, JsEntityRegistry.Count);
      for (var i = 0; i < 10; i++)
      {
        var entity = JsEntityRegistry.GetEntityFromId(ids[i]);
        Assert.AreNotEqual(Entity.Null, entity);
        var pos = m_EntityManager.GetComponentData<LocalTransform>(entity).Position;
        Assert.AreEqual(i, pos.x, 0.001f);
      }
    }

    #endregion

    #region Destroy / Pending Destruction

    [UnityTest]
    public IEnumerator Destroy_CommittedEntity()
    {
      var ecb = CreateECB();
      var id = JsEntityRegistry.Create(float3.zero, ecb);
      yield return null;
      JsEntityRegistry.CommitPendingCreations(m_EntityManager);
      var entity = JsEntityRegistry.GetEntityFromId(id);
      Assert.IsTrue(m_EntityManager.Exists(entity));
      ecb = CreateECB();
      var destroyed = JsEntityRegistry.Destroy(id, ecb);
      Assert.IsTrue(destroyed);
      Assert.IsTrue(JsEntityRegistry.IsMarkedForDestruction(id));
      Assert.IsTrue(JsEntityRegistry.Contains(id));
      yield return null;
      JsEntityRegistry.CommitPendingDestructions();
      Assert.IsFalse(JsEntityRegistry.Contains(id));
      Assert.AreEqual(Entity.Null, JsEntityRegistry.GetEntityFromId(id));
    }

    [UnityTest]
    public IEnumerator Destroy_PendingEntity_SameFrame()
    {
      var ecb = CreateECB();
      var id = JsEntityRegistry.Create(float3.zero, ecb);
      Assert.IsTrue(JsEntityRegistry.IsPending(id));
      var destroyed = JsEntityRegistry.Destroy(id, ecb);
      Assert.IsTrue(destroyed);
      Assert.IsFalse(JsEntityRegistry.IsPending(id));
      Assert.IsFalse(JsEntityRegistry.Contains(id));
      yield return null;
      JsEntityRegistry.CommitPendingCreations(m_EntityManager);
      JsEntityRegistry.CommitPendingDestructions();
      Assert.AreEqual(0, JsEntityRegistry.Count);
    }

    [UnityTest]
    public IEnumerator Destroy_NonExistentEntity_ReturnsFalse()
    {
      var ecb = CreateECB();
      var destroyed = JsEntityRegistry.Destroy(999, ecb);
      Assert.IsFalse(destroyed);
      yield return null;
    }

    [UnityTest]
    public IEnumerator Destroy_MultipleEntitiesInSingleFrame()
    {
      var ecb = CreateECB();
      var ids = new int[5];
      for (var i = 0; i < 5; i++)
        ids[i] = JsEntityRegistry.Create(float3.zero, ecb);
      yield return null;
      JsEntityRegistry.CommitPendingCreations(m_EntityManager);
      Assert.AreEqual(5, JsEntityRegistry.Count);
      ecb = CreateECB();
      foreach (var id in ids)
        JsEntityRegistry.Destroy(id, ecb);
      yield return null;
      JsEntityRegistry.CommitPendingDestructions();
      Assert.AreEqual(0, JsEntityRegistry.Count);
    }

    #endregion

    #region Register / RegisterImmediate

    [UnityTest]
    public IEnumerator Register_ExistingEntity_AssignsId()
    {
      var ecb = CreateECB();
      var entity = m_EntityManager.CreateEntity(typeof(LocalTransform));
      var id = JsEntityRegistry.Register(entity, ecb);
      Assert.Greater(id, 0);
      Assert.IsTrue(JsEntityRegistry.Contains(id));
      Assert.AreEqual(entity, JsEntityRegistry.GetEntityFromId(id));
      yield return null;
    }

    [UnityTest]
    public IEnumerator Register_SameEntityTwice_ReturnsSameId()
    {
      var ecb = CreateECB();
      var entity = m_EntityManager.CreateEntity(typeof(LocalTransform));
      var id1 = JsEntityRegistry.Register(entity, ecb);
      var id2 = JsEntityRegistry.Register(entity, ecb);
      Assert.AreEqual(id1, id2);
      Assert.AreEqual(1, JsEntityRegistry.Count);
      yield return null;
    }

    [UnityTest]
    public IEnumerator RegisterImmediate_UpdatesNextIdCounter()
    {
      var ecb = CreateECB();
      var entity = m_EntityManager.CreateEntity(typeof(LocalTransform));
      m_EntityManager.AddComponentData(entity, new JsEntityId { value = 100 });
      JsEntityRegistry.RegisterImmediate(entity, 100, m_EntityManager);
      var newId = JsEntityRegistry.Create(float3.zero, ecb);
      Assert.AreEqual(101, newId);
      yield return null;
    }

    [UnityTest]
    public IEnumerator RegisterImmediate_LowerId_DoesNotDecrementCounter()
    {
      var ecb = CreateECB();
      JsEntityRegistry.Create(float3.zero, ecb);
      JsEntityRegistry.Create(float3.zero, ecb);
      JsEntityRegistry.Create(float3.zero, ecb);
      var entity = m_EntityManager.CreateEntity(typeof(LocalTransform));
      JsEntityRegistry.RegisterImmediate(entity, 1, m_EntityManager);
      var nextId = JsEntityRegistry.Create(float3.zero, ecb);
      Assert.AreEqual(4, nextId);
      yield return null;
    }

    #endregion

    #region GetEntity / GetId Lookups

    [UnityTest]
    public IEnumerator GetEntityFromId_InvalidIds_ReturnsNull()
    {
      Assert.AreEqual(Entity.Null, JsEntityRegistry.GetEntityFromId(0));
      Assert.AreEqual(Entity.Null, JsEntityRegistry.GetEntityFromId(-1));
      Assert.AreEqual(Entity.Null, JsEntityRegistry.GetEntityFromId(-999));
      Assert.AreEqual(Entity.Null, JsEntityRegistry.GetEntityFromId(int.MinValue));
      yield return null;
    }

    [UnityTest]
    public IEnumerator GetEntityFromId_NonExistentId_ReturnsNull()
    {
      Assert.AreEqual(Entity.Null, JsEntityRegistry.GetEntityFromId(999));
      yield return null;
    }

    [UnityTest]
    public IEnumerator GetIdFromEntity_NullEntity_ReturnsNegativeOne()
    {
      Assert.AreEqual(-1, JsEntityRegistry.GetIdFromEntity(Entity.Null));
      yield return null;
    }

    [UnityTest]
    public IEnumerator GetIdFromEntity_UnregisteredEntity_ReturnsNegativeOne()
    {
      var entity = m_EntityManager.CreateEntity(typeof(LocalTransform));
      Assert.AreEqual(-1, JsEntityRegistry.GetIdFromEntity(entity));
      yield return null;
    }

    [UnityTest]
    public IEnumerator BidirectionalLookup_Consistent()
    {
      var ecb = CreateECB();
      var id = JsEntityRegistry.Create(float3.zero, ecb);
      yield return null;
      JsEntityRegistry.CommitPendingCreations(m_EntityManager);
      var entity = JsEntityRegistry.GetEntityFromId(id);
      var lookupId = JsEntityRegistry.GetIdFromEntity(entity);
      Assert.AreEqual(id, lookupId);
    }

    #endregion

    #region Entity Version Safety

    [UnityTest]
    public IEnumerator SyncWithWorld_DetectsDestroyedEntity_AutoCleanup()
    {
      var ecb = CreateECB();
      var id = JsEntityRegistry.Create(float3.zero, ecb);
      yield return null;
      JsEntityRegistry.CommitPendingCreations(m_EntityManager);
      var entity = JsEntityRegistry.GetEntityFromId(id);
      Assert.AreNotEqual(Entity.Null, entity);
      m_EntityManager.DestroyEntity(entity);
      JsEntityRegistry.SyncWithWorld(m_EntityManager);
      Assert.AreEqual(Entity.Null, JsEntityRegistry.GetEntityFromId(id));
      Assert.IsFalse(JsEntityRegistry.Contains(id));
    }

    [UnityTest]
    public IEnumerator SyncWithWorld_RemovesExternallyDestroyedEntities()
    {
      var ecb = CreateECB();
      var id1 = JsEntityRegistry.Create(float3.zero, ecb);
      var id2 = JsEntityRegistry.Create(float3.zero, ecb);
      var id3 = JsEntityRegistry.Create(float3.zero, ecb);
      yield return null;
      JsEntityRegistry.CommitPendingCreations(m_EntityManager);
      Assert.AreEqual(3, JsEntityRegistry.Count);
      var entity2 = JsEntityRegistry.GetEntityFromId(id2);
      m_EntityManager.DestroyEntity(entity2);
      JsEntityRegistry.SyncWithWorld(m_EntityManager);
      Assert.AreEqual(2, JsEntityRegistry.Count);
      Assert.IsTrue(JsEntityRegistry.Contains(id1));
      Assert.IsFalse(JsEntityRegistry.Contains(id2));
      Assert.IsTrue(JsEntityRegistry.Contains(id3));
    }

    #endregion

    #region Commit Order and Edge Cases

    [UnityTest]
    public IEnumerator CommitCreations_CalledTwice_NoDuplicates()
    {
      var ecb = CreateECB();
      JsEntityRegistry.Create(float3.zero, ecb);
      yield return null;
      JsEntityRegistry.CommitPendingCreations(m_EntityManager);
      var countAfterFirst = JsEntityRegistry.Count;
      JsEntityRegistry.CommitPendingCreations(m_EntityManager);
      var countAfterSecond = JsEntityRegistry.Count;
      Assert.AreEqual(countAfterFirst, countAfterSecond);
    }

    [UnityTest]
    public IEnumerator CommitDestructions_CalledTwice_Safe()
    {
      var ecb = CreateECB();
      var id = JsEntityRegistry.Create(float3.zero, ecb);
      yield return null;
      JsEntityRegistry.CommitPendingCreations(m_EntityManager);
      ecb = CreateECB();
      JsEntityRegistry.Destroy(id, ecb);
      yield return null;
      JsEntityRegistry.CommitPendingDestructions();
      JsEntityRegistry.CommitPendingDestructions();
      Assert.AreEqual(0, JsEntityRegistry.Count);
    }

    [UnityTest]
    public IEnumerator CommitCreations_EmptyPending_NoOp()
    {
      JsEntityRegistry.CommitPendingCreations(m_EntityManager);
      Assert.AreEqual(0, JsEntityRegistry.Count);
      yield return null;
    }

    [UnityTest]
    public IEnumerator CommitDestructions_EmptyPending_NoOp()
    {
      JsEntityRegistry.CommitPendingDestructions();
      Assert.AreEqual(0, JsEntityRegistry.Count);
      yield return null;
    }

    #endregion

    #region GetAll / Enumeration

    [UnityTest]
    public IEnumerator GetAllIds_ReturnsOnlyCommitted()
    {
      var ecb = CreateECB();
      JsEntityRegistry.Create(float3.zero, ecb);
      JsEntityRegistry.Create(float3.zero, ecb);
      var idsBefore = JsEntityRegistry.GetAllIds(Allocator.Persistent);
      Assert.AreEqual(0, idsBefore.Length);
      idsBefore.Dispose();
      yield return null;
      JsEntityRegistry.CommitPendingCreations(m_EntityManager);
      var idsAfter = JsEntityRegistry.GetAllIds(Allocator.Persistent);
      Assert.AreEqual(2, idsAfter.Length);
      idsAfter.Dispose();
    }

    [UnityTest]
    public IEnumerator GetAllEntities_ReturnsOnlyCommitted()
    {
      var ecb = CreateECB();
      JsEntityRegistry.Create(float3.zero, ecb);
      JsEntityRegistry.Create(float3.zero, ecb);
      var entitiesBefore = JsEntityRegistry.GetAllEntities(Allocator.Persistent);
      Assert.AreEqual(0, entitiesBefore.Length);
      entitiesBefore.Dispose();
      yield return null;
      JsEntityRegistry.CommitPendingCreations(m_EntityManager);
      var entitiesAfter = JsEntityRegistry.GetAllEntities(Allocator.Persistent);
      Assert.AreEqual(2, entitiesAfter.Length);
      entitiesAfter.Dispose();
    }

    #endregion

    #region Capacity / Stress

    [UnityTest]
    public IEnumerator Create_ExceedsInitialCapacity_Grows()
    {
      var ecb = CreateECB();
      for (var i = 0; i < 50; i++)
        JsEntityRegistry.Create(float3.zero, ecb);
      yield return null;
      JsEntityRegistry.CommitPendingCreations(m_EntityManager);
      Assert.AreEqual(50, JsEntityRegistry.Count);
    }

    #endregion

    #region Clear / Lifecycle

    [UnityTest]
    public IEnumerator Clear_ResetsState()
    {
      var ecb = CreateECB();
      JsEntityRegistry.Create(float3.zero, ecb);
      JsEntityRegistry.Create(float3.zero, ecb);
      yield return null;
      JsEntityRegistry.CommitPendingCreations(m_EntityManager);
      Assert.AreEqual(2, JsEntityRegistry.Count);
      JsEntityRegistry.Clear();
      Assert.AreEqual(0, JsEntityRegistry.Count);
      Assert.IsTrue(JsEntityRegistry.IsCreated);
      ecb = CreateECB();
      var newId = JsEntityRegistry.Create(float3.zero, ecb);
      Assert.AreEqual(1, newId);
      yield return null;
    }

    #endregion

    #region Complex Scenarios

    [UnityTest]
    public IEnumerator CreateDestroyCreate_SameFrame_IdsRemainUnique()
    {
      var ecb = CreateECB();
      var id1 = JsEntityRegistry.Create(float3.zero, ecb);
      JsEntityRegistry.Destroy(id1, ecb);
      var id2 = JsEntityRegistry.Create(float3.zero, ecb);
      JsEntityRegistry.Destroy(id2, ecb);
      var id3 = JsEntityRegistry.Create(float3.zero, ecb);
      Assert.AreNotEqual(id1, id2);
      Assert.AreNotEqual(id2, id3);
      Assert.AreNotEqual(id1, id3);
      yield return null;
      JsEntityRegistry.CommitPendingCreations(m_EntityManager);
      JsEntityRegistry.CommitPendingDestructions();
      Assert.AreEqual(1, JsEntityRegistry.Count);
      Assert.IsTrue(JsEntityRegistry.Contains(id3));
    }

    [UnityTest]
    public IEnumerator MixedRegisterAndCreate_IdsRemainUnique()
    {
      var ecb = CreateECB();
      var existing1 = m_EntityManager.CreateEntity(typeof(LocalTransform));
      var existing2 = m_EntityManager.CreateEntity(typeof(LocalTransform));
      var regId1 = JsEntityRegistry.Register(existing1, ecb);
      var createId1 = JsEntityRegistry.Create(float3.zero, ecb);
      var regId2 = JsEntityRegistry.Register(existing2, ecb);
      var createId2 = JsEntityRegistry.Create(float3.zero, ecb);
      var allIds = new[] { regId1, createId1, regId2, createId2 };
      for (var i = 0; i < allIds.Length; i++)
      for (var j = i + 1; j < allIds.Length; j++)
        Assert.AreNotEqual(allIds[i], allIds[j]);
      yield return null;
    }

    [UnityTest]
    public IEnumerator PendingEntity_VisibleViaContains_NotViaCount()
    {
      var ecb = CreateECB();
      var id = JsEntityRegistry.Create(float3.zero, ecb);
      Assert.IsTrue(JsEntityRegistry.Contains(id));
      Assert.AreEqual(0, JsEntityRegistry.Count);
      Assert.IsTrue(JsEntityRegistry.IsPending(id));
      yield return null;
      JsEntityRegistry.CommitPendingCreations(m_EntityManager);
      Assert.IsTrue(JsEntityRegistry.Contains(id));
      Assert.AreEqual(1, JsEntityRegistry.Count);
      Assert.IsFalse(JsEntityRegistry.IsPending(id));
    }

    #endregion
  }
}
