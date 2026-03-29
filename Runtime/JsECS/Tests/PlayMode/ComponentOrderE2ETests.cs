namespace UnityJS.Entities.PlayModeTests
{
  using System.Collections;
  using NUnit.Framework;
  using Unity.Entities;
  using UnityEngine.TestTools;
  using UnityJS.Entities.Tests;

  public class ComponentOrderE2ETests
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

    const string EARLY = "components/e2e_order_early";
    const string LATE = "components/e2e_order_late";
    const int INIT_FRAMES = 12;

    [UnityTest]
    public IEnumerator RunsAfter_RespectsOrdering()
    {
      // Spawn with LATE first to prove topo sort wins over registration order
      var entity = m_Scene.Spawn(new[] { LATE, EARLY });
      var eid = m_Scene.GetEntityId(entity);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(m_Scene.AllFulfilled(), "Scripts must be fulfilled");

      // Clear and let one frame run
      JsEval.Void($"_e2e_order[{eid}] = {{ seq: [] }}");
      yield return null;

      var first = JsEval.Int($"_e2e_order[{eid}].seq[0] === 'early' ? 1 : 0");
      Assert.AreEqual(1, first, "runsAfter component must run after its dependency (early first)");

      var second = JsEval.Int($"_e2e_order[{eid}].seq[1] === 'late' ? 1 : 0");
      Assert.AreEqual(1, second, "runsAfter component must run second (late second)");
    }

    [UnityTest]
    public IEnumerator DefaultOrder_NoDependencies()
    {
      var entity = m_Scene.Spawn("components/e2e_tick_group_probe");
      var eid = m_Scene.GetEntityId(entity);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(m_Scene.AllFulfilled(), "Script must be fulfilled");

      var hasAfter = JsEval.Int($"_e2e_tick[{eid}]?.constructor.runsAfter == null ? 1 : 0");
      Assert.AreEqual(1, hasAfter, "Component without runsAfter should have null");

      var hasBefore = JsEval.Int($"_e2e_tick[{eid}]?.constructor.runsBefore == null ? 1 : 0");
      Assert.AreEqual(1, hasBefore, "Component without runsBefore should have null");
    }
  }
}
