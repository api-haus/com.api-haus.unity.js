namespace UnityJS.Entities.PlayModeTests
{
  using System.Collections;
  using NUnit.Framework;
  using Unity.Entities;
  using UnityEngine.TestTools;
  using UnityJS.Entities.Tests;

  /// <summary>
  /// E2E tests for color conversion bridge (hsvToRgb, rgbToHsv, oklabToRgb, rgbToOklab).
  /// Uses e2e_color_probe.ts system with known color values.
  /// </summary>
  public class ColorBridgeE2ETests
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
    const double TOL = 0.02;

    [UnityTest]
    public IEnumerator HsvToRgb_PrimaryColors()
    {
      m_Scene.SpawnBare();
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      // Red: HSV(0, 1, 1) → RGB(1, 0, 0)
      Assert.AreEqual(1.0, JsEval.Double("_e2e_color?.red_r ?? -1"), TOL, "red.r");
      Assert.AreEqual(0.0, JsEval.Double("_e2e_color?.red_g ?? -1"), TOL, "red.g");
      Assert.AreEqual(0.0, JsEval.Double("_e2e_color?.red_b ?? -1"), TOL, "red.b");

      // Green: HSV(1/3, 1, 1) → RGB(0, 1, 0)
      Assert.AreEqual(0.0, JsEval.Double("_e2e_color?.green_r ?? -1"), TOL, "green.r");
      Assert.AreEqual(1.0, JsEval.Double("_e2e_color?.green_g ?? -1"), TOL, "green.g");
      Assert.AreEqual(0.0, JsEval.Double("_e2e_color?.green_b ?? -1"), TOL, "green.b");

      // Blue: HSV(2/3, 1, 1) → RGB(0, 0, 1)
      Assert.AreEqual(0.0, JsEval.Double("_e2e_color?.blue_r ?? -1"), TOL, "blue.r");
      Assert.AreEqual(0.0, JsEval.Double("_e2e_color?.blue_g ?? -1"), TOL, "blue.g");
      Assert.AreEqual(1.0, JsEval.Double("_e2e_color?.blue_b ?? -1"), TOL, "blue.b");
    }

    [UnityTest]
    public IEnumerator RgbHsv_Roundtrip()
    {
      m_Scene.SpawnBare();
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      var err = JsEval.Double("_e2e_color?.rtError ?? 999");
      Assert.Less(err, 0.01, $"RGB→HSV→RGB roundtrip error should be < 0.01, got {err:F4}");
    }

    [UnityTest]
    public IEnumerator OklabRgb_Roundtrip()
    {
      m_Scene.SpawnBare();
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      var err = JsEval.Double("_e2e_color?.oklError ?? 999");
      Assert.Less(err, 0.01, $"RGB→Oklab→RGB roundtrip error should be < 0.01, got {err:F4}");
    }
  }
}
