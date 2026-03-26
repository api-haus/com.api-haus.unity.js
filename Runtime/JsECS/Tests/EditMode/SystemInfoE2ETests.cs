namespace UnityJS.Entities.EditModeTests
{
  using System.Collections;
  using NUnit.Framework;
  using Unity.Entities;
  using UnityEngine.TestTools;
  using UnityJS.Entities.Tests;

  /// <summary>
  /// E2E tests for system.deltaTime, system.time, system.random, system.randomInt.
  /// Uses e2e_sysinfo_probe.ts system.
  /// </summary>
  public class SystemInfoE2ETests
  {
    const int INIT_FRAMES = 10;

    [UnityTest]
    public IEnumerator DeltaTime_Positive()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      scene.SpawnBare();
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      var dt = JsEval.Double("_e2e_sysinfo?.dt ?? -1");
      Assert.Greater(dt, 0.0, "system.deltaTime() must be positive");
      Assert.Less(dt, 1.0, "system.deltaTime() must be < 1s");

      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Time_Increases()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      scene.SpawnBare();
      for (var i = 0; i < INIT_FRAMES + 10; i++) yield return null;

      var early = JsEval.Double("_e2e_sysinfo?.earlyTime ?? -1");
      var late = JsEval.Double("_e2e_sysinfo?.lateTime ?? -1");
      // earlyTime may be 0 on the very first frame — that's fine
      Assert.GreaterOrEqual(early, 0.0, "earlyTime must be >= 0");
      Assert.Greater(late, early, "time must increase over frames");

      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Random_InZeroOneRange()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      scene.SpawnBare();
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      var min = JsEval.Double("_e2e_sysinfo?.randomMin ?? -1");
      var max = JsEval.Double("_e2e_sysinfo?.randomMax ?? 2");
      Assert.GreaterOrEqual(min, 0.0, "random() min must be >= 0");
      Assert.Less(max, 1.0, "random() max must be < 1");

      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator RandomInt_InRequestedRange()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      scene.SpawnBare();
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      var min = JsEval.Int("_e2e_sysinfo?.intMin ?? -1");
      var max = JsEval.Int("_e2e_sysinfo?.intMax ?? -1");
      Assert.GreaterOrEqual(min, 5, "randomInt(5,10) min must be >= 5");
      Assert.LessOrEqual(max, 10, "randomInt(5,10) max must be <= 10");

      yield return new ExitPlayMode();
    }
  }
}
