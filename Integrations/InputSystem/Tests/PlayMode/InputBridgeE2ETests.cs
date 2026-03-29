namespace UnityJS.Integration.InputSystem.PlayModeTests
{
  using System.Collections;
  using NUnit.Framework;
  using Unity.Entities;
  using Unity.Mathematics;
  using UnityEngine.TestTools;
  using UnityJS.Entities.Tests;

  /// <summary>
  /// E2E test for input bridge null-safety.
  /// In batch mode (no input device), input.readValue() must not crash.
  /// </summary>
  public class InputBridgeE2ETests
  {
    const string SCRIPT = "components/e2e_input_probe";
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
    public IEnumerator ReadValue_NullSafe_NoCrash()
    {
      var entity = m_Scene.Spawn(SCRIPT);
      var eid = m_Scene.GetEntityId(entity);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(m_Scene.AllFulfilled(), "Script must be fulfilled");

      Assert.IsTrue(JsEval.Bool($"_e2e_input[{eid}]?.noThrow === true"),
        "Input bridge must not throw when no device is connected");
    }
  }
}
