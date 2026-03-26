namespace UnityJS.Entities.EditModeTests
{
  using System.Collections;
  using NUnit.Framework;
  using Unity.Entities;
  using Unity.Mathematics;
  using UnityEngine.TestTools;
  using UnityJS.Entities.Tests;

  /// <summary>
  /// E2E tests for multiple scripts on one entity.
  /// Uses e2e_multi_a.ts + e2e_multi_b.ts — two independent components.
  /// </summary>
  public class MultiScriptE2ETests
  {
    const int INIT_FRAMES = 10;

    [UnityTest]
    public IEnumerator TwoComponents_BothInitialized()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);

      var entity = scene.Spawn(
        new[] { "components/e2e_multi_a", "components/e2e_multi_b" });
      var eid = scene.GetEntityId(entity);

      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(scene.AllFulfilled(), "Both scripts must be fulfilled");

      Assert.AreEqual(1, JsEval.Int($"_e2e_multi_a[{eid}]?.startCount ?? -1"), "A.start() once");
      Assert.AreEqual(1, JsEval.Int($"_e2e_multi_b[{eid}]?.startCount ?? -1"), "B.start() once");

      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator TwoComponents_IndependentUpdate()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);

      var entity = scene.Spawn(
        new[] { "components/e2e_multi_a", "components/e2e_multi_b" });
      var eid = scene.GetEntityId(entity);

      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(scene.AllFulfilled(), "Both scripts must be fulfilled");

      JsEval.Void($"_e2e_multi_a[{eid}].updateCount = 0; _e2e_multi_b[{eid}].updateCount = 0");

      const int frames = 5;
      for (var i = 0; i < frames; i++) yield return null;

      Assert.AreEqual(frames, JsEval.Int($"_e2e_multi_a[{eid}]?.updateCount ?? -1"), "A updates per frame");
      Assert.AreEqual(frames, JsEval.Int($"_e2e_multi_b[{eid}]?.updateCount ?? -1"), "B updates per frame");

      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator TwoComponents_BothDestroyed()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);

      var entity = scene.Spawn(
        new[] { "components/e2e_multi_a", "components/e2e_multi_b" });
      var eid = scene.GetEntityId(entity);

      for (var i = 0; i < INIT_FRAMES + 3; i++) yield return null;
      Assert.IsTrue(scene.AllFulfilled(), "Both scripts must be fulfilled");

      world.EntityManager.DestroyEntity(entity);
      for (var i = 0; i < 3; i++) yield return null;

      Assert.AreEqual(1, JsEval.Int($"_e2e_multi_a[{eid}]?.destroyCount ?? -1"), "A.onDestroy() once");
      Assert.AreEqual(1, JsEval.Int($"_e2e_multi_b[{eid}]?.destroyCount ?? -1"), "B.onDestroy() once");

      yield return new ExitPlayMode();
    }
  }
}
