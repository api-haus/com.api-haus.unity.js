namespace UnityJS.Entities.EditModeTests
{
  using System.Collections;
  using NUnit.Framework;
  using Unity.Entities;
  using UnityEngine.TestTools;

  /// <summary>
  /// E2E tests for ES module imports (unity.js/ecs, unity.js/math, unity.js/types).
  /// Uses e2e_import_probe.ts system that imports and verifies module exports.
  /// </summary>
  public class ModuleImportE2ETests
  {
    const int INIT_FRAMES = 10;

    [UnityTest]
    public IEnumerator BuiltinModule_EcsImportWorks()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      scene.SpawnBare();
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      Assert.IsTrue(JsEval.Bool("_e2e_import?.hasQuery === true"), "ecs.query should be a function");
      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator BuiltinModule_MathImportWorks()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      scene.SpawnBare();
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      Assert.IsTrue(JsEval.Bool("_e2e_import?.hasSin === true"), "math.sin should be a function");
      Assert.AreEqual(0.0, JsEval.Double("_e2e_import?.mathResult ?? -999"), 1e-5, "math.sin(0) = 0");
      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator BuiltinModule_TypesImportWorks()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      scene.SpawnBare();
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;

      Assert.IsTrue(JsEval.Bool("_e2e_import?.hasFloat3 === true"), "float3 should be a function");
      yield return new ExitPlayMode();
    }
  }
}
