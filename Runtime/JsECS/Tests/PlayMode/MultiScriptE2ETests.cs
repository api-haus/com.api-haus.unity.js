namespace UnityJS.Entities.PlayModeTests
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

    const int INIT_FRAMES = 10;

    [UnityTest]
    public IEnumerator TwoComponents_BothInitialized()
    {
      var entity = m_Scene.Spawn(
        new[] { "components/e2e_multi_a", "components/e2e_multi_b" });
      var eid = m_Scene.GetEntityId(entity);

      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(m_Scene.AllFulfilled(), "Both scripts must be fulfilled");

      Assert.AreEqual(1, JsEval.Int($"_e2e_multi_a[{eid}]?.startCount ?? -1"), "A.start() once");
      Assert.AreEqual(1, JsEval.Int($"_e2e_multi_b[{eid}]?.startCount ?? -1"), "B.start() once");
    }

    [UnityTest]
    public IEnumerator TwoComponents_IndependentUpdate()
    {
      var entity = m_Scene.Spawn(
        new[] { "components/e2e_multi_a", "components/e2e_multi_b" });
      var eid = m_Scene.GetEntityId(entity);

      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(m_Scene.AllFulfilled(), "Both scripts must be fulfilled");

      JsEval.Void($"_e2e_multi_a[{eid}].updateCount = 0; _e2e_multi_b[{eid}].updateCount = 0");

      const int frames = 5;
      for (var i = 0; i < frames; i++) yield return null;

      Assert.AreEqual(frames, JsEval.Int($"_e2e_multi_a[{eid}]?.updateCount ?? -1"), "A updates per frame");
      Assert.AreEqual(frames, JsEval.Int($"_e2e_multi_b[{eid}]?.updateCount ?? -1"), "B updates per frame");
    }

    [UnityTest]
    public IEnumerator TwoComponents_BothDestroyed()
    {
      var entity = m_Scene.Spawn(
        new[] { "components/e2e_multi_a", "components/e2e_multi_b" });
      var eid = m_Scene.GetEntityId(entity);

      for (var i = 0; i < INIT_FRAMES + 3; i++) yield return null;
      Assert.IsTrue(m_Scene.AllFulfilled(), "Both scripts must be fulfilled");

      World.DefaultGameObjectInjectionWorld.EntityManager.DestroyEntity(entity);
      for (var i = 0; i < 3; i++) yield return null;

      Assert.AreEqual(1, JsEval.Int($"_e2e_multi_a[{eid}]?.destroyCount ?? -1"), "A.onDestroy() once");
      Assert.AreEqual(1, JsEval.Int($"_e2e_multi_b[{eid}]?.destroyCount ?? -1"), "B.onDestroy() once");
    }
  }
}
