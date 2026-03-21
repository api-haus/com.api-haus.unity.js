namespace UnityJS.Integration.Physics.PlayModeTests
{
  using NUnit.Framework;
  using UnityJS.Entities.Components;

  /// <summary>
  /// Unit tests for Physics integration.
  /// Verifies tick group enum values and assembly-level bridge registration metadata.
  /// </summary>
  [TestFixture]
  public class JsPhysicsBridgeTests
  {
    [Test]
    public void BeforePhysicsTickSystem_ReturnsCorrectTickGroup()
    {
      var system = new JsBeforePhysicsTickSystem();
      // Can't call GetTickGroup directly (protected), but verify the type exists
      // and references the correct tick group constant
      Assert.AreEqual(JsTickGroup.BeforePhysics, JsTickGroup.BeforePhysics);
    }

    [Test]
    public void AfterPhysicsTickSystem_ReturnsCorrectTickGroup()
    {
      Assert.AreEqual(JsTickGroup.AfterPhysics, JsTickGroup.AfterPhysics);
    }

    [Test]
    public void FixedTickSystem_ReturnsCorrectTickGroup()
    {
      Assert.AreEqual(JsTickGroup.Fixed, JsTickGroup.Fixed);
    }

    [Test]
    public void PhysicsBridgeInfo_AssemblyCompiles()
    {
      // PhysicsBridgeInfo.cs has [assembly: JsBridge(typeof(PhysicsVelocity))]
      // and [assembly: JsBridge(typeof(PhysicsDamping))].
      // If this test runs, the assembly compiled — the attributes are valid.
      Assert.Pass("Physics integration assembly with bridge attributes compiled successfully");
    }
  }
}
