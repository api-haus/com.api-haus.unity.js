namespace UnityJS.Integration.InputSystem.PlayModeTests
{
  using NUnit.Framework;
  using UnityJS.Entities.PlayModeTests;

  /// <summary>
  /// Unit tests for InputSystem integration bridge functions.
  /// Tests input.readValue, input.wasPressed, input.isHeld, input.wasReleased.
  /// </summary>
  [TestFixture]
  public unsafe class JsInputBridgeTests : JsBridgeTestFixture
  {
    [Test]
    public void InputNamespace_Exists()
    {
      var result = EvalGlobalBool("typeof input !== 'undefined'");
      Assert.IsTrue(result, "input namespace should exist after bridge registration");
    }

    [Test]
    public void ReadValue_WithoutInit_ReturnsNull()
    {
      // input bridge not initialized — readValue should return null, not crash
      var result = EvalGlobalBool("input.readValue('Move') === null");
      Assert.IsTrue(result, "readValue should return null when input not initialized");
    }

    [Test]
    public void WasPressed_WithoutInit_ReturnsFalse()
    {
      var result = EvalGlobalBool("input.wasPressed('Jump') === false");
      Assert.IsTrue(result, "wasPressed should return false when input not initialized");
    }

    [Test]
    public void IsHeld_WithoutInit_ReturnsFalse()
    {
      var result = EvalGlobalBool("input.isHeld('Sprint') === false");
      Assert.IsTrue(result, "isHeld should return false when input not initialized");
    }

    [Test]
    public void WasReleased_WithoutInit_ReturnsFalse()
    {
      var result = EvalGlobalBool("input.wasReleased('Fire') === false");
      Assert.IsTrue(result, "wasReleased should return false when input not initialized");
    }

    [Test]
    public void InitializeWithNull_ClearsState_NoThrow()
    {
      JsInputBridge.InitializeInputSystem(null);
      var result = EvalGlobalBool("input.readValue('Move') === null");
      Assert.IsTrue(result, "readValue should return null after null init");
    }
  }
}
