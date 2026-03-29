namespace UnityJS.Integration.Spatial.PlayModeTests
{
  using System.Collections;
  using UnityJS.Entities.Tests;
  using NUnit.Framework;
  using Unity.Entities;
  using Unity.Mathematics;
  using UnityEngine;
  using UnityEngine.TestTools;

  /// <summary>
  /// E2E tests for spatial.query() (synchronous non-trigger query).
  /// Uses e2e_spatial_query_probe.ts which registers as agent and queries.
  /// </summary>
  public class SpatialQueryE2ETests
  {
    SceneFixture m_Scene;

    [SetUp]
    public void SetUp()
    {
      var world = World.DefaultGameObjectInjectionWorld;
      m_Scene = new SceneFixture(world);
    }

    [TearDown]
    public void TearDown()
    {
      m_Scene?.Dispose();
    }

    [UnityTest]
    public IEnumerator SpatialQuery_FindsSelf()
    {
      var entity = m_Scene.Spawn("components/e2e_spatial_query_probe", float3.zero);
      var eid = m_Scene.GetEntityId(entity);

      // Wait for spatial system to rebuild KDTree
      for (var i = 0; i < 16; i++) yield return null;
      for (var i = 0; i < 8; i++) yield return new WaitForFixedUpdate();
      for (var i = 0; i < 8; i++) yield return null;

      Assert.IsTrue(m_Scene.AllFulfilled(), "Script must be fulfilled");

      var error = JsEval.Bool($"!!_e2e_sq[{eid}]?.error");
      Assert.IsFalse(error,
        $"Spatial probe error: {JsEval.Int($"_e2e_sq[{eid}]?.error ?? 0")}");

      // After spatial system rebuilds, query should find at least self
      var queryCount = JsEval.Int($"_e2e_sq[{eid}]?.queryCount ?? -1");
      Assert.GreaterOrEqual(queryCount, 1,
        $"spatial.query() should find at least 1 entity (self), got {queryCount}");
    }

    [UnityTest]
    public IEnumerator SpatialQuery_FindsMultipleAgents()
    {
      // Spawn 3 probes close together — all should find each other
      var eids = new int[3];
      for (var i = 0; i < 3; i++)
      {
        var e = m_Scene.Spawn("components/e2e_spatial_query_probe", new float3(i * 0.5f, 0, 0));
        eids[i] = m_Scene.GetEntityId(e);
      }

      for (var i = 0; i < 16; i++) yield return null;
      for (var i = 0; i < 8; i++) yield return new WaitForFixedUpdate();
      for (var i = 0; i < 8; i++) yield return null;

      Assert.IsTrue(m_Scene.AllFulfilled(), "All scripts must be fulfilled");

      // Each probe queries radius 10 — should find all 3 (they're within 1 unit of each other)
      for (var i = 0; i < 3; i++)
      {
        var count = JsEval.Int($"_e2e_sq[{eids[i]}]?.queryCount ?? -1");
        Assert.GreaterOrEqual(count, 3,
          $"Probe {i} should find >= 3 agents (all nearby), got {count}");
      }
    }
  }
}
