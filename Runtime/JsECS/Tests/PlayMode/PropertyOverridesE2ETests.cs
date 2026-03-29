namespace UnityJS.Entities.PlayModeTests
{
  using System.Collections;
  using NUnit.Framework;
  using Unity.Entities;
  using Unity.Mathematics;
  using UnityEngine.TestTools;
  using UnityJS.Entities.Tests;

  /// <summary>
  /// E2E tests for propertiesJson parameter — proves Inspector-style property
  /// overrides are applied to component instances during initialization.
  /// </summary>
  public class PropertyOverridesE2ETests
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

    const string SCRIPT = "components/e2e_wanderer";
    const int INIT_FRAMES = 12;

    [UnityTest]
    public IEnumerator Override_ChangesDefaultValue()
    {
      var entity = m_Scene.Spawn(SCRIPT, float3.zero, @"{""speed"":10}");
      var eid = m_Scene.GetEntityId(entity);

      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(m_Scene.AllFulfilled(), "Script must be fulfilled");

      var speed = JsEval.Int($"_e2e_wander[{eid}]?.speed ?? -1");
      Assert.AreEqual(10, speed, "speed should be overridden to 10, not default 3");
    }

    [UnityTest]
    public IEnumerator Override_MultipleProperties()
    {
      var entity = m_Scene.Spawn(SCRIPT, float3.zero, @"{""speed"":7,""wanderRadius"":20}");
      var eid = m_Scene.GetEntityId(entity);

      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(m_Scene.AllFulfilled(), "Script must be fulfilled");

      Assert.AreEqual(7, JsEval.Int($"_e2e_wander[{eid}]?.speed ?? -1"), "speed=7");
      Assert.AreEqual(20, JsEval.Int($"_e2e_wander[{eid}]?.wanderRadius ?? -1"), "wanderRadius=20");
    }

    [UnityTest]
    public IEnumerator NoOverride_UsesDefault()
    {
      var entity = m_Scene.Spawn(SCRIPT, float3.zero);
      var eid = m_Scene.GetEntityId(entity);

      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(m_Scene.AllFulfilled(), "Script must be fulfilled");

      var speed = JsEval.Int($"_e2e_wander[{eid}]?.speed ?? -1");
      Assert.AreEqual(3, speed, "speed should be default 3 when no override");
    }
  }
}
