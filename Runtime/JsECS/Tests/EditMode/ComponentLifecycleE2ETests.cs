namespace UnityJS.Entities.EditModeTests
{
  using System.Collections;
  using NUnit.Framework;
  using Unity.Entities;
  using UnityEngine;
  using UnityEngine.TestTools;

  /// <summary>
  /// E2E tests for Component lifecycle (start, update, onDestroy).
  /// Uses SceneFixture + lifecycle_probe.ts — the real fulfillment pipeline.
  /// </summary>
  public class ComponentLifecycleE2ETests
  {
    const string SCRIPT_NAME = "components/lifecycle_probe";
    const int INIT_FRAMES = 5;

    [UnityTest]
    public IEnumerator Start_CalledExactlyOnce()
    {
      yield return new EnterPlayMode();

      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);

      var entity = scene.Spawn(SCRIPT_NAME);
      var eid = scene.GetEntityId(entity);

      for (var i = 0; i < INIT_FRAMES + 3; i++)
        yield return null;

      Assert.IsTrue(scene.AllFulfilled(), "Script must be fulfilled by JsComponentInitSystem");

      var startCount = JsEval.Int($"_e2e_lifecycle[{eid}]?.startCount ?? -1");
      Assert.AreEqual(1, startCount, "start() must be called exactly once per component lifecycle");

      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Update_CalledEveryFrame()
    {
      yield return new EnterPlayMode();

      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);

      var entity = scene.Spawn(SCRIPT_NAME);
      var eid = scene.GetEntityId(entity);

      for (var i = 0; i < INIT_FRAMES; i++)
        yield return null;

      Assert.IsTrue(scene.AllFulfilled(), "Script must be fulfilled");

      JsEval.Void($"_e2e_lifecycle[{eid}].updateCount = 0");

      const int framesToRun = 10;
      for (var i = 0; i < framesToRun; i++)
        yield return null;

      var updateCount = JsEval.Int($"_e2e_lifecycle[{eid}]?.updateCount ?? -1");
      var lastDt = JsEval.Double($"_e2e_lifecycle[{eid}]?.lastDt ?? -1");

      Assert.AreEqual(framesToRun, updateCount,
        $"update() must be called exactly once per frame ({framesToRun} frames)");
      Assert.Greater(lastDt, 0.0, "deltaTime must be positive");

      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator OnDestroy_CalledOnEntityDestruction()
    {
      yield return new EnterPlayMode();

      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);

      var entity = scene.Spawn(SCRIPT_NAME);
      var eid = scene.GetEntityId(entity);

      for (var i = 0; i < INIT_FRAMES + 3; i++)
        yield return null;

      Assert.IsTrue(scene.AllFulfilled(), "Script must be fulfilled");

      var destroyBefore = JsEval.Int($"_e2e_lifecycle[{eid}]?.destroyCount ?? 0");
      Assert.AreEqual(0, destroyBefore, "onDestroy() must not be called before entity destruction");

      world.EntityManager.DestroyEntity(entity);

      for (var i = 0; i < 3; i++)
        yield return null;

      var destroyAfter = JsEval.Int($"_e2e_lifecycle[{eid}]?.destroyCount ?? -1");
      Assert.AreEqual(1, destroyAfter, "onDestroy() must be called exactly once on entity destruction");

      yield return new ExitPlayMode();
    }
  }
}
