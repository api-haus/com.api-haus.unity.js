namespace UnityJS.Entities.EditModeTests
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using System.Text.RegularExpressions;
  using NUnit.Framework;
  using UnityEngine;
  using UnityEngine.TestTools;
  using UnityJS.Runtime;

  /// <summary>
  /// Fixture-based hot-reload stress tests using JsTranspiler (synchronous).
  /// Copies tests~/hot-reload-fixture/ to a temp dir, transpiles with Sucrase,
  /// mutates .ts files, re-transpiles, and verifies output is consistent.
  /// </summary>
  [TestFixture]
  [Timeout(60000)]
  public class JsHotReloadFixtureStressTests
  {
    static readonly string[] ModuleNames =
    {
      "core",
      "math_utils",
      "transform",
      "physics",
      "ai_state",
      "ai_behavior",
      "renderer",
      "main_system",
    };

    static readonly Dictionary<string, long> DefaultValues = new()
    {
      { "core", 1 },
      { "math_utils", 10 },
      { "transform", 100 },
      { "physics", 1000 },
      { "ai_state", 10000 },
      { "ai_behavior", 100000 },
      { "renderer", 1000000 },
      { "main_system", 10000000 },
    };

    enum MutationType
    {
      TouchComment,
      ChangeConstant,
      InjectSyntaxError,
      FixSyntaxError,
    }

    string m_WorkDir;
    string m_SrcDir;

    readonly Dictionary<string, long> m_CurrentValues = new();
    readonly Dictionary<string, bool> m_HasSyntaxError = new();
    int m_MutationCount;

    static string FixtureRoot
    {
      get
      {
        var pkgInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
          typeof(JsHotReloadFixtureStressTests).Assembly
        );
        Assert.IsNotNull(pkgInfo, "Could not resolve unity.js package root");
        return Path.Combine(pkgInfo.resolvedPath, "tests~", "hot-reload-fixture");
      }
    }

    [SetUp]
    public void SetUp()
    {
      m_WorkDir = Path.Combine(
        Path.GetTempPath(),
        "js-fixture-stress-" + Guid.NewGuid().ToString("N")[..8]
      );
      m_SrcDir = Path.Combine(m_WorkDir, "src");
      Directory.CreateDirectory(m_SrcDir);

      var fixtureSrc = Path.Combine(FixtureRoot, "src");
      foreach (var ts in Directory.GetFiles(fixtureSrc, "*.ts"))
        File.Copy(ts, Path.Combine(m_SrcDir, Path.GetFileName(ts)));

      // Ensure VM exists
      var vm = JsRuntimeManager.GetOrCreate();
      Assert.IsNotNull(vm, "VM must exist");
      foreach (var mod in ModuleNames)
      {
        var tsSource = File.ReadAllText(TsPath(mod));
        var js = JsTranspiler.Transpile(tsSource, TsPath(mod));
        Assert.IsNotNull(js, $"Initial transpile of {mod} failed");
      }
      Assert.IsTrue(JsTranspiler.IsInitialized, "JsTranspiler must be initialized after first transpile");

      m_CurrentValues.Clear();
      m_HasSyntaxError.Clear();
      m_MutationCount = 0;
      foreach (var mod in ModuleNames)
      {
        m_CurrentValues[mod] = DefaultValues[mod];
        m_HasSyntaxError[mod] = false;
      }
    }

    [TearDown]
    public void TearDown()
    {
      if (m_WorkDir != null && Directory.Exists(m_WorkDir))
      {
        try { Directory.Delete(m_WorkDir, true); }
        catch { /* best-effort */ }
      }
    }

    string TsPath(string module) => Path.Combine(m_SrcDir, module + ".ts");

    string TranspileModule(string module)
    {
      var vm = JsRuntimeManager.GetOrCreate();
      var tsSource = File.ReadAllText(TsPath(module));
      return JsTranspiler.Transpile(tsSource, TsPath(module));
    }

    void ReplaceSlotLine(string module, string slotMarker, string newLine)
    {
      var path = TsPath(module);
      var lines = File.ReadAllLines(path).ToList();
      var idx = lines.FindIndex(l => l.Contains(slotMarker));
      Assert.GreaterOrEqual(idx, 0, $"Slot marker '{slotMarker}' not found in {module}.ts");
      if (idx + 1 < lines.Count)
        lines[idx + 1] = newLine;
      else
        lines.Add(newLine);
      File.WriteAllLines(path, lines);
    }

    void ApplyMutation(string module, MutationType type, System.Random rng)
    {
      var path = TsPath(module);
      switch (type)
      {
        case MutationType.TouchComment:
          var n = ++m_MutationCount;
          ReplaceSlotLine(module, "COMMENT_SLOT", $"// touch {n}");
          break;

        case MutationType.ChangeConstant:
          var newVal = rng.Next(1, 9999);
          ReplaceSlotLine(module, "CONST_SLOT", $"export const VALUE = {newVal};");
          m_CurrentValues[module] = newVal;
          break;

        case MutationType.InjectSyntaxError:
          if (m_HasSyntaxError[module])
            break;
          var content = File.ReadAllText(path);
          var importIdx = content.IndexOf("import", StringComparison.Ordinal);
          if (importIdx < 0)
            importIdx = 0;
          var eol = content.IndexOf('\n', importIdx);
          if (eol < 0)
            break;
          content = content.Insert(eol + 1, "{{{SYNTAX_ERROR\n");
          File.WriteAllText(path, content);
          m_HasSyntaxError[module] = true;
          break;

        case MutationType.FixSyntaxError:
          var text = File.ReadAllText(path);
          text = text.Replace("{{{SYNTAX_ERROR\n", "");
          text = text.Replace("{{{SYNTAX_ERROR", "");
          File.WriteAllText(path, text);
          m_HasSyntaxError[module] = false;
          break;
      }
    }

    [Test]
    public void CommentOnlyMutations_AllTranspileSucceed()
    {
      var rng = new System.Random(42);

      for (var cycle = 0; cycle < 10; cycle++)
      {
        var module = ModuleNames[rng.Next(ModuleNames.Length)];
        ApplyMutation(module, MutationType.TouchComment, rng);

        var js = TranspileModule(module);
        Assert.IsNotNull(js,
          $"Cycle {cycle}: comment-only mutation to {module} must transpile. Error: {JsTranspiler.LastError}");
      }
    }

    [Test]
    public void ConstantMutations_TranspileReflectsNewValues()
    {
      var rng = new System.Random(123);

      for (var cycle = 0; cycle < 8; cycle++)
      {
        var module = ModuleNames[cycle % ModuleNames.Length];
        ApplyMutation(module, MutationType.ChangeConstant, rng);

        var js = TranspileModule(module);
        Assert.IsNotNull(js,
          $"Cycle {cycle}: constant change in {module} must transpile. Error: {JsTranspiler.LastError}");

        var expected = m_CurrentValues[module];
        Assert.IsTrue(
          js.Contains($"VALUE = {expected}") || js.Contains($"VALUE={expected}"),
          $"Cycle {cycle}: transpiled JS for {module} missing VALUE={expected}"
        );
      }
    }

    [Test]
    public void SyntaxErrorRecovery_ErrorThenFix()
    {
      var rng = new System.Random(99);
      var target = "core";

      for (var cycle = 0; cycle < 5; cycle++)
      {
        ApplyMutation(target, MutationType.InjectSyntaxError, rng);
        LogAssert.Expect(LogType.Error, new Regex("\\[JsTranspiler\\]"));
        var js = TranspileModule(target);
        Assert.IsNull(js, $"Cycle {cycle}: should fail with syntax error");

        ApplyMutation(target, MutationType.FixSyntaxError, rng);
        js = TranspileModule(target);
        Assert.IsNotNull(js,
          $"Cycle {cycle}: should succeed after fix. Error: {JsTranspiler.LastError}");
      }
    }

    [Test]
    public void MixedMutations_LedgerStaysConsistent()
    {
      var rng = new System.Random(456);

      for (var i = 0; i < 5; i++)
      {
        var module = ModuleNames[rng.Next(ModuleNames.Length)];
        ApplyMutation(module, MutationType.ChangeConstant, rng);
      }

      foreach (var mod in ModuleNames)
      {
        var js = TranspileModule(mod);
        Assert.IsNotNull(js, $"Transpile of {mod} failed. Error: {JsTranspiler.LastError}");
        var expected = m_CurrentValues[mod];
        Assert.IsTrue(
          js.Contains($"VALUE = {expected}") || js.Contains($"VALUE={expected}"),
          $"Module {mod}: expected VALUE={expected} in transpiled output"
        );
      }
    }
  }
}
