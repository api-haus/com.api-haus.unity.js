namespace UnityJS.Entities.EditModeTests
{
  using System.Collections;
  using NUnit.Framework;
  using Unity.Entities;
  using Unity.Mathematics;
  using UnityEngine.TestTools;

  /// <summary>
  /// E2E tests for ecs.query().withNone() filter.
  /// Uses e2e_query_filter_probe.ts system.
  /// </summary>
  public class QueryFilterE2ETests
  {
    const int INIT_FRAMES = 12;

    [UnityTest]
    public IEnumerator WithNone_ExcludesTaggedEntities()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);

      // Activate the query filter probe
      JsEval.Void("_e2e_qf_active = true");

      // Spawn 5 entities
      var eids = new int[5];
      for (var i = 0; i < 5; i++)
        eids[i] = scene.GetEntityId(scene.SpawnBare(new float3(i, 0, 0)));

      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      var allBefore = JsEval.Int("_e2e_qf?.allCount ?? -1");
      var filteredBefore = JsEval.Int("_e2e_qf?.filteredCount ?? -1");

      // Before tagging: filtered should equal all (no exclusions)
      Assert.GreaterOrEqual(allBefore, 5, $"allCount should be >= 5, got {allBefore}");
      Assert.AreEqual(allBefore, filteredBefore,
        $"Before tagging, filtered ({filteredBefore}) must equal all ({allBefore})");

      // Tag 2 entities
      JsEval.Void($"_e2e_qf_tag = [{eids[0]}, {eids[1]}]");
      for (var i = 0; i < 5; i++) yield return null;

      var allAfter = JsEval.Int("_e2e_qf?.allCount ?? -1");
      var filteredAfter = JsEval.Int("_e2e_qf?.filteredCount ?? -1");

      // After tagging 2: filtered should be all - 2
      Assert.AreEqual(allAfter - 2, filteredAfter,
        $"After tagging 2, filtered ({filteredAfter}) should be all ({allAfter}) - 2");

      yield return new ExitPlayMode();
    }
  }
}
