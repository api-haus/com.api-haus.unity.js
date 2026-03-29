namespace UnityJS.Entities.PlayModeTests
{
  using System.Collections;
  using NUnit.Framework;
  using Unity.Entities;
  using UnityEngine.TestTools;
  using UnityJS.Entities.Tests;

  public class ModuleImportE2ETests
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
    public IEnumerator BuiltinModule_EcsQueryCallable()
    {
      m_Scene.SpawnBare();
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      Assert.IsTrue(JsEval.Bool("_e2e_import?.hasQuery === true"), "ecs.query must be a function");
      Assert.IsTrue(JsEval.Bool("_e2e_import?.queryHasWithAll === true"),
        "ecs.query() must return builder with withAll method");
    }

    [UnityTest]
    public IEnumerator BuiltinModule_MathCallable()
    {
      m_Scene.SpawnBare();
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      Assert.AreEqual(0.0, JsEval.Double("_e2e_import?.mathResult ?? -999"), 1e-5,
        "math.sin(0) must return 0");
    }

    [UnityTest]
    public IEnumerator BuiltinModule_Float3Constructable()
    {
      m_Scene.SpawnBare();
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      Assert.AreEqual(1.0, JsEval.Double("_e2e_import?.float3X ?? -999"), 1e-5, "float3(1,2,3).x = 1");
      Assert.AreEqual(2.0, JsEval.Double("_e2e_import?.float3Y ?? -999"), 1e-5, "float3(1,2,3).y = 2");
      Assert.AreEqual(3.0, JsEval.Double("_e2e_import?.float3Z ?? -999"), 1e-5, "float3(1,2,3).z = 3");
    }
  }
}
