namespace UnityJS.Entities.EditModeTests
{
  using System.Collections;
  using NUnit.Framework;
  using Unity.Entities;
  using Unity.Mathematics;
  using UnityEngine;
  using UnityEngine.TestTools;

  /// <summary>
  /// Gameplay lighthouse: 10 deterministic wanderers cycling through
  /// 5 hardcoded waypoints at speed=3 for 5 seconds.
  /// </summary>
  public class WanderingSlimesE2ETests
  {
    const string SCRIPT = "tests/components/e2e_wanderer";
    const int SLIME_COUNT = 10;
    const int INIT_FRAMES = 5;

    [UnityTest]
    public IEnumerator AllSlimes_MoveFromOrigin()
    {
      yield return new EnterPlayMode();

      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);

      // Spawn 10 slimes at origin
      var entities = new Entity[SLIME_COUNT];
      var eids = new int[SLIME_COUNT];
      for (var i = 0; i < SLIME_COUNT; i++)
      {
        entities[i] = scene.Spawn(SCRIPT, float3.zero);
        eids[i] = scene.GetEntityId(entities[i]);
      }

      // Let pipeline initialize
      for (var i = 0; i < INIT_FRAMES; i++)
        yield return null;

      Assert.IsTrue(scene.AllFulfilled(), "All slime scripts must be fulfilled");

      // Run for ~5 seconds
      var timer = 0f;
      while (timer < 5f)
      {
        yield return null;
        timer += Time.deltaTime;
      }

      // Assert every slime moved from origin
      for (var i = 0; i < SLIME_COUNT; i++)
      {
        var pos = scene.GetPosition(entities[i]);
        var distFromOrigin = math.length(pos);
        Assert.Greater(distFromOrigin, 0.5f,
          $"Slime {i} (eid={eids[i]}) must have moved from origin, but is at {pos}");
      }

      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator AllSlimes_ReachMultipleWaypoints()
    {
      yield return new EnterPlayMode();

      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);

      var entities = new Entity[SLIME_COUNT];
      var eids = new int[SLIME_COUNT];
      for (var i = 0; i < SLIME_COUNT; i++)
      {
        entities[i] = scene.Spawn(SCRIPT, float3.zero);
        eids[i] = scene.GetEntityId(entities[i]);
      }

      for (var i = 0; i < INIT_FRAMES; i++)
        yield return null;

      Assert.IsTrue(scene.AllFulfilled(), "All scripts must be fulfilled");

      // Run for ~5 seconds
      var timer = 0f;
      while (timer < 5f)
      {
        yield return null;
        timer += Time.deltaTime;
      }

      // Assert waypoint progress
      // speed=3, waypoint distance ≈ 2.83 → ~0.94s per leg → ~5 legs in 5s

      for (var i = 0; i < SLIME_COUNT; i++)
      {
        var wpIndex = JsEval.Int($"_e2e_wander[{eids[i]}]?.waypointIndex ?? -1");
        Assert.GreaterOrEqual(wpIndex, 3,
          $"Slime {i} (eid={eids[i]}) should have reached at least waypoint 3, got {wpIndex}");
      }

      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Slimes_AccumulateDistance()
    {
      yield return new EnterPlayMode();

      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);

      var entities = new Entity[SLIME_COUNT];
      var eids = new int[SLIME_COUNT];
      for (var i = 0; i < SLIME_COUNT; i++)
      {
        entities[i] = scene.Spawn(SCRIPT, float3.zero);
        eids[i] = scene.GetEntityId(entities[i]);
      }

      for (var i = 0; i < INIT_FRAMES; i++)
        yield return null;

      Assert.IsTrue(scene.AllFulfilled(), "All scripts must be fulfilled");

      // Run for ~5 seconds
      var timer = 0f;
      while (timer < 5f)
      {
        yield return null;
        timer += Time.deltaTime;
      }

      // Assert cumulative distance
      // speed=3 over 5 seconds → total path ≈ 15 units (minus startup frames)

      for (var i = 0; i < SLIME_COUNT; i++)
      {
        var totalDist = JsEval.Double($"_e2e_wander[{eids[i]}]?.totalDist ?? -1");
        Assert.Greater(totalDist, 5.0,
          $"Slime {i} (eid={eids[i]}) should have traveled > 5 units, got {totalDist:F2}");
      }

      yield return new ExitPlayMode();
    }
  }
}
