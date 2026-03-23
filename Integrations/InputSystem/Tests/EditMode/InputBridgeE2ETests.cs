namespace UnityJS.Integration.InputSystem.EditModeTests
{
  using System.Collections;
  using Entities.EditModeTests;
  using NUnit.Framework;
  using Unity.Entities;
  using Unity.Mathematics;
  using UnityEngine.TestTools;

  /// <summary>
  /// E2E test for input bridge null-safety.
  /// In batch mode (no input device), input.readValue() must not crash.
  /// </summary>
  public class InputBridgeE2ETests
  {
    const string SCRIPT = "components/e2e_input_probe";
    const int INIT_FRAMES = 10;

    [UnityTest]
    public IEnumerator ReadValue_NullSafe_NoCrash()
    {
      yield return new EnterPlayMode();
      var world = World.DefaultGameObjectInjectionWorld;
      using var scene = new SceneFixture(world);
      var entity = scene.Spawn(SCRIPT);
      var eid = scene.GetEntityId(entity);
      for (var i = 0; i < INIT_FRAMES; i++) yield return null;
      Assert.IsTrue(scene.AllFulfilled(), "Script must be fulfilled");

      Assert.IsTrue(JsEval.Bool($"_e2e_input[{eid}]?.noThrow === true"),
        "Input bridge must not throw when no device is connected");

      yield return new ExitPlayMode();
    }
  }
}
