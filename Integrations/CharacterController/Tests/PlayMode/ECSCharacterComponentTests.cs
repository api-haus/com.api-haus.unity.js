namespace UnityJS.Integration.CharacterController.PlayModeTests
{
  using NUnit.Framework;

  /// <summary>
  /// Unit tests for CharacterController integration components and data structures.
  /// </summary>
  [TestFixture]
  public class ECSCharacterComponentTests
  {
    [Test]
    public void DefaultStats_HasSensibleValues()
    {
      var stats = ECSCharacterStats.Default();

      Assert.Greater(stats.maxSpeed, 0f, "maxSpeed should be positive");
      Assert.Greater(stats.sprintSpeed, stats.maxSpeed, "sprintSpeed should exceed maxSpeed");
      Assert.Greater(stats.acceleration, 0f, "acceleration should be positive");
      Assert.Greater(stats.jumpForce, 0f, "jumpForce should be positive");
      Assert.Less(stats.gravity, 0f, "gravity should be negative (downward)");
      Assert.Greater(stats.rotationSpeed, 0f, "rotationSpeed should be positive");
      Assert.AreEqual(1f, stats.speedMultiplier, "speedMultiplier default should be 1");
      Assert.Greater(stats.maxStamina, 0f, "maxStamina should be positive");
      Assert.AreEqual(stats.maxStamina, stats.stamina, "stamina should start at maxStamina");
      Assert.AreEqual(0, stats.jumpCount, "jumpCount should start at 0");
      Assert.Greater(stats.maxJumps, 0, "maxJumps should be positive");
    }

    [Test]
    public void FixedInputEvent_Set_IsSet_Roundtrip()
    {
      var evt = new FixedInputEvent();
      evt.Set(42);

      Assert.IsTrue(evt.IsSet(42), "IsSet should return true for the tick it was set on");
    }

    [Test]
    public void FixedInputEvent_Set_DifferentTick_ReturnsFalse()
    {
      var evt = new FixedInputEvent();
      evt.Set(42);

      Assert.IsFalse(evt.IsSet(43), "IsSet should return false for a different tick");
      Assert.IsFalse(evt.IsSet(41), "IsSet should return false for a previous tick");
    }

    [Test]
    public void FixedInputEvent_Initial_IsNotSet()
    {
      var evt = new FixedInputEvent();

      Assert.IsFalse(evt.IsSet(0), "Initial event should not be set for tick 0");
      Assert.IsFalse(evt.IsSet(1), "Initial event should not be set for tick 1");
    }

    [Test]
    public void FixedInputEvent_SetMultipleTimes_LastWins()
    {
      var evt = new FixedInputEvent();
      evt.Set(10);
      evt.Set(20);

      Assert.IsFalse(evt.IsSet(10), "First set should be overwritten");
      Assert.IsTrue(evt.IsSet(20), "Last set should be active");
    }

    [Test]
    public void ECSCharacterControl_DefaultFields()
    {
      var control = new ECSCharacterControl();

      Assert.AreEqual(
        Unity.Mathematics.float3.zero,
        control.moveVector,
        "Default moveVector should be zero"
      );
      Assert.IsFalse(control.jump, "Default jump should be false");
      Assert.IsFalse(control.sprint, "Default sprint should be false");
    }

    [Test]
    public void ECSCharacterState_DefaultFields()
    {
      var state = new ECSCharacterState();

      Assert.IsFalse(state.isGrounded, "Default isGrounded should be false");
      Assert.AreEqual(
        Unity.Mathematics.float3.zero,
        state.velocity,
        "Default velocity should be zero"
      );
      Assert.IsFalse(state.wasGroundedLastFrame, "Default wasGroundedLastFrame should be false");
    }
  }
}
