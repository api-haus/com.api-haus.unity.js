namespace UnityJS.Entities.EditModeTests
{
  using System.Collections;
  using NUnit.Framework;
  using Unity.Entities;
  using Unity.Mathematics;
  using UnityEngine;
  using UnityEngine.TestTools;

  /// <summary>
  /// Gameplay lighthouse: character entity moves forward at constant speed.
  /// Proves: Component update() + LocalTransform write + input bridge null-safety.
  /// </summary>
  public class CharacterMovementE2ETests
  {
    const string SCRIPT = "tests/components/e2e_character";
    const int INIT_FRAMES = 5;
    const float DURATION = 3f;
    const float SPEED = 4f;

    [UnityTest]
    public IEnumerator Character_MovesForward()
    {
      yield return new EnterPlayMode();

      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);

      var entity = scene.Spawn(SCRIPT, float3.zero);

      // Initialize
      for (var i = 0; i < INIT_FRAMES; i++)
        yield return null;

      Assert.IsTrue(scene.AllFulfilled(), "Script must be fulfilled");

      // Run for DURATION seconds
      var timer = 0f;
      while (timer < DURATION)
      {
        yield return null;
        timer += Time.deltaTime;
      }

      // Assert: speed=4, direction=(0,0,1), time≈3s → z ≈ 12
      var pos = scene.GetPosition(entity);
      var expected = SPEED * DURATION; // 12

      Assert.Greater(pos.z, expected * 0.7f,
        $"Character should have moved ~{expected:F0} on Z, but z={pos.z:F2}");
      Assert.Less(math.abs(pos.x), 0.1f,
        $"Character should not move on X axis, but x={pos.x:F2}");

      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Character_TotalDistanceMatchesSpeed()
    {
      yield return new EnterPlayMode();

      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);

      var entity = scene.Spawn(SCRIPT, float3.zero);
      var eid = scene.GetEntityId(entity);

      for (var i = 0; i < INIT_FRAMES; i++)
        yield return null;

      Assert.IsTrue(scene.AllFulfilled(), "Script must be fulfilled");

      var timer = 0f;
      while (timer < DURATION)
      {
        yield return null;
        timer += Time.deltaTime;
      }


      var totalDist = JsEval.Double($"_e2e_char[{eid}]?.totalDist ?? -1");
      var expected = SPEED * DURATION; // 12

      // Tolerance: ±2 units for frame timing variance
      Assert.Greater(totalDist, expected - 2.0,
        $"Total distance should be ~{expected:F0}, got {totalDist:F2}");
      Assert.Less(totalDist, expected + 2.0,
        $"Total distance should be ~{expected:F0}, got {totalDist:F2}");

      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Input_NullSafe_NoCrash()
    {
      yield return new EnterPlayMode();

      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);

      var entity = scene.Spawn(SCRIPT, float3.zero);
      var eid = scene.GetEntityId(entity);

      for (var i = 0; i < INIT_FRAMES; i++)
        yield return null;

      Assert.IsTrue(scene.AllFulfilled(), "Script must be fulfilled");

      // Run a few frames — if input is null, it should not crash
      for (var i = 0; i < 30; i++)
        yield return null;


      var frameCount = JsEval.Int($"_e2e_char[{eid}]?.frameCount ?? -1");

      // Assert: ran without crashing, accumulated frames
      Assert.Greater(frameCount, 10,
        $"Character should have run 30+ frames, got {frameCount}");

      yield return new ExitPlayMode();
    }
  }
}
