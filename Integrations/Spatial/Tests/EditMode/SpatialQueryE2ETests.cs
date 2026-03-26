namespace UnityJS.Integration.Spatial.EditModeTests
{
  using System.Collections;
  using Entities.EditModeTests;
  using Entities.Tests;
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
    [UnityTest]
    public IEnumerator SpatialQuery_FindsSelf()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);

      var entity = scene.Spawn("components/e2e_spatial_query_probe", float3.zero);
      var eid = scene.GetEntityId(entity);

      // Wait for spatial system to rebuild KDTree
      for (var i = 0; i < 16; i++) yield return null;
      for (var i = 0; i < 8; i++) yield return new WaitForFixedUpdate();
      for (var i = 0; i < 8; i++) yield return null;

      Assert.IsTrue(scene.AllFulfilled(), "Script must be fulfilled");

      var error = JsEval.Bool($"!!_e2e_sq[{eid}]?.error");
      Assert.IsFalse(error,
        $"Spatial probe error: {JsEval.Int($"_e2e_sq[{eid}]?.error ?? 0")}");

      // After spatial system rebuilds, query should find at least self
      var queryCount = JsEval.Int($"_e2e_sq[{eid}]?.queryCount ?? -1");
      Assert.GreaterOrEqual(queryCount, 1,
        $"spatial.query() should find at least 1 entity (self), got {queryCount}");

      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator SpatialQuery_FindsMultipleAgents()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);

      // Spawn 3 probes close together — all should find each other
      var eids = new int[3];
      for (var i = 0; i < 3; i++)
      {
        var e = scene.Spawn("components/e2e_spatial_query_probe", new float3(i * 0.5f, 0, 0));
        eids[i] = scene.GetEntityId(e);
      }

      for (var i = 0; i < 16; i++) yield return null;
      for (var i = 0; i < 8; i++) yield return new WaitForFixedUpdate();
      for (var i = 0; i < 8; i++) yield return null;

      Assert.IsTrue(scene.AllFulfilled(), "All scripts must be fulfilled");

      // Each probe queries radius 10 — should find all 3 (they're within 1 unit of each other)
      for (var i = 0; i < 3; i++)
      {
        var count = JsEval.Int($"_e2e_sq[{eids[i]}]?.queryCount ?? -1");
        Assert.GreaterOrEqual(count, 3,
          $"Probe {i} should find >= 3 agents (all nearby), got {count}");
      }

      yield return new ExitPlayMode();
    }
  }
}
