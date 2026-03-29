namespace UnityJS.Entities.PlayModeTests
{
  using System.Collections;
  using NUnit.Framework;
  using Unity.Entities;
  using UnityEngine.TestTools;
  using UnityJS.Entities.Tests;

  /// <summary>
  /// E2E tests for math bridge. Uses e2e_math_probe.ts system that
  /// evaluates all math operations once on first onUpdate with known values.
  /// All assertions derived from first principles.
  /// </summary>
  public class MathBridgeE2ETests
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
    const double TOL = 1e-5;

    [UnityTest]
    public IEnumerator Dot_Orthogonal_IsZero()
    {
      m_Scene.SpawnBare();
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      var v = JsEval.Double("_e2e_math?.dot_ortho ?? -999");
      Assert.AreEqual(0.0, v, TOL, "dot(x-axis, y-axis) must be 0 (orthogonal)");
    }

    [UnityTest]
    public IEnumerator Dot_Parallel_IsProduct()
    {
      m_Scene.SpawnBare();
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      Assert.AreEqual(6.0, JsEval.Double("_e2e_math?.dot_para ?? -999"), TOL, "dot((3,0,0),(2,0,0)) = 6");
    }

    [UnityTest]
    public IEnumerator Length_Pythagorean()
    {
      m_Scene.SpawnBare();
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      Assert.AreEqual(5.0, JsEval.Double("_e2e_math?.len_345 ?? -999"), TOL, "length(3,4,0) = 5");
    }

    [UnityTest]
    public IEnumerator Normalize_UnitLength()
    {
      m_Scene.SpawnBare();
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      Assert.AreEqual(1.0, JsEval.Double("_e2e_math?.norm_z_len ?? -999"), TOL, "length(normalize(v)) = 1");
      Assert.AreEqual(1.0, JsEval.Double("_e2e_math?.norm_z?.z ?? -999"), TOL, "normalize(0,0,5).z = 1");
    }

    [UnityTest]
    public IEnumerator Lerp_Boundaries()
    {
      m_Scene.SpawnBare();
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      Assert.AreEqual(0.0, JsEval.Double("_e2e_math?.lerp_0 ?? -999"), TOL, "lerp(0,10,0) = 0");
      Assert.AreEqual(10.0, JsEval.Double("_e2e_math?.lerp_1 ?? -999"), TOL, "lerp(0,10,1) = 10");
      Assert.AreEqual(5.0, JsEval.Double("_e2e_math?.lerp_half ?? -999"), TOL, "lerp(0,10,0.5) = 5");
    }

    [UnityTest]
    public IEnumerator Cross_RightHandRule()
    {
      m_Scene.SpawnBare();
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      Assert.AreEqual(1.0, JsEval.Double("_e2e_math?.cross_xy?.z ?? -999"), TOL, "cross(x,y).z = 1");
      Assert.AreEqual(-1.0, JsEval.Double("_e2e_math?.cross_yx?.z ?? -999"), TOL, "cross(y,x).z = -1");
    }

    [UnityTest]
    public IEnumerator Trig_KnownValues()
    {
      m_Scene.SpawnBare();
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      Assert.AreEqual(0.0, JsEval.Double("_e2e_math?.sin_0 ?? -999"), TOL, "sin(0) = 0");
      Assert.AreEqual(1.0, JsEval.Double("_e2e_math?.sin_half_pi ?? -999"), TOL, "sin(PI/2) = 1");
      Assert.AreEqual(1.0, JsEval.Double("_e2e_math?.cos_0 ?? -999"), TOL, "cos(0) = 1");
    }

    [UnityTest]
    public IEnumerator Clamp_Boundaries()
    {
      m_Scene.SpawnBare();
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      Assert.AreEqual(10.0, JsEval.Double("_e2e_math?.clamp_over ?? -999"), TOL, "clamp(15,0,10) = 10");
      Assert.AreEqual(0.0, JsEval.Double("_e2e_math?.clamp_under ?? -999"), TOL, "clamp(-5,0,10) = 0");
      Assert.AreEqual(5.0, JsEval.Double("_e2e_math?.clamp_in ?? -999"), TOL, "clamp(5,0,10) = 5");
    }

    [UnityTest]
    public IEnumerator Distance_Pythagorean()
    {
      m_Scene.SpawnBare();
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      Assert.AreEqual(5.0, JsEval.Double("_e2e_math?.dist_345 ?? -999"), TOL, "distance(origin,(3,4,0)) = 5");
    }

    [UnityTest]
    public IEnumerator Float2_Constructors()
    {
      m_Scene.SpawnBare();
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      Assert.AreEqual(3.0, JsEval.Double("_e2e_math?.f2_components?.x ?? -999"), TOL, "float2(3,4).x = 3");
      Assert.AreEqual(4.0, JsEval.Double("_e2e_math?.f2_components?.y ?? -999"), TOL, "float2(3,4).y = 4");
      Assert.AreEqual(5.0, JsEval.Double("_e2e_math?.f2_splat?.x ?? -999"), TOL, "float2(5).x = 5");
      Assert.AreEqual(5.0, JsEval.Double("_e2e_math?.f2_splat?.y ?? -999"), TOL, "float2(5).y = 5");
    }

    [UnityTest]
    public IEnumerator Float3_Swizzle()
    {
      m_Scene.SpawnBare();
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      Assert.AreEqual(1.0, JsEval.Double("_e2e_math?.f3_swizzle_xz?.x ?? -999"), TOL, "float3(1,2,3).xz.x = 1");
      Assert.AreEqual(3.0, JsEval.Double("_e2e_math?.f3_swizzle_xz?.y ?? -999"), TOL, "float3(1,2,3).xz.y = 3");
    }
  }
}
