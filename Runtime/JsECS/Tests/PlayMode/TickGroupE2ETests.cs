namespace UnityJS.Entities.PlayModeTests
{
  using System.Collections;
  using NUnit.Framework;
  using Unity.Entities;
  using Unity.Mathematics;
  using UnityEngine;
  using UnityEngine.TestTools;
  using UnityJS.Entities.Tests;

  /// <summary>
  /// E2E tests for tick groups (update, fixedUpdate, lateUpdate).
  /// Uses e2e_tick_group_probe.ts component with independent counters per group.
  /// </summary>
  public class TickGroupE2ETests
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

    const string SCRIPT = "components/e2e_tick_group_probe";
    const int INIT_FRAMES = 10;

    [UnityTest]
    public IEnumerator Update_CalledEveryFrame()
    {
      var entity = m_Scene.Spawn(SCRIPT);
      var eid = m_Scene.GetEntityId(entity);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(m_Scene.AllFulfilled(), "Script must be fulfilled");

      JsEval.Void($"_e2e_tick[{eid}].updateCount = 0");
      for (var i = 0; i < 10; i++) yield return null;

      var count = JsEval.Int($"_e2e_tick[{eid}]?.updateCount ?? -1");
      Assert.AreEqual(10, count, "update() must be called once per frame");
    }

    [UnityTest]
    public IEnumerator FixedUpdate_CalledAtLeastOnce()
    {
      var entity = m_Scene.Spawn(SCRIPT);
      var eid = m_Scene.GetEntityId(entity);

      // Need fixed updates — wait with WaitForFixedUpdate
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(m_Scene.AllFulfilled(), "Script must be fulfilled");

      for (var i = 0; i < 10; i++) yield return new WaitForFixedUpdate();
      for (var i = 0; i < 3; i++) yield return null;

      var count = JsEval.Int($"_e2e_tick[{eid}]?.fixedUpdateCount ?? -1");
      Assert.Greater(count, 0, "fixedUpdate() must be called at least once");
    }

    [UnityTest]
    public IEnumerator LateUpdate_CalledAfterUpdate()
    {
      var entity = m_Scene.Spawn(SCRIPT);
      var eid = m_Scene.GetEntityId(entity);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(m_Scene.AllFulfilled(), "Script must be fulfilled");

      for (var i = 0; i < 5; i++) yield return null;

      var lateCount = JsEval.Int($"_e2e_tick[{eid}]?.lateUpdateCount ?? -1");
      Assert.Greater(lateCount, 0, "lateUpdate() must be called");

      // frameOrdinal: 1=update ran, 2=lateUpdate ran after update
      var ordinal = JsEval.Int($"_e2e_tick[{eid}]?.frameOrdinal ?? -1");
      Assert.AreEqual(2, ordinal, "lateUpdate must run after update (ordinal=2)");
    }
  }
}
