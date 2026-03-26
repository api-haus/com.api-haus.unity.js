namespace UnityJS.Entities.EditModeTests
{
  using System.Collections;
  using NUnit.Framework;
  using Unity.Entities;
  using UnityEngine;
  using UnityEngine.TestTools;
  using UnityJS.Entities.Tests;

  /// <summary>
  /// E2E tests for system auto-discovery and onUpdate execution.
  /// Uses execution_probe.ts — a system in tests/systems/ that counts frames.
  /// </summary>
  public class SystemExecutionE2ETests
  {
    const int INIT_FRAMES = 8;

    [UnityTest]
    public IEnumerator System_AutoDiscovered_RunsOnUpdate()
    {
      yield return new EnterPlayMode();

      // Systems auto-discover from systems/ dirs — no SceneFixture needed.
      // But we need a sentinel entity for JsSystemRunner to run.
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      scene.SpawnBare(); // sentinel

      for (var i = 0; i < INIT_FRAMES; i++)
        yield return null;

      // Reset after init
      JsEval.Void("_e2e_sys.updateCount = 0");

      const int framesToRun = 10;
      for (var i = 0; i < framesToRun; i++)
        yield return null;

      var count = JsEval.Int("_e2e_sys?.updateCount ?? -1");
      Assert.AreEqual(framesToRun, count,
        $"System onUpdate must be called once per frame ({framesToRun} frames)");

      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator System_ReceivesDeltaTime()
    {
      yield return new EnterPlayMode();

      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      scene.SpawnBare();

      for (var i = 0; i < INIT_FRAMES + 5; i++)
        yield return null;

      var dt = JsEval.Double("_e2e_sys?.lastDt ?? -1");
      Assert.Greater(dt, 0.0, "deltaTime must be positive");
      Assert.Less(dt, 1.0, "deltaTime must be less than 1 second");

      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator System_ElapsedTimeIncreases()
    {
      yield return new EnterPlayMode();

      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      scene.SpawnBare();

      for (var i = 0; i < INIT_FRAMES; i++)
        yield return null;

      // Reset and capture early elapsed
      JsEval.Void("_e2e_sys.updateCount = 0; _e2e_sys.earlyElapsed = 0");

      for (var i = 0; i < 20; i++)
        yield return null;

      var early = JsEval.Double("_e2e_sys?.earlyElapsed ?? -1");
      var late = JsEval.Double("_e2e_sys?.lastElapsed ?? -1");

      Assert.Greater(early, 0.0, "earlyElapsed must be positive");
      Assert.Greater(late, early, "elapsedTime must increase over frames");

      yield return new ExitPlayMode();
    }
  }
}
