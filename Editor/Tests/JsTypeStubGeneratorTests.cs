using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace UnityJS.Editor.Tests
{
	public class JsTypeStubGeneratorTests
	{
		string m_Content;

		[OneTimeSetUp]
		public void Setup()
		{
			m_Content = JsTypeStubGenerator.GenerateContent();
		}

		// ── Structure ────────────────────────────────────────────────

		[Test]
		public void GenerateContent_HasTypeAlias()
		{
			StringAssert.Contains("type entity = number;", m_Content);
		}

		[Test]
		public void GenerateContent_HasEnumSection()
		{
			StringAssert.Contains("Enum Constants", m_Content);
		}

		[Test]
		public void GenerateContent_HasComponentSection()
		{
			StringAssert.Contains("Component Bridges", m_Content);
		}

		[Test]
		public void GenerateContent_HasLifecycleSection()
		{
			StringAssert.Contains("ECS System Lifecycle", m_Content);
			StringAssert.Contains("interface UpdateState", m_Content);
		}

		// ── Bridged Components ───────────────────────────────────────

		[Test]
		public void GenerateContent_ContainsBridgedComponents()
		{
			// LocalTransform is always bridged via [assembly: JsBridge(typeof(LocalTransform))]
			StringAssert.Contains("interface LocalTransform {", m_Content);
		}

		[Test]
		public void GenerateContent_ComponentHasGetSet()
		{
			// LocalTransform uses ComponentAccessor (get + set); actual get/set are in bridges.d.ts
			StringAssert.Contains("declare const LocalTransform: ComponentAccessor<LocalTransform>;", m_Content);
		}

		[Test]
		public void GenerateContent_ComponentFieldsHaveTypes()
		{
			// LocalTransform fields use type references, not inline shapes
			StringAssert.Contains("Position: float3;", m_Content);
			StringAssert.Contains("Scale: number;", m_Content);
			StringAssert.Contains("Rotation: quaternion;", m_Content);
		}

		// ── Descriptions Dictionaries ────────────────────────────────

		[Test]
		public void BridgeClasses_ExistForKnownComponents()
		{
			var bridgeType = FindType("UnityJS.Entities.Generated.JsLocalTransformBridge");
			Assert.IsNotNull(bridgeType, "JsLocalTransformBridge should exist");
		}

		[Test]
		public void BridgeClasses_HaveDescriptionsField_WhenDocsExist()
		{
			var bridgeType = FindType("UnityJS.Entities.Generated.JsECSCharacterStatsBridge");
			if (bridgeType == null)
			{
				Assert.Inconclusive("JsECSCharacterStatsBridge not found — project components may not be compiled");
				return;
			}

			var field = bridgeType.GetField("Descriptions", BindingFlags.Public | BindingFlags.Static);
			Assert.IsNotNull(field, "Descriptions field should exist on bridge with documented struct");

			var dict = field.GetValue(null) as Dictionary<string, string>;
			Assert.IsNotNull(dict, "Descriptions should be a Dictionary<string, string>");
			Assert.IsTrue(dict.ContainsKey(""), "Should have struct-level description (empty key)");
			Assert.IsTrue(dict.ContainsKey("maxSpeed"), "Should have field-level description for maxSpeed");
		}

		[Test]
		public void EnumClasses_HaveDescriptionsField_WhenDocsExist()
		{
			var enumType = FindType("UnityJS.Entities.Generated.JsWanderPlaneEnum");
			if (enumType == null)
			{
				Assert.Inconclusive("JsWanderPlaneEnum not found — project enums may not be compiled");
				return;
			}

			var field = enumType.GetField("Descriptions", BindingFlags.Public | BindingFlags.Static);
			Assert.IsNotNull(field, "Descriptions field should exist on enum with docs");

			var dict = field.GetValue(null) as Dictionary<string, string>;
			Assert.IsNotNull(dict, "Descriptions should be a Dictionary<string, string>");
			Assert.IsTrue(dict.ContainsKey(""), "Should have enum-level description");
			Assert.IsTrue(dict.ContainsKey("XY"), "Should have member-level description for XY");
		}

		// ── Doc Propagation to Stubs ─────────────────────────────────

		[Test]
		public void GenerateContent_ComponentDocsAppearInStub()
		{
			var bridgeType = FindType("UnityJS.Entities.Generated.JsECSCharacterStatsBridge");
			if (bridgeType == null)
			{
				Assert.Inconclusive("JsECSCharacterStatsBridge not found");
				return;
			}

			var field = bridgeType.GetField("Descriptions", BindingFlags.Public | BindingFlags.Static);
			if (field == null)
			{
				Assert.Fail("Descriptions field missing — source generator did not emit docs");
				return;
			}

			var dict = field.GetValue(null) as Dictionary<string, string>;

			// Struct-level doc should appear before interface
			if (dict != null && dict.TryGetValue("", out var structDesc))
			{
				var classIdx = m_Content.IndexOf("interface ECSCharacterStats {", StringComparison.Ordinal);
				Assert.Greater(classIdx, 0, "interface ECSCharacterStats should exist");

				var docLine = $"/** {structDesc} */";
				var docIdx = m_Content.IndexOf(docLine, StringComparison.Ordinal);
				Assert.Greater(docIdx, 0, $"Struct doc should appear in stub: {docLine}");
				Assert.Less(docIdx, classIdx, "Struct doc should appear BEFORE interface");
			}

			// Field-level doc should appear before field line
			if (dict != null && dict.TryGetValue("max_speed", out var fieldDesc))
			{
				StringAssert.Contains($"/** {fieldDesc} */", m_Content);
				StringAssert.Contains("max_speed: number;", m_Content);
			}
		}

		[Test]
		public void GenerateContent_EnumDocsAppearInStub()
		{
			var enumType = FindType("UnityJS.Entities.Generated.JsWanderPlaneEnum");
			if (enumType == null)
			{
				Assert.Inconclusive("JsWanderPlaneEnum not found");
				return;
			}

			var field = enumType.GetField("Descriptions", BindingFlags.Public | BindingFlags.Static);
			if (field == null)
			{
				Assert.Fail("Descriptions field missing on enum — source generator did not emit docs");
				return;
			}

			var dict = field.GetValue(null) as Dictionary<string, string>;

			// Enum-level doc
			if (dict != null && dict.TryGetValue("", out var enumDesc))
			{
				var enumIdx = m_Content.IndexOf("declare const WANDER_PLANE:", StringComparison.Ordinal);
				Assert.Greater(enumIdx, 0, "declare const WANDER_PLANE should exist");

				var docLine = $"/** {enumDesc} */";
				var docIdx = m_Content.IndexOf(docLine, StringComparison.Ordinal);
				Assert.Greater(docIdx, 0, $"Enum doc should appear in stub: {docLine}");
				Assert.Less(docIdx, enumIdx, "Enum doc should appear BEFORE declare const");
			}
		}

		// ── ReadOnly component (NeedSetters = false) ─────────────────

		[Test]
		public void GenerateContent_ReadOnlyComponent_NoSetFunction()
		{
			// ECSCharacterState has NeedSetters = false → ReadonlyComponentAccessor
			if (!m_Content.Contains("interface ECSCharacterState"))
			{
				Assert.Inconclusive("ECSCharacterState not in stub");
				return;
			}

			StringAssert.Contains(
				"declare const ECSCharacterState: ReadonlyComponentAccessor<ECSCharacterState>;",
				m_Content
			);
		}

		// ── Enum field types ─────────────────────────────────────────

		[Test]
		public void GenerateContent_EnumFieldUsesEnumTypeName()
		{
			// SlimeWanderConfig.wanderPlane should map to WanderPlane type
			if (!m_Content.Contains("interface SlimeWanderConfig"))
			{
				Assert.Inconclusive("SlimeWanderConfig not in stub");
				return;
			}

			StringAssert.Contains("wanderPlane: WanderPlane;", m_Content);
		}

		// ── Enum type alias ─────────────────────────────────────────

		[Test]
		public void GenerateContent_EnumHasTypeAlias()
		{
			if (!m_Content.Contains("declare const WANDER_PLANE:"))
			{
				Assert.Inconclusive("WANDER_PLANE not in stub");
				return;
			}

			StringAssert.Contains("type WanderPlane = ", m_Content);
		}

		// ── tsc --noEmit validation ─────────────────────────────────

		[Test]
		public void GenerateContent_PassesTscNoEmit()
		{
			// Write generated content to a temp file and run tsc --noEmit
			var projectRoot = System.IO.Path.GetFullPath(".");
			var tscPath = System.IO.Path.Combine(projectRoot, "node_modules/.bin/tsc");

			if (!System.IO.File.Exists(tscPath))
			{
				Assert.Inconclusive("tsc not found at " + tscPath + " — run npm install");
				return;
			}

			var tsDir = System.IO.Path.Combine(projectRoot, "Assets/StreamingAssets/unity.js/types");
			var tsconfigDir = System.IO.Path.Combine(projectRoot, "Assets/StreamingAssets/unity.js");

			// Ensure the stub is up-to-date
			var stubPath = System.IO.Path.Combine(tsDir, "unity.d.ts");
			System.IO.File.WriteAllText(stubPath, m_Content);

			var psi = new System.Diagnostics.ProcessStartInfo
			{
				FileName = tscPath,
				Arguments = "--noEmit --project " + tsconfigDir,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true,
			};

			using var proc = System.Diagnostics.Process.Start(psi);
			var stdout = proc.StandardOutput.ReadToEnd();
			var stderr = proc.StandardError.ReadToEnd();
			proc.WaitForExit(30000);

			var output = (stdout + "\n" + stderr).Trim();
			Assert.AreEqual(0, proc.ExitCode, "tsc --noEmit failed:\n" + output);
		}

		// ── Helpers ──────────────────────────────────────────────────

		static Type FindType(string fullName)
		{
			foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
			{
				var t = asm.GetType(fullName);
				if (t != null) return t;
			}
			return null;
		}
	}
}
