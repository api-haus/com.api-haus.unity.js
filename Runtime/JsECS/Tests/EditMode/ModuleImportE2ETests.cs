namespace UnityJS.Entities.EditModeTests
{
  using System.Collections;
  using NUnit.Framework;
  using Unity.Entities;
  using UnityEngine.TestTools;
  using UnityJS.Entities.Tests;

  public class ModuleImportE2ETests
  {
    const int INIT_FRAMES = 10;

    [UnityTest]
    public IEnumerator BuiltinModule_EcsQueryCallable()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      scene.SpawnBare();
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      Assert.IsTrue(JsEval.Bool("_e2e_import?.hasQuery === true"), "ecs.query must be a function");
      Assert.IsTrue(JsEval.Bool("_e2e_import?.queryHasWithAll === true"),
        "ecs.query() must return builder with withAll method");

      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator BuiltinModule_MathCallable()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      scene.SpawnBare();
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      Assert.AreEqual(0.0, JsEval.Double("_e2e_import?.mathResult ?? -999"), 1e-5,
        "math.sin(0) must return 0");

      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator BuiltinModule_Float3Constructable()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      scene.SpawnBare();
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      Assert.AreEqual(1.0, JsEval.Double("_e2e_import?.float3X ?? -999"), 1e-5, "float3(1,2,3).x = 1");
      Assert.AreEqual(2.0, JsEval.Double("_e2e_import?.float3Y ?? -999"), 1e-5, "float3(1,2,3).y = 2");
      Assert.AreEqual(3.0, JsEval.Double("_e2e_import?.float3Z ?? -999"), 1e-5, "float3(1,2,3).z = 3");

      yield return new ExitPlayMode();
    }
  }
}
