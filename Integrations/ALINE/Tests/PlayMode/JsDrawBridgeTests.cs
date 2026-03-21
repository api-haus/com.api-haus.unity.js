namespace UnityJS.Integration.ALINE.PlayModeTests
{
  using NUnit.Framework;
  using UnityJS.Entities.PlayModeTests;

  /// <summary>
  /// Unit tests for ALINE integration draw bridge functions.
  /// Tests draw.setColor, draw.withDuration, draw.line, draw.ray, draw.arrow, etc.
  /// </summary>
  [TestFixture]
  public unsafe class JsDrawBridgeTests : JsBridgeTestFixture
  {
    [Test]
    public void DrawNamespace_Exists()
    {
      var result = EvalGlobalBool("typeof draw !== 'undefined'");
      Assert.IsTrue(result, "draw namespace should exist after bridge registration");
    }

    [Test]
    public void SetColor_ValidArgs_NoThrow()
    {
      EvalGlobalVoid("draw.setColor(1, 0, 0)");
    }

    [Test]
    public void SetColor_WithAlpha_NoThrow()
    {
      EvalGlobalVoid("draw.setColor(1, 0.5, 0, 0.8)");
    }

    [Test]
    public void WithDuration_ValidArg_NoThrow()
    {
      EvalGlobalVoid("draw.withDuration(5.0)");
    }

    [Test]
    public void Line_ValidArgs_NoThrow()
    {
      EvalGlobalVoid("draw.line({x:0,y:0,z:0}, {x:1,y:1,z:1})");
    }

    [Test]
    public void Ray_ValidArgs_NoThrow()
    {
      EvalGlobalVoid("draw.ray({x:0,y:0,z:0}, {x:0,y:1,z:0})");
    }

    [Test]
    public void Arrow_ValidArgs_NoThrow()
    {
      EvalGlobalVoid("draw.arrow({x:0,y:0,z:0}, {x:1,y:0,z:0})");
    }

    [Test]
    public void WireSphere_ValidArgs_NoThrow()
    {
      EvalGlobalVoid("draw.wireSphere({x:0,y:1,z:0}, 2.5)");
    }

    [Test]
    public void WireBox_ValidArgs_NoThrow()
    {
      EvalGlobalVoid("draw.wireBox({x:0,y:0,z:0}, {x:1,y:1,z:1})");
    }

    [Test]
    public void WireCapsule_ValidArgs_NoThrow()
    {
      EvalGlobalVoid("draw.wireCapsule({x:0,y:0,z:0}, {x:0,y:2,z:0}, 0.5)");
    }

    [Test]
    public void CircleXz_ValidArgs_NoThrow()
    {
      EvalGlobalVoid("draw.circleXz({x:0,y:0,z:0}, 3.0)");
    }

    [Test]
    public void SolidBox_ValidArgs_NoThrow()
    {
      EvalGlobalVoid("draw.solidBox({x:0,y:0,z:0}, {x:1,y:1,z:1})");
    }

    [Test]
    public void SolidCircle_ValidArgs_NoThrow()
    {
      EvalGlobalVoid("draw.solidCircle({x:0,y:0,z:0}, {x:0,y:1,z:0}, 1.0)");
    }

    [Test]
    public void Label2d_ValidArgs_NoThrow()
    {
      EvalGlobalVoid("draw.label2d({x:0,y:0,z:0}, 'hello')");
    }

    [Test]
    public void Line_InsufficientArgs_NoThrow()
    {
      // Should return undefined without crashing, not throw
      EvalGlobalVoid("draw.line({x:0,y:0,z:0})");
    }

    [Test]
    public void SetColor_Then_Line_UsesColor_NoThrow()
    {
      // Verify state persistence across calls
      EvalGlobalVoid("draw.setColor(0, 1, 0)");
      EvalGlobalVoid("draw.withDuration(1.0)");
      EvalGlobalVoid("draw.line({x:0,y:0,z:0}, {x:5,y:5,z:5})");
    }
  }
}
