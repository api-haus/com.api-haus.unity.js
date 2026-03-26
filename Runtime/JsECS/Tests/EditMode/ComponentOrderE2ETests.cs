namespace UnityJS.Entities.EditModeTests
{
  using System.Collections;
  using NUnit.Framework;
  using Unity.Entities;
  using UnityEngine.TestTools;
  using UnityJS.Entities.Tests;

  public class ComponentOrderE2ETests
  {
    const string EARLY = "components/e2e_order_early";
    const string LATE = "components/e2e_order_late";
    const int INIT_FRAMES = 12;

    [UnityTest]
    public IEnumerator RunsAfter_RespectsOrdering()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);

      // Spawn with LATE first to prove topo sort wins over registration order
      var entity = scene.Spawn(new[] { LATE, EARLY });
      var eid = scene.GetEntityId(entity);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(scene.AllFulfilled(), "Scripts must be fulfilled");

      // Clear and let one frame run
      JsEval.Void($"_e2e_order[{eid}] = {{ seq: [] }}");
      yield return null;

      var first = JsEval.Int($"_e2e_order[{eid}].seq[0] === 'early' ? 1 : 0");
      Assert.AreEqual(1, first, "runsAfter component must run after its dependency (early first)");

      var second = JsEval.Int($"_e2e_order[{eid}].seq[1] === 'late' ? 1 : 0");
      Assert.AreEqual(1, second, "runsAfter component must run second (late second)");

      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator DefaultOrder_NoDependencies()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);

      var entity = scene.Spawn("components/e2e_tick_group_probe");
      var eid = scene.GetEntityId(entity);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(scene.AllFulfilled(), "Script must be fulfilled");

      var hasAfter = JsEval.Int($"_e2e_tick[{eid}]?.constructor.runsAfter == null ? 1 : 0");
      Assert.AreEqual(1, hasAfter, "Component without runsAfter should have null");

      var hasBefore = JsEval.Int($"_e2e_tick[{eid}]?.constructor.runsBefore == null ? 1 : 0");
      Assert.AreEqual(1, hasBefore, "Component without runsBefore should have null");

      yield return new ExitPlayMode();
    }
  }
}
