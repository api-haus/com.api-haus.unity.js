namespace UnityJS.Runtime.Tests
{
	using System;
	using System.IO;
	using NUnit.Framework;

	[TestFixture]
	public class JsScriptLoaderTests
	{
		string m_TempDir;

		[SetUp]
		public void SetUp()
		{
			m_TempDir = Path.Combine(Path.GetTempPath(), "jsloader_tests_" + Guid.NewGuid().ToString("N")[..8]);
			Directory.CreateDirectory(m_TempDir);
			JsScriptSearchPaths.Reset();
			JsScriptSearchPaths.AddSearchPath(m_TempDir, 0);
		}

		[TearDown]
		public void TearDown()
		{
			if (Directory.Exists(m_TempDir))
				Directory.Delete(m_TempDir, true);
		}

		[Test]
		public void ValidateScriptId_Valid()
		{
			var result = JsScriptLoader.ValidateScriptId("my_script");
			Assert.IsTrue(result.isValid);
			Assert.AreEqual("my_script", result.scriptId.ToString());
		}

		[Test]
		public void ValidateScriptId_Empty_Fails()
		{
			var result = JsScriptLoader.ValidateScriptId("");
			Assert.IsFalse(result.isValid);
		}

		[Test]
		public void FromStreamingAssets_BuildsPath()
		{
			// This test validates path construction even if the file doesn't exist
			var result = JsScriptLoader.FromStreamingAssets("foo");
			// File won't exist in test env, so it should fail with "File not found"
			Assert.IsFalse(result.isValid);
			Assert.IsTrue(result.error.ToString().Contains("File not found"));
		}

		[Test]
		public void FromSearchPaths_FindsScript()
		{
			File.WriteAllText(Path.Combine(m_TempDir, "player.js"), "export const x = 1;");

			var result = JsScriptLoader.FromSearchPaths("player");
			Assert.IsTrue(result.isValid);
			Assert.AreEqual("player", result.scriptId.ToString());
			Assert.IsTrue(result.filePath.ToString().EndsWith("player.js"));
		}
	}
}
