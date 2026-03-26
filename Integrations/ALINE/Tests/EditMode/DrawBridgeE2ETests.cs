namespace UnityJS.Integration.ALINE.EditModeTests
{
  using System.Collections;
  using Entities.EditModeTests;
  using Entities.Tests;
  using NUnit.Framework;
  using Unity.Entities;
  using Unity.Mathematics;
  using UnityEngine.TestTools;

  /// <summary>
  /// E2E tests for draw bridge functions through real component lifecycle.
  /// Uses e2e_draw_probe.ts which calls all draw functions in start().
  /// </summary>
  public class DrawBridgeE2ETests
  {
    const string SCRIPT = "components/e2e_draw_probe";
    const int INIT_FRAMES = 10;

    [UnityTest]
    public IEnumerator Draw_AllFunctions_CallableFromComponent()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      var entity = scene.Spawn(SCRIPT);
      var eid = scene.GetEntityId(entity);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(scene.AllFulfilled(), "Script must be fulfilled");

      var error = JsEval.Bool($"!!_e2e_draw[{eid}]?.error");
      Assert.IsFalse(error,
        $"Draw bridge error: {JsEval.Int($"_e2e_draw[{eid}]?.error ?? 0")}");

      var success = JsEval.Bool($"_e2e_draw[{eid}]?.success === true");
      Assert.IsTrue(success, "All draw functions must complete without error");

      // 8 draw calls: setColor, line, ray, wireSphere, wireBox, solidBox, circleXz, arrow
      var callCount = JsEval.Int($"_e2e_draw[{eid}]?.callCount ?? -1");
      Assert.AreEqual(8, callCount, "All 8 draw functions must have been called");

      yield return new ExitPlayMode();
    }
  }
}
