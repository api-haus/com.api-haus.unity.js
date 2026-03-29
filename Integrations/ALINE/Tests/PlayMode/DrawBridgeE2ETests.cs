namespace UnityJS.Integration.ALINE.PlayModeTests
{
  using System.Collections;
  using UnityJS.Entities.Tests;
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

    [UnityTest]
    public IEnumerator Draw_AllFunctions_CallableFromComponent()
    {
      var entity = m_Scene.Spawn(SCRIPT);
      var eid = m_Scene.GetEntityId(entity);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(m_Scene.AllFulfilled(), "Script must be fulfilled");

      var error = JsEval.Bool($"!!_e2e_draw[{eid}]?.error");
      Assert.IsFalse(error,
        $"Draw bridge error: {JsEval.Int($"_e2e_draw[{eid}]?.error ?? 0")}");

      var success = JsEval.Bool($"_e2e_draw[{eid}]?.success === true");
      Assert.IsTrue(success, "All draw functions must complete without error");

      // 8 draw calls: setColor, line, ray, wireSphere, wireBox, solidBox, circleXz, arrow
      var callCount = JsEval.Int($"_e2e_draw[{eid}]?.callCount ?? -1");
      Assert.AreEqual(8, callCount, "All 8 draw functions must have been called");
    }
  }
}
