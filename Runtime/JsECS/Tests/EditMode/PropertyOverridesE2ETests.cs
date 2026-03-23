namespace UnityJS.Entities.EditModeTests
{
  using System.Collections;
  using NUnit.Framework;
  using Unity.Entities;
  using Unity.Mathematics;
  using UnityEngine.TestTools;

  /// <summary>
  /// E2E tests for propertiesJson parameter — proves Inspector-style property
  /// overrides are applied to component instances during initialization.
  /// </summary>
  public class PropertyOverridesE2ETests
  {
    const string SCRIPT = "components/e2e_wanderer";
    const int INIT_FRAMES = 12;

    [UnityTest]
    public IEnumerator Override_ChangesDefaultValue()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);

      var entity = scene.Spawn(SCRIPT, float3.zero, @"{""speed"":10}");
      var eid = scene.GetEntityId(entity);

      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(scene.AllFulfilled(), "Script must be fulfilled");

      var speed = JsEval.Int($"_e2e_wander[{eid}]?.speed ?? -1");
      Assert.AreEqual(10, speed, "speed should be overridden to 10, not default 3");

      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator Override_MultipleProperties()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);

      var entity = scene.Spawn(SCRIPT, float3.zero, @"{""speed"":7,""wanderRadius"":20}");
      var eid = scene.GetEntityId(entity);

      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(scene.AllFulfilled(), "Script must be fulfilled");

      Assert.AreEqual(7, JsEval.Int($"_e2e_wander[{eid}]?.speed ?? -1"), "speed=7");
      Assert.AreEqual(20, JsEval.Int($"_e2e_wander[{eid}]?.wanderRadius ?? -1"), "wanderRadius=20");

      yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator NoOverride_UsesDefault()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);

      var entity = scene.Spawn(SCRIPT, float3.zero);
      var eid = scene.GetEntityId(entity);

      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(scene.AllFulfilled(), "Script must be fulfilled");

      var speed = JsEval.Int($"_e2e_wander[{eid}]?.speed ?? -1");
      Assert.AreEqual(3, speed, "speed should be default 3 when no override");

      yield return new ExitPlayMode();
    }
  }
}
