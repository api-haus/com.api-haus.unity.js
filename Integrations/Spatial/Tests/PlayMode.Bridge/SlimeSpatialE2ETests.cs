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
  /// Gameplay lighthouse: wandering slimes with spatial triggers + dynamic bodies.
  /// Proves: multi-component entities + spatial.trigger() + cross-entity interaction.
  /// </summary>
  public class SlimeSpatialE2ETests
  {
    const int SLIME_COUNT = 5;
    const int BODY_COUNT = 16;
    const float DURATION = 5f;

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

    /// <summary>
    /// Wait for fulfillment + spatial systems to stabilize.
    /// JsComponentInitSystem (Initialization) → start() → spatial.add() via ECB
    /// ECB playback → SpatialAgent component added
    /// SpatialQuerySystem (Simulation) → rebuild KDTree
    /// SpatialTriggerDispatchSystem (FixedStep) → detect overlaps
    /// </summary>
    static IEnumerator WaitForSpatialStabilization()
    {
      // Frames for fulfillment + ECB playback + KDTree rebuild
      for (var i = 0; i < 16; i++)
        yield return null;
      // Fixed updates for trigger dispatch
      for (var i = 0; i < 8; i++)
        yield return new WaitForFixedUpdate();
      // A few more frames for callbacks to fire
      for (var i = 0; i < 4; i++)
        yield return null;
    }

    [UnityTest]
    public IEnumerator Bodies_AreInfluenced()
    {
      // Spawn slimes at origin with wanderer + goo
      var slimeEids = new int[SLIME_COUNT];
      for (var i = 0; i < SLIME_COUNT; i++)
      {
        var slime = m_Scene.Spawn(
          new[] { "components/e2e_wanderer", "components/e2e_spatial_goo" },
          float3.zero);
        slimeEids[i] = m_Scene.GetEntityId(slime);
      }

      // Spawn bodies in a 4x4 grid centered around origin
      var bodyEids = new int[BODY_COUNT];
      var idx = 0;
      for (var x = -1; x <= 2; x++)
      for (var z = -1; z <= 2; z++)
      {
        var body = m_Scene.Spawn(
          "components/e2e_body_tracker",
          new float3(x * 1.5f, 0, z * 1.5f));
        bodyEids[idx++] = m_Scene.GetEntityId(body);
      }

      yield return WaitForSpatialStabilization();
      Assert.IsTrue(m_Scene.AllFulfilled(), "All scripts must be fulfilled");

      // Run for DURATION seconds
      var timer = 0f;
      while (timer < DURATION)
      {
        yield return null;
        timer += Time.deltaTime;
      }

      // Assert: at least some bodies moved
      var movedCount = 0;
      for (var i = 0; i < BODY_COUNT; i++)
      {
        var dist = JsEval.Double($"_e2e_bodies[{bodyEids[i]}]?.totalDist ?? 0");
        if (dist > 0.3)
          movedCount++;
      }

      Assert.Greater(movedCount, 0,
        "At least some dynamic bodies should have been moved by spatial goo");
    }

    [UnityTest]
    public IEnumerator Slimes_StillMoveWithSpatial()
    {
      var slimes = new Entity[SLIME_COUNT];
      for (var i = 0; i < SLIME_COUNT; i++)
      {
        slimes[i] = m_Scene.Spawn(
          new[] { "components/e2e_wanderer", "components/e2e_spatial_goo" },
          float3.zero);
      }

      for (var i = 0; i < 4; i++)
        m_Scene.Spawn("components/e2e_body_tracker", new float3(i, 0, 0));

      yield return WaitForSpatialStabilization();
      Assert.IsTrue(m_Scene.AllFulfilled(), "All scripts must be fulfilled");

      var timer = 0f;
      while (timer < DURATION)
      {
        yield return null;
        timer += Time.deltaTime;
      }

      for (var i = 0; i < SLIME_COUNT; i++)
      {
        var pos = m_Scene.GetPosition(slimes[i]);
        var dist = math.length(pos);
        Assert.Greater(dist, 0.5f,
          $"Slime {i} should have moved from origin but is at {pos}");
      }
    }

    [UnityTest]
    public IEnumerator Triggers_Fire()
    {
      // Single slime at origin with goo (radius=3)
      var slime = m_Scene.Spawn(
        new[] { "components/e2e_wanderer", "components/e2e_spatial_goo" },
        float3.zero);
      var slimeEid = m_Scene.GetEntityId(slime);

      // Place body right at origin — inside trigger radius from frame 1
      m_Scene.Spawn("components/e2e_body_tracker", float3.zero);

      yield return WaitForSpatialStabilization();
      Assert.IsTrue(m_Scene.AllFulfilled(), "All scripts must be fulfilled");

      // Run for 3 seconds — plenty of time for trigger enter/exit cycles
      var timer = 0f;
      while (timer < 3f)
      {
        yield return null;
        timer += Time.deltaTime;
      }

      var enters = JsEval.Int($"_e2e_goo[{slimeEid}]?.totalEnters ?? 0");

      Assert.Greater(enters, 0,
        $"Spatial trigger should have fired enter events, got {enters}");
    }
  }
}
