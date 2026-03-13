namespace UnityJS.Entities.Tests
{
	using NUnit.Framework;

	[TestFixture]
	public unsafe class JsMathBridgeTests : JsBridgeTestFixture
	{
		[Test]
		public void Cross_KnownVectors_ReturnsCorrect()
		{
			// cross({1,0,0}, {0,1,0}) = {0,0,1}
			var z = EvalGlobalFloat("math.cross({x:1,y:0,z:0},{x:0,y:1,z:0}).z");
			Assert.AreEqual(1.0, z, 0.001);
		}

		[Test]
		public void Dot_Perpendicular_ReturnsZero()
		{
			var d = EvalGlobalFloat("math.dot({x:1,y:0,z:0},{x:0,y:1,z:0})");
			Assert.AreEqual(0.0, d, 0.001);
		}

		[Test]
		public void Dot_Parallel_ReturnsProduct()
		{
			var d = EvalGlobalFloat("math.dot({x:3,y:0,z:0},{x:2,y:0,z:0})");
			Assert.AreEqual(6.0, d, 0.001);
		}

		[Test]
		public void Normalize_UnitVector_ReturnsSame()
		{
			var x = EvalGlobalFloat("math.normalize({x:1,y:0,z:0}).x");
			Assert.AreEqual(1.0, x, 0.001);
		}

		[Test]
		public void Lerp_Halfway_ReturnsMidpoint()
		{
			var x = EvalGlobalFloat("math.lerp({x:0,y:0,z:0},{x:10,y:0,z:0},0.5).x");
			Assert.AreEqual(5.0, x, 0.001);
		}

		[Test]
		public void HsvToRgb_Red()
		{
			// HSV(0, 1, 1) = RGB(1, 0, 0)
			var r = EvalGlobalFloat("colors.hsvToRgb(0, 1, 1).x");
			Assert.AreEqual(1.0, r, 0.01);
			var g = EvalGlobalFloat("colors.hsvToRgb(0, 1, 1).y");
			Assert.AreEqual(0.0, g, 0.01);
		}

		[Test]
		public void RgbToHsv_Red()
		{
			// RGB(1, 0, 0) → h ≈ 0
			var h = EvalGlobalFloat("colors.rgbToHsv({x:1,y:0,z:0}).h");
			Assert.AreEqual(0.0, h, 1.0); // h should be ~0 (or 360)
		}

		[Test]
		public void OklabRoundtrip_Consistency()
		{
			// Convert RGB → Oklab → RGB should be approximately the same
			var x = EvalGlobalFloat(
				"var lab = colors.rgbToOklab({x:0.5,y:0.3,z:0.1}); colors.oklabToRgb(lab).x");
			Assert.AreEqual(0.5, x, 0.05);
		}

		// ── New: scalar math tests ──

		[Test]
		public void Sin_Scalar_ReturnsCorrect()
		{
			var v = EvalGlobalFloat("math.sin(math.PI / 2)");
			Assert.AreEqual(1.0, v, 0.001);
		}

		[Test]
		public void Cos_Scalar_ReturnsCorrect()
		{
			var v = EvalGlobalFloat("math.cos(0)");
			Assert.AreEqual(1.0, v, 0.001);
		}

		[Test]
		public void Abs_Scalar_ReturnsCorrect()
		{
			var v = EvalGlobalFloat("math.abs(-5)");
			Assert.AreEqual(5.0, v, 0.001);
		}

		[Test]
		public void Min_Scalar_ReturnsCorrect()
		{
			var v = EvalGlobalFloat("math.min(3, 7)");
			Assert.AreEqual(3.0, v, 0.001);
		}

		[Test]
		public void Max_Scalar_ReturnsCorrect()
		{
			var v = EvalGlobalFloat("math.max(3, 7)");
			Assert.AreEqual(7.0, v, 0.001);
		}

		[Test]
		public void Floor_Scalar_ReturnsCorrect()
		{
			var v = EvalGlobalFloat("math.floor(3.7)");
			Assert.AreEqual(3.0, v, 0.001);
		}

		[Test]
		public void Lerp_Scalar_ReturnsCorrect()
		{
			var v = EvalGlobalFloat("math.lerp(0, 10, 0.25)");
			Assert.AreEqual(2.5, v, 0.001);
		}

		[Test]
		public void Clamp_Scalar_ReturnsCorrect()
		{
			var v = EvalGlobalFloat("math.clamp(15, 0, 10)");
			Assert.AreEqual(10.0, v, 0.001);
		}

		[Test]
		public void Length_Float3_ReturnsCorrect()
		{
			var v = EvalGlobalFloat("math.length({x:3,y:4,z:0})");
			Assert.AreEqual(5.0, v, 0.001);
		}

		[Test]
		public void Distance_Float3_ReturnsCorrect()
		{
			var v = EvalGlobalFloat("math.distance({x:0,y:0,z:0},{x:3,y:4,z:0})");
			Assert.AreEqual(5.0, v, 0.001);
		}

		[Test]
		public void Constants_PI_Exists()
		{
			var v = EvalGlobalFloat("math.PI");
			Assert.AreEqual(3.14159265, v, 0.0001);
		}

		[Test]
		public void Constructors_Float3_Works()
		{
			var v = EvalGlobalFloat("float3(1,2,3).y");
			Assert.AreEqual(2.0, v, 0.001);
		}

		[Test]
		public void Constructors_Float3_Splat()
		{
			var v = EvalGlobalFloat("float3(5).z");
			Assert.AreEqual(5.0, v, 0.001);
		}
	}
}
