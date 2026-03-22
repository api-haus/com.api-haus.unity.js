namespace MiniSpatial.PlayModeTests
{
  using System.Collections;
  using System.Collections.Generic;
  using NUnit.Framework;
  using Unity.Collections;
  using Unity.Entities;
  using Unity.Mathematics;
  using Unity.Transforms;
  using UnityEngine;
  using UnityEngine.TestTools;

  public class SpatialQueryE2ETests
  {
    World m_World;
    EntityManager m_EM;
    List<Entity> m_Entities;

    static readonly SpatialTag s_Agent = "agent";
    static readonly SpatialTag s_Enemy = "enemy";
    static readonly SpatialTag s_Ally = "ally";

    [UnitySetUp]
    public IEnumerator SetUp()
    {
      m_World = World.DefaultGameObjectInjectionWorld;
      m_EM = m_World.EntityManager;
      m_Entities = new List<Entity>();
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

      yield return new WaitForFixedUpdate();
      yield return null;
    }

    #region Helpers

    Entity CreateAgent(float3 position, SpatialShape shape, SpatialTag tag)
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
      m_EM.AddComponentData(entity, new SpatialAgent { shape = shape, tag = tag });
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

    #endregion

    #region Basic System Integration

    [UnityTest]
    public IEnumerator SystemBuildsTree_FromAgentEntities()
    {
      CreateAgent(new float3(0, 0, 0), SpatialShape.Sphere(4f), s_Agent);
      CreateAgent(new float3(5, 0, 0), SpatialShape.Sphere(4f), s_Agent);
      CreateAgent(new float3(10, 0, 0), SpatialShape.Sphere(4f), s_Agent);

      yield return WaitForTreeRebuild();

      var results = new NativeList<Entity>(16, Allocator.Temp);
      var query = new ShapeQuery { shape = SpatialShape.Sphere(100f), results = results };
      SpatialQuery.Range(s_Agent, ref query);

      Assert.AreEqual(3, results.Length, "All 3 agents should be found");
      results.Dispose();
    }

    [UnityTest]
    public IEnumerator SystemBuildsTree_EmptyWorldNoError()
    {
      yield return WaitForTreeRebuild();

      var results = new NativeList<Entity>(16, Allocator.Temp);
      var query = new ShapeQuery { shape = SpatialShape.Sphere(100f), results = results };
      SpatialQuery.Range(s_Agent, ref query);

      Assert.AreEqual(0, results.Length);
      results.Dispose();
    }

    #endregion

    #region Sphere Queries

    [UnityTest]
    public IEnumerator SphereQuery_FindsNearbyAgents()
    {
      var near = CreateAgent(new float3(1, 0, 0), SpatialShape.Sphere(1f), s_Agent);
      CreateAgent(new float3(100, 0, 0), SpatialShape.Sphere(1f), s_Agent);

      yield return WaitForTreeRebuild();

      var results = new NativeList<Entity>(16, Allocator.Temp);
      var query = new ShapeQuery { shape = SpatialShape.Sphere(9f), results = results };
      SpatialQuery.Range(s_Agent, ref query);

      Assert.AreEqual(1, results.Length, "Only nearby agent should be found");
      Assert.AreEqual(near, results[0]);
      results.Dispose();
    }

    [UnityTest]
    public IEnumerator SphereQuery_FindsNone_WhenAllFar()
    {
      CreateAgent(new float3(50, 0, 0), SpatialShape.Sphere(1f), s_Agent);
      CreateAgent(new float3(-50, 0, 0), SpatialShape.Sphere(1f), s_Agent);

      yield return WaitForTreeRebuild();

      var results = new NativeList<Entity>(16, Allocator.Temp);
      var query = new ShapeQuery { shape = SpatialShape.Sphere(4f), results = results };
      SpatialQuery.Range(s_Agent, ref query);

      Assert.AreEqual(0, results.Length);
      results.Dispose();
    }

    #endregion

    #region Box Queries

    [UnityTest]
    public IEnumerator BoxQuery_FindsOverlappingAgents()
    {
      var inside = CreateAgent(
        new float3(1, 0, 0),
        SpatialShape.Box(new float3(0.5f, 0.5f, 0.5f)),
        s_Agent
      );
      CreateAgent(new float3(20, 0, 0), SpatialShape.Box(new float3(0.5f, 0.5f, 0.5f)), s_Agent);

      yield return WaitForTreeRebuild();

      var results = new NativeList<Entity>(16, Allocator.Temp);
      var query = new ShapeQuery
      {
        shape = SpatialShape.Box(new float3(3, 3, 3)),
        results = results,
      };
      SpatialQuery.Range(s_Agent, ref query);

      Assert.AreEqual(1, results.Length);
      Assert.AreEqual(inside, results[0]);
      results.Dispose();
    }

    #endregion

    #region Mixed Shape Queries

    [UnityTest]
    public IEnumerator SphereQueryAgainstBoxAgents()
    {
      var boxNear = CreateAgent(
        new float3(2, 0, 0),
        SpatialShape.Box(new float3(1, 1, 1)),
        s_Agent
      );
      CreateAgent(new float3(50, 0, 0), SpatialShape.Box(new float3(1, 1, 1)), s_Agent);

      yield return WaitForTreeRebuild();

      var results = new NativeList<Entity>(16, Allocator.Temp);
      var query = new ShapeQuery { shape = SpatialShape.Sphere(16f), results = results };
      SpatialQuery.Range(s_Agent, ref query);

      Assert.AreEqual(1, results.Length);
      Assert.AreEqual(boxNear, results[0]);
      results.Dispose();
    }

    [UnityTest]
    public IEnumerator BoxQueryAgainstSphereAgents()
    {
      var sphereNear = CreateAgent(new float3(1, 0, 0), SpatialShape.Sphere(1f), s_Agent);
      CreateAgent(new float3(50, 0, 0), SpatialShape.Sphere(1f), s_Agent);

      yield return WaitForTreeRebuild();

      var results = new NativeList<Entity>(16, Allocator.Temp);
      var query = new ShapeQuery
      {
        shape = SpatialShape.Box(new float3(3, 3, 3)),
        results = results,
      };
      SpatialQuery.Range(s_Agent, ref query);

      Assert.AreEqual(1, results.Length);
      Assert.AreEqual(sphereNear, results[0]);
      results.Dispose();
    }

    #endregion

    #region Tag Layers

    [UnityTest]
    public IEnumerator TagLayers_OnlyReturnsMatchingTag()
    {
      var e1 = CreateAgent(new float3(1, 0, 0), SpatialShape.Sphere(1f), s_Enemy);
      CreateAgent(new float3(2, 0, 0), SpatialShape.Sphere(1f), s_Ally);
      var e3 = CreateAgent(new float3(3, 0, 0), SpatialShape.Sphere(1f), s_Enemy);

      yield return WaitForTreeRebuild();

      var results = new NativeList<Entity>(16, Allocator.Temp);
      var query = new ShapeQuery { shape = SpatialShape.Sphere(100f), results = results };
      SpatialQuery.Range(s_Enemy, ref query);

      Assert.AreEqual(2, results.Length, "Only 2 enemies should be found");
      Assert.IsTrue(results.Contains(e1));
      Assert.IsTrue(results.Contains(e3));
      results.Dispose();
    }

    [UnityTest]
    public IEnumerator TagLayers_NoMatchReturnsEmpty()
    {
      SpatialTag npc = "npc";

      CreateAgent(new float3(1, 0, 0), SpatialShape.Sphere(1f), s_Enemy);
      CreateAgent(new float3(2, 0, 0), SpatialShape.Sphere(1f), s_Enemy);

      yield return WaitForTreeRebuild();

      var results = new NativeList<Entity>(16, Allocator.Temp);
      var query = new ShapeQuery { shape = SpatialShape.Sphere(100f), results = results };
      SpatialQuery.Range(npc, ref query);

      Assert.AreEqual(0, results.Length, "No NPCs exist, should find none");
      results.Dispose();
    }

    [UnityTest]
    public IEnumerator TagLayers_MultipleTagsIndependent()
    {
      CreateAgent(new float3(1, 0, 0), SpatialShape.Sphere(1f), s_Enemy);
      CreateAgent(new float3(2, 0, 0), SpatialShape.Sphere(1f), s_Ally);
      CreateAgent(new float3(50, 0, 0), SpatialShape.Sphere(1f), s_Enemy);

      yield return WaitForTreeRebuild();

      // Query enemies nearby
      var results1 = new NativeList<Entity>(16, Allocator.Temp);
      var q1 = new ShapeQuery { shape = SpatialShape.Sphere(25f), results = results1 };
      SpatialQuery.Range(s_Enemy, ref q1);
      Assert.AreEqual(1, results1.Length, "One nearby enemy");

      // Query allies nearby
      var results2 = new NativeList<Entity>(16, Allocator.Temp);
      var q2 = new ShapeQuery { shape = SpatialShape.Sphere(25f), results = results2 };
      SpatialQuery.Range(s_Ally, ref q2);
      Assert.AreEqual(1, results2.Length, "One nearby ally");

      // Query enemies at far position
      var results3 = new NativeList<Entity>(16, Allocator.Temp);
      var q3 = new ShapeQuery
      {
        shape = SpatialShape.Sphere(4f, new float3(50, 0, 0)),
        results = results3,
      };
      SpatialQuery.Range(s_Enemy, ref q3);
      Assert.AreEqual(1, results3.Length, "One enemy near (50,0,0)");

      results1.Dispose();
      results2.Dispose();
      results3.Dispose();
    }

    #endregion

    #region Dynamic Entity Changes

    [UnityTest]
    public IEnumerator TreeRebuilds_WhenEntityDestroyed()
    {
      var e1 = CreateAgent(new float3(1, 0, 0), SpatialShape.Sphere(1f), s_Agent);
      var e2 = CreateAgent(new float3(2, 0, 0), SpatialShape.Sphere(1f), s_Agent);

      yield return WaitForTreeRebuild();

      var results = new NativeList<Entity>(16, Allocator.Temp);
      var query = new ShapeQuery { shape = SpatialShape.Sphere(100f), results = results };
      SpatialQuery.Range(s_Agent, ref query);
      Assert.AreEqual(2, results.Length, "Both agents should be found initially");
      results.Dispose();

      m_EM.DestroyEntity(e1);
      m_Entities.Remove(e1);

      yield return WaitForTreeRebuild();

      results = new NativeList<Entity>(16, Allocator.Temp);
      query.results = results;
      SpatialQuery.Range(s_Agent, ref query);
      Assert.AreEqual(1, results.Length, "Only surviving agent should remain");
      Assert.AreEqual(e2, results[0]);
      results.Dispose();
    }

    [UnityTest]
    public IEnumerator TreeRebuilds_WhenEntityAdded()
    {
      CreateAgent(new float3(1, 0, 0), SpatialShape.Sphere(1f), s_Agent);

      yield return WaitForTreeRebuild();

      var results = new NativeList<Entity>(16, Allocator.Temp);
      var query = new ShapeQuery { shape = SpatialShape.Sphere(100f), results = results };
      SpatialQuery.Range(s_Agent, ref query);
      Assert.AreEqual(1, results.Length, "Initially one agent");
      results.Dispose();

      CreateAgent(new float3(2, 0, 0), SpatialShape.Sphere(1f), s_Agent);

      yield return WaitForTreeRebuild();

      results = new NativeList<Entity>(16, Allocator.Temp);
      query.results = results;
      SpatialQuery.Range(s_Agent, ref query);
      Assert.AreEqual(2, results.Length, "Second agent should now appear");
      results.Dispose();
    }

    #endregion

    #region Many Agents

    [UnityTest]
    public IEnumerator LargeAgentCount_CorrectQueryResults()
    {
      int totalAgents = 100;
      var rng = new Unity.Mathematics.Random(42);
      int expectedInRange = 0;

      for (int i = 0; i < totalAgents; i++)
      {
        var pos = rng.NextFloat3() * 200f - 100f;
        CreateAgent(pos, SpatialShape.Sphere(1f), s_Agent);
        float dist = math.length(pos);
        float queryRadius = 20f;
        float agentRadius = 1f;
        if (queryRadius + agentRadius > dist)
          expectedInRange++;
      }

      yield return WaitForTreeRebuild();

      var results = new NativeList<Entity>(totalAgents, Allocator.Temp);
      var query = new ShapeQuery { shape = SpatialShape.Sphere(400f), results = results };
      SpatialQuery.Range(s_Agent, ref query);

      Assert.AreEqual(
        expectedInRange,
        results.Length,
        $"Expected {expectedInRange} agents within range, got {results.Length}"
      );
      results.Dispose();
    }

    #endregion

    #region Entity Identity

    [UnityTest]
    public IEnumerator QueryResults_ContainCorrectEntities()
    {
      var e1 = CreateAgent(new float3(0, 0, 0), SpatialShape.Sphere(1f), s_Agent);
      var e2 = CreateAgent(new float3(1, 0, 0), SpatialShape.Sphere(1f), s_Agent);
      var e3 = CreateAgent(new float3(100, 0, 0), SpatialShape.Sphere(1f), s_Agent);

      yield return WaitForTreeRebuild();

      var results = new NativeList<Entity>(16, Allocator.Temp);
      var query = new ShapeQuery { shape = SpatialShape.Sphere(25f), results = results };
      SpatialQuery.Range(s_Agent, ref query);

      Assert.AreEqual(2, results.Length);
      Assert.IsTrue(results.Contains(e1), "e1 should be in results");
      Assert.IsTrue(results.Contains(e2), "e2 should be in results");
      Assert.IsFalse(results.Contains(e3), "e3 should NOT be in results");
      results.Dispose();
    }

    #endregion

    #region Agent Shape Offsets

    [UnityTest]
    public IEnumerator AgentWithShapeOffset_QueriedCorrectly()
    {
      CreateAgent(float3.zero, SpatialShape.Sphere(1f, new float3(10, 0, 0)), s_Agent);
      CreateAgent(new float3(10, 0, 0), SpatialShape.Sphere(1f), s_Agent);

      yield return WaitForTreeRebuild();

      var results = new NativeList<Entity>(16, Allocator.Temp);
      var query = new ShapeQuery
      {
        shape = SpatialShape.Sphere(4f, new float3(10, 0, 0)),
        results = results,
      };
      SpatialQuery.Range(s_Agent, ref query);

      Assert.AreEqual(2, results.Length, "Both agents resolve to world center (10,0,0)");
      results.Dispose();
    }

    #endregion
  }
}
