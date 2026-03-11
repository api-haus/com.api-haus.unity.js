using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace LuaGame.Editor.Tests
{
	public class LuaTypeStubGeneratorTests
	{
		string m_Content;

		[OneTimeSetUp]
		public void Setup()
		{
			m_Content = LuaTypeStubGenerator.GenerateContent();
		}

		// ── Structure ────────────────────────────────────────────────

		[Test]
		public void GenerateContent_StartsWithMeta()
		{
			StringAssert.Contains("---@meta", m_Content);
		}

		[Test]
		public void GenerateContent_HasAliases()
		{
			StringAssert.Contains("---@alias entity integer", m_Content);
			StringAssert.Contains("---@alias vec2", m_Content);
			StringAssert.Contains("---@alias vec3", m_Content);
			StringAssert.Contains("---@alias vec4", m_Content);
			StringAssert.Contains("---@alias quat", m_Content);
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
		public void GenerateContent_HasBuiltinSection()
		{
			StringAssert.Contains("Built-in Bridges", m_Content);
		}

		[Test]
		public void GenerateContent_HasLifecycleSection()
		{
			StringAssert.Contains("ECS Script Lifecycle", m_Content);
			StringAssert.Contains("function OnInit(entity, state) end", m_Content);
			StringAssert.Contains("function OnUpdate(dt) end", m_Content);
			StringAssert.Contains("function OnDestroy(entity, state) end", m_Content);
			StringAssert.Contains("function OnCommand(entity, state, cmd) end", m_Content);
		}

		// ── Bridged Components ───────────────────────────────────────

		[Test]
		public void GenerateContent_ContainsBridgedComponents()
		{
			// LocalTransform is always bridged via [assembly: LuaBridge(typeof(LocalTransform))]
			StringAssert.Contains("---@class LocalTransform", m_Content);
			StringAssert.Contains("local_transform = {}", m_Content);
		}

		[Test]
		public void GenerateContent_ComponentHasGetSet()
		{
			StringAssert.Contains("function local_transform.get(eid) end", m_Content);
			StringAssert.Contains("function local_transform.set(eid, value) end", m_Content);
		}

		[Test]
		public void GenerateContent_ComponentFieldsHaveTypes()
		{
			// LocalTransform has position (vec3), scale (number), rotation (quat)
			StringAssert.Contains("---@field position vec3", m_Content);
			StringAssert.Contains("---@field scale number", m_Content);
			StringAssert.Contains("---@field rotation quat", m_Content);
		}

		// ── Descriptions Dictionaries ────────────────────────────────

		[Test]
		public void BridgeClasses_ExistForKnownComponents()
		{
			// The source generator should produce bridge classes in whatever assembly
			// contains the bridged struct — find at least the LuaECS one
			var bridgeType = FindType("LuaECS.Generated.LuaLocalTransformBridge");
			Assert.IsNotNull(bridgeType, "LuaLocalTransformBridge should exist");
		}

		[Test]
		public void BridgeClasses_HaveDescriptionsField_WhenDocsExist()
		{
			// Components with XML docs should have a Descriptions static field.
			// ECSCharacterStats has docs on struct + all fields.
			var bridgeType = FindType("LuaECS.Generated.LuaECSCharacterStatsBridge");
			if (bridgeType == null)
			{
				Assert.Inconclusive("LuaECSCharacterStatsBridge not found — project components may not be compiled");
				return;
			}

			var field = bridgeType.GetField("Descriptions", BindingFlags.Public | BindingFlags.Static);
			Assert.IsNotNull(field, "Descriptions field should exist on bridge with documented struct");

			var dict = field.GetValue(null) as Dictionary<string, string>;
			Assert.IsNotNull(dict, "Descriptions should be a Dictionary<string, string>");
			Assert.IsTrue(dict.ContainsKey(""), "Should have struct-level description (empty key)");
			Assert.IsTrue(dict.ContainsKey("max_speed"), "Should have field-level description for max_speed");
		}

		[Test]
		public void EnumClasses_HaveDescriptionsField_WhenDocsExist()
		{
			var enumType = FindType("LuaECS.Generated.LuaWanderPlaneEnum");
			if (enumType == null)
			{
				Assert.Inconclusive("LuaWanderPlaneEnum not found — project enums may not be compiled");
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
			// ECSCharacterStats has /// <summary>Runtime character movement stats...</summary>
			var bridgeType = FindType("LuaECS.Generated.LuaECSCharacterStatsBridge");
			if (bridgeType == null)
			{
				Assert.Inconclusive("LuaECSCharacterStatsBridge not found");
				return;
			}

			var field = bridgeType.GetField("Descriptions", BindingFlags.Public | BindingFlags.Static);
			if (field == null)
			{
				Assert.Fail("Descriptions field missing — source generator did not emit docs");
				return;
			}

			var dict = field.GetValue(null) as Dictionary<string, string>;

			// Struct-level doc should appear before ---@class
			if (dict != null && dict.TryGetValue("", out var structDesc))
			{
				var classIdx = m_Content.IndexOf("---@class ECSCharacterStats", StringComparison.Ordinal);
				Assert.Greater(classIdx, 0, "---@class ECSCharacterStats should exist");

				var docLine = $"---{structDesc}";
				var docIdx = m_Content.IndexOf(docLine, StringComparison.Ordinal);
				Assert.Greater(docIdx, 0, $"Struct doc should appear in stub: {docLine}");
				Assert.Less(docIdx, classIdx, "Struct doc should appear BEFORE ---@class");
			}

			// Field-level doc should appear on ---@field line
			if (dict != null && dict.TryGetValue("max_speed", out var fieldDesc))
			{
				StringAssert.Contains($"---@field max_speed number {fieldDesc}", m_Content);
			}
		}

		[Test]
		public void GenerateContent_EnumDocsAppearInStub()
		{
			var enumType = FindType("LuaECS.Generated.LuaWanderPlaneEnum");
			if (enumType == null)
			{
				Assert.Inconclusive("LuaWanderPlaneEnum not found");
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
				var enumIdx = m_Content.IndexOf("---@enum WanderPlane", StringComparison.Ordinal);
				Assert.Greater(enumIdx, 0, "---@enum WanderPlane should exist");

				var docLine = $"---{enumDesc}";
				var docIdx = m_Content.IndexOf(docLine, StringComparison.Ordinal);
				Assert.Greater(docIdx, 0, $"Enum doc should appear in stub: {docLine}");
				Assert.Less(docIdx, enumIdx, "Enum doc should appear BEFORE ---@enum");
			}

			// Member-level doc
			if (dict != null && dict.TryGetValue("XZ", out var memberDesc))
			{
				StringAssert.Contains($"XZ = 1, ---{memberDesc}", m_Content);
			}
		}

		// ── ReadOnly component (NeedSetters = false) ─────────────────

		[Test]
		public void GenerateContent_ReadOnlyComponent_NoSetFunction()
		{
			// ECSCharacterState has NeedSetters = false
			if (!m_Content.Contains("---@class ECSCharacterState"))
			{
				Assert.Inconclusive("ECSCharacterState not in stub");
				return;
			}

			StringAssert.Contains("function char_state.get(eid) end", m_Content);
			Assert.IsFalse(
				m_Content.Contains("function char_state.set("),
				"Read-only component should not have a set function"
			);
		}

		// ── Enum field types ─────────────────────────────────────────

		[Test]
		public void GenerateContent_EnumFieldUsesEnumTypeName()
		{
			// SlimeWanderConfig.wanderPlane should map to WanderPlane type
			if (!m_Content.Contains("---@class SlimeWanderConfig"))
			{
				Assert.Inconclusive("SlimeWanderConfig not in stub");
				return;
			}

			StringAssert.Contains("---@field wander_plane WanderPlane", m_Content);
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
