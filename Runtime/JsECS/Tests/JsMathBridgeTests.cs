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
			var r = EvalGlobalFloat("math.hsv_to_rgb(0, 1, 1).x");
			Assert.AreEqual(1.0, r, 0.01);
			var g = EvalGlobalFloat("math.hsv_to_rgb(0, 1, 1).y");
			Assert.AreEqual(0.0, g, 0.01);
		}

		[Test]
		public void RgbToHsv_Red()
		{
			// RGB(1, 0, 0) → h ≈ 0
			var h = EvalGlobalFloat("math.rgb_to_hsv({x:1,y:0,z:0}).h");
			Assert.AreEqual(0.0, h, 1.0); // h should be ~0 (or 360)
		}

		[Test]
		public void OklabRoundtrip_Consistency()
		{
			// Convert RGB → Oklab → RGB should be approximately the same
			var x = EvalGlobalFloat(
				"var lab = math.rgb_to_oklab({x:0.5,y:0.3,z:0.1}); math.oklab_to_rgb(lab).x");
			Assert.AreEqual(0.5, x, 0.05);
		}
	}
}
