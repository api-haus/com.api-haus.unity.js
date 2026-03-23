namespace UnityJS.Entities.EditModeTests
{
  using System.Collections;
  using NUnit.Framework;
  using Unity.Entities;
  using Unity.Mathematics;
  using UnityEngine;
  using UnityEngine.TestTools;

  /// <summary>
  /// E2E tests for ecs.query() pipeline. Uses e2e_query_probe.ts system that
  /// queries LocalTransform entities, counts matches, and moves them by (1,0,0)*dt.
  ///
  /// Assertions from first principles:
  ///   matchCount >= spawned entity count (query finds our entities)
  ///   Position.x > 0 after frames (write-back persists to ECS)
  ///   matchCount stable across frames (query not rebuilt)
  /// </summary>
  public class QueryPipelineE2ETests
  {
    const int INIT_FRAMES = 10;

    [UnityTest]
    public IEnumerator Query_WithAll_MatchesSpawnedEntities()
    {
      yield return new EnterPlayMode();

      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);

      // Spawn 5 entities with LocalTransform (SceneFixture always adds it)
      const int count = 5;
      for (var i = 0; i < count; i++)
        scene.SpawnBare(new float3(0, 0, i));

      for (var i = 0; i < INIT_FRAMES; i++)
        yield return null;

      var matchCount = JsEval.Int("_e2e_query?.matchCount ?? -1");
      Assert.GreaterOrEqual(matchCount, count,
        $"Query should match at least {count} entities, got {matchCount}");

      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Query_WriteBack_PersistsToECS()
    {
      yield return new EnterPlayMode();

      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);

      // e2e_mover component moves entity by (1,0,0)*dt via ecs.get() write-back
      var entity = scene.Spawn("tests/components/e2e_mover", float3.zero);

      for (var i = 0; i < INIT_FRAMES; i++)
        yield return null;

      Assert.IsTrue(scene.AllFulfilled(), "Script must be fulfilled");

      // Record start position
      var startX = scene.GetPosition(entity).x;

      // Run for 2 seconds — system moves by (1,0,0)*dt each frame
      var timer = 0f;
      while (timer < 2f)
      {
        yield return null;
        timer += Time.deltaTime;
      }

      var endX = scene.GetPosition(entity).x;

      // At 1 u/s for 2 seconds → ~2 units displacement on X
      Assert.Greater(endX, startX + 0.5f,
        $"Entity should have moved on X axis via query write-back. start={startX:F3}, end={endX:F3}");

      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Query_StableAcrossFrames()
    {
      yield return new EnterPlayMode();

      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);

      const int count = 3;
      for (var i = 0; i < count; i++)
        scene.SpawnBare();

      for (var i = 0; i < INIT_FRAMES; i++)
        yield return null;

      // Reset frame counter
      JsEval.Void("_e2e_query.frameCount = 0");

      var countEarly = JsEval.Int("_e2e_query?.matchCount ?? -1");

      // Run 10 more frames
      for (var i = 0; i < 10; i++)
        yield return null;

      var countLate = JsEval.Int("_e2e_query?.matchCount ?? -1");
      var frames = JsEval.Int("_e2e_query?.frameCount ?? -1");

      Assert.AreEqual(countEarly, countLate,
        "Query matchCount must be stable across frames (query built once at module scope)");
      Assert.AreEqual(10, frames, "System should have run exactly 10 frames");

      yield return new ExitPlayMode();
    }
  }
}
