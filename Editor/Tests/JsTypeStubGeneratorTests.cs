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
    public void GenerateContent_HasEnumSection()
    {
      StringAssert.Contains("Enum Type Aliases", m_Content);
    }

    [Test]
    public void GenerateContent_HasComponentSection()
    {
      StringAssert.Contains("Component Interfaces", m_Content);
    }

    [Test]
    public void GenerateContent_HasComponentsModule()
    {
      StringAssert.Contains("declare module 'unity.js/components'", m_Content);
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
      // LocalTransform uses ComponentAccessor (get + set) inside the module declaration
      StringAssert.Contains(
        "export const LocalTransform: ComponentAccessor<LocalTransform>;",
        m_Content
      );
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
        Assert.Inconclusive(
          "JsECSCharacterStatsBridge not found — project components may not be compiled"
        );
        return;
      }

      var field = bridgeType.GetField("Descriptions", BindingFlags.Public | BindingFlags.Static);
      Assert.IsNotNull(field, "Descriptions field should exist on bridge with documented struct");

      var dict = field.GetValue(null) as Dictionary<string, string>;
      Assert.IsNotNull(dict, "Descriptions should be a Dictionary<string, string>");
      Assert.IsTrue(dict.ContainsKey(""), "Should have struct-level description (empty key)");
      Assert.IsTrue(
        dict.ContainsKey("maxSpeed"),
        "Should have field-level description for maxSpeed"
      );
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

    // ── ECSCharacterState component accessor ───────────────────

    [Test]
    public void GenerateContent_ECSCharacterState_HasComponentAccessor()
    {
      if (!m_Content.Contains("interface ECSCharacterState"))
      {
        Assert.Inconclusive("ECSCharacterState not in stub");
        return;
      }

      StringAssert.Contains(
        "export const ECSCharacterState: ComponentAccessor<ECSCharacterState>;",
        m_Content
      );
    }

    // ── Helpers ──────────────────────────────────────────────────

    static Type FindType(string fullName)
    {
      foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
      {
        var t = asm.GetType(fullName);
        if (t != null)
          return t;
      }

      return null;
    }
  }
}
