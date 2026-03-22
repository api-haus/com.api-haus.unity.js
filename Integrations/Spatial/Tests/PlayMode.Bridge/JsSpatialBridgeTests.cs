namespace UnityJS.Integration.Spatial.PlayModeTests
{
  using System.Collections;
  using MiniSpatial;
  using NUnit.Framework;
  using Unity.Entities;
  using Unity.Mathematics;
  using Unity.Transforms;
  using UnityEngine;
  using UnityEngine.TestTools;
  using UnityJS.Entities.Components;
  using UnityJS.Entities.PlayModeTests;

  public class JsSpatialBridgeTests : JsEcsTestFixture
  {
    #region Helpers

    IEnumerator WaitForTreeRebuild()
    {
      yield return null;
      yield return new WaitForFixedUpdate();
      yield return new WaitForFixedUpdate();
      yield return null;
    }

    #endregion

    #region spatial.add

    [UnityTest]
    public IEnumerator Add_Sphere_EntityGetsSpatialAgent()
    {
      var entity = CreateRegisteredEntity(ComponentType.ReadWrite<JsEntityId>());
      var eid = GetEntityId(entity);

      SetupBurstContext();

      var ok = EvalBool($"spatial.add({eid}, \"enemy\", spatial.sphere(5))");
      Assert.IsTrue(ok);

      PlaybackAndCleanupEcb();

      yield return null;

      Assert.IsTrue(m_Em.HasComponent<SpatialAgent>(entity));
      var agent = m_Em.GetComponentData<SpatialAgent>(entity);
      Assert.AreEqual(SpatialShapeType.Sphere, agent.shape.type);
      Assert.AreEqual(25f, agent.shape.sphere.radiusSq, 0.001f);
      SpatialTag expectedTag = "enemy";
      Assert.AreEqual((int)expectedTag, agent.tag);
    }

    [UnityTest]
    public IEnumerator Add_Box_EntityGetsSpatialAgent()
    {
      var entity = CreateRegisteredEntity(ComponentType.ReadWrite<JsEntityId>());
      var eid = GetEntityId(entity);

      SetupBurstContext();

      var ok = EvalBool($"spatial.add({eid}, \"ally\", spatial.box(float3(2, 3, 4)))");
      Assert.IsTrue(ok);

      PlaybackAndCleanupEcb();

      yield return null;

      Assert.IsTrue(m_Em.HasComponent<SpatialAgent>(entity));
      var agent = m_Em.GetComponentData<SpatialAgent>(entity);
      Assert.AreEqual(SpatialShapeType.Box, agent.shape.type);
      Assert.AreEqual(2f, agent.shape.box.halfExtents.x, 0.001f);
      Assert.AreEqual(3f, agent.shape.box.halfExtents.y, 0.001f);
      Assert.AreEqual(4f, agent.shape.box.halfExtents.z, 0.001f);
      SpatialTag expectedTag = "ally";
      Assert.AreEqual((int)expectedTag, agent.tag);
    }

    #endregion

    #region spatial.get

    [UnityTest]
    public IEnumerator Get_Sphere_ReturnsCorrectFields()
    {
      var entity = CreateRegisteredEntity(
        ComponentType.ReadWrite<JsEntityId>(),
        ComponentType.ReadWrite<SpatialAgent>()
      );
      SpatialTag tag = "enemy";
      m_Em.SetComponentData(
        entity,
        new SpatialAgent { shape = SpatialShape.Sphere(25f, new float3(1, 2, 3)), tag = tag }
      );
      var eid = GetEntityId(entity);

      SetupBurstContext();

      var shapeType = EvalInt($"spatial.get({eid}).shape");
      Assert.AreEqual(0, shapeType);

      var radiusSq = EvalFloat($"spatial.get({eid}).radiusSq");
      Assert.AreEqual(25.0, radiusSq, 0.001);

      var cx = EvalFloat($"spatial.get({eid}).center.x");
      Assert.AreEqual(1.0, cx, 0.001);

      var cy = EvalFloat($"spatial.get({eid}).center.y");
      Assert.AreEqual(2.0, cy, 0.001);

      var cz = EvalFloat($"spatial.get({eid}).center.z");
      Assert.AreEqual(3.0, cz, 0.001);

      var tagVal = EvalInt($"spatial.get({eid}).tag");
      Assert.AreEqual((int)tag, tagVal);

      PlaybackAndCleanupEcb();
      yield return null;
    }

    [UnityTest]
    public IEnumerator Get_Box_ReturnsCorrectFields()
    {
      var entity = CreateRegisteredEntity(
        ComponentType.ReadWrite<JsEntityId>(),
        ComponentType.ReadWrite<SpatialAgent>()
      );
      SpatialTag tag = "ally";
      m_Em.SetComponentData(
        entity,
        new SpatialAgent
        {
          shape = SpatialShape.Box(new float3(2, 3, 4), new float3(5, 6, 7)),
          tag = tag,
        }
      );
      var eid = GetEntityId(entity);

      SetupBurstContext();

      var shapeType = EvalInt($"spatial.get({eid}).shape");
      Assert.AreEqual(1, shapeType);

      var hx = EvalFloat($"spatial.get({eid}).halfExtents.x");
      Assert.AreEqual(2.0, hx, 0.001);

      var hy = EvalFloat($"spatial.get({eid}).halfExtents.y");
      Assert.AreEqual(3.0, hy, 0.001);

      var hz = EvalFloat($"spatial.get({eid}).halfExtents.z");
      Assert.AreEqual(4.0, hz, 0.001);

      var cx = EvalFloat($"spatial.get({eid}).center.x");
      Assert.AreEqual(5.0, cx, 0.001);

      PlaybackAndCleanupEcb();
      yield return null;
    }

    [UnityTest]
    public IEnumerator Get_NonAgent_ReturnsUndefined()
    {
      var entity = CreateRegisteredEntity(ComponentType.ReadWrite<JsEntityId>());
      var eid = GetEntityId(entity);

      SetupBurstContext();

      var isUndef = EvalBool($"spatial.get({eid}) === undefined");
      Assert.IsTrue(isUndef);

      PlaybackAndCleanupEcb();
      yield return null;
    }

    #endregion

    #region spatial.query

    [UnityTest]
    public IEnumerator Query_ReturnsCorrectEntityIds()
    {
      var e1 = CreateRegisteredEntity(
        ComponentType.ReadWrite<JsEntityId>(),
        ComponentType.ReadWrite<SpatialAgent>(),
        ComponentType.ReadWrite<LocalToWorld>()
      );
      m_Em.SetComponentData(
        e1,
        new SpatialAgent { shape = SpatialShape.Sphere(1f), tag = (SpatialTag)"enemy" }
      );
      m_Em.SetComponentData(
        e1,
        new LocalToWorld
        {
          Value = float4x4.TRS(new float3(1, 0, 0), quaternion.identity, new float3(1f)),
        }
      );
      m_Em.SetComponentData(e1, LocalTransform.FromPosition(new float3(1, 0, 0)));

      var e2 = CreateRegisteredEntity(
        ComponentType.ReadWrite<JsEntityId>(),
        ComponentType.ReadWrite<SpatialAgent>(),
        ComponentType.ReadWrite<LocalToWorld>()
      );
      m_Em.SetComponentData(
        e2,
        new SpatialAgent { shape = SpatialShape.Sphere(1f), tag = (SpatialTag)"enemy" }
      );
      m_Em.SetComponentData(
        e2,
        new LocalToWorld
        {
          Value = float4x4.TRS(new float3(2, 0, 0), quaternion.identity, new float3(1f)),
        }
      );
      m_Em.SetComponentData(e2, LocalTransform.FromPosition(new float3(2, 0, 0)));

      yield return WaitForTreeRebuild();

      SetupBurstContext();

      EvalVoid("var __qr = spatial.query(\"enemy\", spatial.sphere(10))");
      var len = EvalInt("__qr.length");
      Assert.AreEqual(2, len);

      var id1 = GetEntityId(e1);
      var id2 = GetEntityId(e2);
      var hasId1 = EvalBool($"__qr.indexOf({id1}) >= 0");
      var hasId2 = EvalBool($"__qr.indexOf({id2}) >= 0");
      Assert.IsTrue(hasId1, "Should contain entity 1");
      Assert.IsTrue(hasId2, "Should contain entity 2");

      PlaybackAndCleanupEcb();
    }

    [UnityTest]
    public IEnumerator Query_NoMatches_ReturnsEmptyArray()
    {
      yield return WaitForTreeRebuild();

      SetupBurstContext();

      var len = EvalInt("spatial.query(\"ghost\", spatial.sphere(100)).length");
      Assert.AreEqual(0, len);

      PlaybackAndCleanupEcb();
    }

    #endregion
  }
}
