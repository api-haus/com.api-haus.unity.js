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

    // ── tsc --noEmit validation ─────────────────────────────────

    [Test]
    public void GenerateContent_PassesTscNoEmit()
    {
      var projectRoot = System.IO.Path.GetFullPath(".");
      var tscPath = System.IO.Path.Combine(projectRoot, "node_modules/.bin/tsc");

      if (!System.IO.File.Exists(tscPath))
      {
        Assert.Inconclusive("tsc not found at " + tscPath + " — run npm install");
        return;
      }

      var tsconfigPath = System.IO.Path.Combine(projectRoot, "tsconfig.json");
      if (!System.IO.File.Exists(tsconfigPath))
      {
        Assert.Inconclusive("tsconfig.json not found at project root — domain reload should generate it");
        return;
      }

      // Ensure the stub is up-to-date in Library/
      var typesDir = System.IO.Path.Combine(projectRoot, "Library/unity.js/types");
      if (!System.IO.Directory.Exists(typesDir))
        System.IO.Directory.CreateDirectory(typesDir);
      var stubPath = System.IO.Path.Combine(typesDir, "unity.d.ts");
      System.IO.File.WriteAllText(stubPath, m_Content);

      var psi = new System.Diagnostics.ProcessStartInfo
      {
        FileName = tscPath,
        Arguments = "--noEmit --project " + projectRoot,
        WorkingDirectory = projectRoot,
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
        if (t != null)
          return t;
      }

      return null;
    }
  }
}
