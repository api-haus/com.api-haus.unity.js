namespace UnityJS.Entities.EditModeTests
{
  using System.Collections;
  using NUnit.Framework;
  using Unity.Entities;
  using UnityEngine;
  using UnityEngine.TestTools;
  using UnityJS.Entities.Tests;

  /// <summary>
  /// E2E tests for math bridge. Uses e2e_math_probe.ts system that
  /// evaluates all math operations once on first onUpdate with known values.
  /// All assertions derived from first principles.
  ///
  /// Single play mode session — the probe runs once and populates _e2e_math.
  /// Each [UnityTest] enters+exits play mode independently.
  /// </summary>
  public class MathBridgeE2ETests
  {
    const int INIT_FRAMES = 10;
    const double TOL = 1e-5;

    [UnityTest]
    public IEnumerator Dot_Orthogonal_IsZero()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      scene.SpawnBare();
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      var v = JsEval.Double("_e2e_math?.dot_ortho ?? -999");
      Assert.AreEqual(0.0, v, TOL, "dot(x-axis, y-axis) must be 0 (orthogonal)");
      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Dot_Parallel_IsProduct()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      scene.SpawnBare();
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      Assert.AreEqual(6.0, JsEval.Double("_e2e_math?.dot_para ?? -999"), TOL, "dot((3,0,0),(2,0,0)) = 6");
      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Length_Pythagorean()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      scene.SpawnBare();
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      Assert.AreEqual(5.0, JsEval.Double("_e2e_math?.len_345 ?? -999"), TOL, "length(3,4,0) = 5");
      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Normalize_UnitLength()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      scene.SpawnBare();
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      Assert.AreEqual(1.0, JsEval.Double("_e2e_math?.norm_z_len ?? -999"), TOL, "length(normalize(v)) = 1");
      Assert.AreEqual(1.0, JsEval.Double("_e2e_math?.norm_z?.z ?? -999"), TOL, "normalize(0,0,5).z = 1");
      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Lerp_Boundaries()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      scene.SpawnBare();
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      Assert.AreEqual(0.0, JsEval.Double("_e2e_math?.lerp_0 ?? -999"), TOL, "lerp(0,10,0) = 0");
      Assert.AreEqual(10.0, JsEval.Double("_e2e_math?.lerp_1 ?? -999"), TOL, "lerp(0,10,1) = 10");
      Assert.AreEqual(5.0, JsEval.Double("_e2e_math?.lerp_half ?? -999"), TOL, "lerp(0,10,0.5) = 5");
      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Cross_RightHandRule()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      scene.SpawnBare();
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      Assert.AreEqual(1.0, JsEval.Double("_e2e_math?.cross_xy?.z ?? -999"), TOL, "cross(x,y).z = 1");
      Assert.AreEqual(-1.0, JsEval.Double("_e2e_math?.cross_yx?.z ?? -999"), TOL, "cross(y,x).z = -1");
      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Trig_KnownValues()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      scene.SpawnBare();
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      Assert.AreEqual(0.0, JsEval.Double("_e2e_math?.sin_0 ?? -999"), TOL, "sin(0) = 0");
      Assert.AreEqual(1.0, JsEval.Double("_e2e_math?.sin_half_pi ?? -999"), TOL, "sin(PI/2) = 1");
      Assert.AreEqual(1.0, JsEval.Double("_e2e_math?.cos_0 ?? -999"), TOL, "cos(0) = 1");
      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Clamp_Boundaries()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      scene.SpawnBare();
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      Assert.AreEqual(10.0, JsEval.Double("_e2e_math?.clamp_over ?? -999"), TOL, "clamp(15,0,10) = 10");
      Assert.AreEqual(0.0, JsEval.Double("_e2e_math?.clamp_under ?? -999"), TOL, "clamp(-5,0,10) = 0");
      Assert.AreEqual(5.0, JsEval.Double("_e2e_math?.clamp_in ?? -999"), TOL, "clamp(5,0,10) = 5");
      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Distance_Pythagorean()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      scene.SpawnBare();
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      Assert.AreEqual(5.0, JsEval.Double("_e2e_math?.dist_345 ?? -999"), TOL, "distance(origin,(3,4,0)) = 5");
      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Float2_Constructors()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      scene.SpawnBare();
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      Assert.AreEqual(3.0, JsEval.Double("_e2e_math?.f2_components?.x ?? -999"), TOL, "float2(3,4).x = 3");
      Assert.AreEqual(4.0, JsEval.Double("_e2e_math?.f2_components?.y ?? -999"), TOL, "float2(3,4).y = 4");
      Assert.AreEqual(5.0, JsEval.Double("_e2e_math?.f2_splat?.x ?? -999"), TOL, "float2(5).x = 5");
      Assert.AreEqual(5.0, JsEval.Double("_e2e_math?.f2_splat?.y ?? -999"), TOL, "float2(5).y = 5");
      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Float3_Swizzle()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      scene.SpawnBare();
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      Assert.AreEqual(1.0, JsEval.Double("_e2e_math?.f3_swizzle_xz?.x ?? -999"), TOL, "float3(1,2,3).xz.x = 1");
      Assert.AreEqual(3.0, JsEval.Double("_e2e_math?.f3_swizzle_xz?.y ?? -999"), TOL, "float3(1,2,3).xz.y = 3");
      yield return new ExitPlayMode();
    }
  }
}
