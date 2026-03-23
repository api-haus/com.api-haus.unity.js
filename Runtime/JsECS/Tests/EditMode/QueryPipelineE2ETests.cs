namespace UnityJS.Entities.EditModeTests
{
  using System.Collections;
  using NUnit.Framework;
  using Unity.Entities;
  using Unity.Mathematics;
  using UnityEngine;
  using UnityEngine.TestTools;

  /// <summary>
  /// E2E tests for the query pipeline.
  /// - Write-back: e2e_mover component moves entity via ecs.get() → lt.Position mutation
  /// - Match count: e2e_wanderer entities queried inline via JS eval
  /// </summary>
  public class QueryPipelineE2ETests
  {
    const int INIT_FRAMES = 10;

    [UnityTest]
    public IEnumerator Query_WriteBack_PersistsToECS()
    {
      yield return new EnterPlayMode();

      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);

      // e2e_mover component moves entity by (1,0,0)*dt via ecs.get() write-back
      var entity = scene.Spawn("components/e2e_mover", float3.zero);

      for (var i = 0; i < INIT_FRAMES; i++)
        yield return null;

      Assert.IsTrue(scene.AllFulfilled(), "Script must be fulfilled");

      var startX = scene.GetPosition(entity).x;

      // Run for 2 seconds — component moves by (1,0,0)*dt each frame
      var timer = 0f;
      while (timer < 2f)
      {
        yield return null;
        timer += Time.deltaTime;
      }

      var endX = scene.GetPosition(entity).x;

      // At 1 u/s for 2s → ~2 units on X
      Assert.Greater(endX, startX + 0.5f,
        $"Entity should have moved on X axis via write-back. start={startX:F3}, end={endX:F3}");

      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Query_WandererEntities_AllMove()
    {
      // This proves queries + write-back work across multiple entities over time.
      // Uses e2e_wanderer — same as WanderingSlimesE2ETests but focused on
      // verifying the query pipeline (wanderer uses ecs.get(LocalTransform) internally).
      yield return new EnterPlayMode();

      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);

      const int count = 3;
      var entities = new Entity[count];
      for (var i = 0; i < count; i++)
        entities[i] = scene.Spawn("components/e2e_wanderer", float3.zero);

      for (var i = 0; i < INIT_FRAMES; i++)
        yield return null;

      Assert.IsTrue(scene.AllFulfilled(), "Scripts must be fulfilled");

      // Run 2 seconds
      var timer = 0f;
      while (timer < 2f)
      {
        yield return null;
        timer += Time.deltaTime;
      }

      // All 3 should have moved from origin (query read + write-back works)
      for (var i = 0; i < count; i++)
      {
        var dist = math.length(scene.GetPosition(entities[i]));
        Assert.Greater(dist, 0.3f,
          $"Entity {i} should have moved from origin via query write-back, dist={dist:F3}");
      }

      yield return new ExitPlayMode();
    }
  }
}
