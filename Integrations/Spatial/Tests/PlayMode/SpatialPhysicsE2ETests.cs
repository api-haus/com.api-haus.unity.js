namespace MiniSpatial.PlayModeTests
{
  using System.Collections;
  using System.Collections.Generic;
  using NUnit.Framework;
  using Unity.Collections;
  using Unity.Entities;
  using Unity.Mathematics;
  using Unity.Physics;
  using Unity.Transforms;
  using UnityEngine;
  using UnityEngine.TestTools;

  public class SpatialPhysicsE2ETests
  {
    World m_World;
    EntityManager m_EM;
    List<Entity> m_Entities;
    List<BlobAssetReference<Unity.Physics.Collider>> m_Colliders;

    static readonly SpatialTag s_Tag = "test_phys";

    [UnitySetUp]
    public IEnumerator SetUp()
    {
      m_World = World.DefaultGameObjectInjectionWorld;
      m_EM = m_World.EntityManager;
      m_Entities = new List<Entity>();
      m_Colliders = new List<BlobAssetReference<Unity.Physics.Collider>>();
      yield return null;
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
      foreach (var e in m_Entities)
      {
        if (m_EM.Exists(e))
          m_EM.DestroyEntity(e);
      }
      m_Entities.Clear();

      foreach (var c in m_Colliders)
      {
        if (c.IsCreated)
          c.Dispose();
      }
      m_Colliders.Clear();

      yield return new WaitForFixedUpdate();
      yield return null;
    }

    Entity CreatePhysicsAgent(float3 position, float3 linearVelocity)
    {
      var entity = m_EM.CreateEntity();

      m_EM.AddComponentData(
        entity,
        new LocalTransform
        {
          Position = position,
          Rotation = quaternion.identity,
          Scale = 1f,
        }
      );
      m_EM.AddComponentData(
        entity,
        new LocalToWorld { Value = float4x4.TRS(position, quaternion.identity, new float3(1f)) }
      );

      var collider = Unity.Physics.SphereCollider.Create(
        new SphereGeometry { Center = float3.zero, Radius = 0.5f }
      );
      m_Colliders.Add(collider);

      m_EM.AddComponentData(entity, new PhysicsCollider { Value = collider });
      m_EM.AddComponentData(entity, PhysicsMass.CreateDynamic(collider.Value.MassProperties, 1f));
      m_EM.AddComponentData(
        entity,
        new PhysicsVelocity { Linear = linearVelocity, Angular = float3.zero }
      );
      m_EM.AddComponentData(entity, new PhysicsDamping { Linear = 0f, Angular = 0f });
      m_EM.AddComponentData(entity, new PhysicsGravityFactor { Value = 0f });
      m_EM.AddSharedComponentManaged(entity, new PhysicsWorldIndex(0));

      // Spatial agent — the only spatial touchpoint
      m_EM.AddComponentData(
        entity,
        new SpatialAgent { shape = SpatialShape.Sphere(1f), tag = s_Tag }
      );

      m_Entities.Add(entity);
      return entity;
    }

    IEnumerator WaitForTreeRebuild()
    {
      yield return null;
      yield return new WaitForFixedUpdate();
      yield return new WaitForFixedUpdate();
      yield return null;
    }

    int QueryCount(float3 center, float radiusSq)
    {
      var results = new NativeList<Entity>(16, Allocator.Temp);
      var query = new ShapeQuery
      {
        shape = SpatialShape.Sphere(radiusSq, center),
        results = results,
      };
      SpatialQuery.Range(s_Tag, ref query);
      int count = results.Length;
      results.Dispose();
      return count;
    }

    [UnityTest]
    public IEnumerator PhysicsBody_MovesIntoQueryRange()
    {
      // Entity starts at (30,0,0), velocity (-20,0,0), gravity off → moves toward origin
      // Query sphere at origin with radius² = 100 (radius 10)
      // After ~1s of sim the body is at ~(10,0,0) → outside radius 10
      // After ~1.5s it is at ~(0,0,0) → inside radius 10
      CreatePhysicsAgent(new float3(30, 0, 0), new float3(-20, 0, 0));

      yield return WaitForTreeRebuild();

      // Should NOT be in range yet — distance 30 > radius 10
      Assert.AreEqual(0, QueryCount(float3.zero, 100f), "Should start outside query range");

      // Let physics run — at 0.02s/tick, 60 ticks ≈ 1.2s, body travels ~24 units → at ~(6,0,0)
      for (int i = 0; i < 60; i++)
      {
        yield return new WaitForFixedUpdate();
      }
      // Extra frames for tree rebuild
      yield return WaitForTreeRebuild();

      Assert.AreEqual(1, QueryCount(float3.zero, 100f), "Body should have moved into query range");
    }

    [UnityTest]
    public IEnumerator PhysicsBody_StartsInside_FoundImmediately()
    {
      // Entity starts at (2,0,0), no velocity — already inside query sphere radius 5
      CreatePhysicsAgent(new float3(2, 0, 0), float3.zero);

      yield return WaitForTreeRebuild();

      Assert.AreEqual(
        1,
        QueryCount(float3.zero, 25f),
        "Body inside range should be found immediately"
      );
    }
  }
}
