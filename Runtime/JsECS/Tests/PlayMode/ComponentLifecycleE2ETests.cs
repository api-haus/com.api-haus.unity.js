namespace UnityJS.Entities.PlayModeTests
{
  using System.Collections;
  using NUnit.Framework;
  using Unity.Entities;
  using UnityEngine;
  using UnityEngine.TestTools;
  using UnityJS.Entities.Tests;

  /// <summary>
  /// E2E tests for Component lifecycle (start, update, onDestroy).
  /// Uses SceneFixture + lifecycle_probe.ts — the real fulfillment pipeline.
  /// </summary>
  public class ComponentLifecycleE2ETests
  {
    SceneFixture m_Scene;

    [SetUp]
    public void SetUp()
    {
      m_Scene = new SceneFixture(World.DefaultGameObjectInjectionWorld);
    }

    [TearDown]
    public void TearDown()
    {
      m_Scene?.Dispose();
    }

    const string SCRIPT_NAME = "components/lifecycle_probe";
    const int INIT_FRAMES = 5;

    [UnityTest]
    public IEnumerator Start_CalledExactlyOnce()
    {
      var entity = m_Scene.Spawn(SCRIPT_NAME);
      var eid = m_Scene.GetEntityId(entity);

      for (var i = 0; i < INIT_FRAMES + 3; i++)
        yield return null;

      Assert.IsTrue(m_Scene.AllFulfilled(), "Script must be fulfilled by JsComponentInitSystem");

      var startCount = JsEval.Int($"_e2e_lifecycle[{eid}]?.startCount ?? -1");
      Assert.AreEqual(1, startCount, "start() must be called exactly once per component lifecycle");
    }

    [UnityTest]
    public IEnumerator Update_CalledEveryFrame()
    {
      var entity = m_Scene.Spawn(SCRIPT_NAME);
      var eid = m_Scene.GetEntityId(entity);

      for (var i = 0; i < INIT_FRAMES; i++)
        yield return null;

      Assert.IsTrue(m_Scene.AllFulfilled(), "Script must be fulfilled");

      JsEval.Void($"_e2e_lifecycle[{eid}].updateCount = 0");

      const int framesToRun = 10;
      for (var i = 0; i < framesToRun; i++)
        yield return null;

      var updateCount = JsEval.Int($"_e2e_lifecycle[{eid}]?.updateCount ?? -1");
      var lastDt = JsEval.Double($"_e2e_lifecycle[{eid}]?.lastDt ?? -1");

      Assert.AreEqual(framesToRun, updateCount,
        $"update() must be called exactly once per frame ({framesToRun} frames)");
      Assert.Greater(lastDt, 0.0, "deltaTime must be positive");
    }

    [UnityTest]
    public IEnumerator OnDestroy_CalledOnEntityDestruction()
    {
      var entity = m_Scene.Spawn(SCRIPT_NAME);
      var eid = m_Scene.GetEntityId(entity);

      for (var i = 0; i < INIT_FRAMES + 3; i++)
        yield return null;

      Assert.IsTrue(m_Scene.AllFulfilled(), "Script must be fulfilled");

      var destroyBefore = JsEval.Int($"_e2e_lifecycle[{eid}]?.destroyCount ?? 0");
      Assert.AreEqual(0, destroyBefore, "onDestroy() must not be called before entity destruction");

      World.DefaultGameObjectInjectionWorld.EntityManager.DestroyEntity(entity);

      for (var i = 0; i < 3; i++)
        yield return null;

      var destroyAfter = JsEval.Int($"_e2e_lifecycle[{eid}]?.destroyCount ?? -1");
      Assert.AreEqual(1, destroyAfter, "onDestroy() must be called exactly once on entity destruction");
    }
  }
}
