namespace UnityJS.Entities.EditModeTests
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Linq;
  using NUnit.Framework;
  using UnityJS.Editor;

  /// <summary>
  /// Fixture-based hot-reload stress tests using TscCompiler (synchronous).
  /// Copies tests~/hot-reload-fixture/ to a temp dir, compiles with TscCompiler,
  /// mutates .ts files, recompiles, and verifies compiled output is consistent.
  /// </summary>
  [TestFixture]
  [Timeout(60000)]
  public class JsHotReloadFixtureStressTests
  {
    static readonly string[] ModuleNames =
    {
      "core", "math_utils", "transform", "physics",
      "ai_state", "ai_behavior", "renderer", "main_system",
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

    enum MutationType { TouchComment, ChangeConstant, InjectSyntaxError, FixSyntaxError }

    string m_WorkDir;
    string m_OutDir;
    string m_SrcDir;
    TscCompiler m_Compiler;

    // Track current VALUE per module (after mutations)
    readonly Dictionary<string, long> m_CurrentValues = new();
    // Track which modules have injected syntax errors
    readonly Dictionary<string, bool> m_HasSyntaxError = new();
    int m_MutationCount;

    static string FixtureRoot
    {
      get
      {
        var pkgInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
          typeof(JsHotReloadFixtureStressTests).Assembly);
        Assert.IsNotNull(pkgInfo, "Could not resolve unity.js package root");
        return Path.Combine(pkgInfo.resolvedPath, "tests~", "hot-reload-fixture");
      }
    }

    [SetUp]
    public void SetUp()
    {
      m_WorkDir = Path.Combine(Path.GetTempPath(), "js-fixture-stress-" + Guid.NewGuid().ToString("N")[..8]);
      m_SrcDir = Path.Combine(m_WorkDir, "src");
      m_OutDir = Path.Combine(m_WorkDir, "out");
      Directory.CreateDirectory(m_SrcDir);

      // Copy fixture .ts files
      var fixtureSrc = Path.Combine(FixtureRoot, "src");
      foreach (var ts in Directory.GetFiles(fixtureSrc, "*.ts"))
        File.Copy(ts, Path.Combine(m_SrcDir, Path.GetFileName(ts)));

      // Copy tsconfig.json
      File.Copy(Path.Combine(FixtureRoot, "tsconfig.json"), Path.Combine(m_WorkDir, "tsconfig.json"));

      m_Compiler = new TscCompiler(m_WorkDir, m_OutDir);
      Assert.IsTrue(m_Compiler.Recompile(), "Initial compile failed:\n" + string.Join("\n", m_Compiler.LastErrors));

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
        catch { /* best-effort cleanup */ }
      }
    }

    // ── Mutation helpers ──

    string TsPath(string module) => Path.Combine(m_SrcDir, module + ".ts");

    void ReplaceSlotLine(string module, string slotMarker, string newLine)
    {
      var path = TsPath(module);
      var lines = File.ReadAllLines(path).ToList();
      var idx = lines.FindIndex(l => l.Contains(slotMarker));
      Assert.GreaterOrEqual(idx, 0, $"Slot marker '{slotMarker}' not found in {module}.ts");
      // Replace the line AFTER the marker
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
          if (m_HasSyntaxError[module]) break;
          var content = File.ReadAllText(path);
          var importIdx = content.IndexOf("import", StringComparison.Ordinal);
          if (importIdx < 0) importIdx = 0;
          var eol = content.IndexOf('\n', importIdx);
          if (eol < 0) break;
          content = content.Insert(eol + 1, "{{{SYNTAX_ERROR}}}\n");
          File.WriteAllText(path, content);
          m_HasSyntaxError[module] = true;
          break;

        case MutationType.FixSyntaxError:
          var text = File.ReadAllText(path);
          text = text.Replace("{{{SYNTAX_ERROR}}}\n", "");
          text = text.Replace("{{{SYNTAX_ERROR}}}", "");
          File.WriteAllText(path, text);
          m_HasSyntaxError[module] = false;
          break;
      }
    }

    // ── Tests ──

    [Test]
    public void CommentOnlyMutations_AllCompileSucceed()
    {
      var rng = new System.Random(42);

      for (var cycle = 0; cycle < 10; cycle++)
      {
        var module = ModuleNames[rng.Next(ModuleNames.Length)];
        ApplyMutation(module, MutationType.TouchComment, rng);

        Assert.IsTrue(m_Compiler.Recompile(),
          $"Cycle {cycle}: comment-only mutation to {module} must compile:\n"
          + string.Join("\n", m_Compiler.LastErrors));
      }
    }

    [Test]
    public void ConstantMutations_RecompileReflectsNewValues()
    {
      var rng = new System.Random(123);

      for (var cycle = 0; cycle < 8; cycle++)
      {
        var module = ModuleNames[cycle % ModuleNames.Length];
        ApplyMutation(module, MutationType.ChangeConstant, rng);

        Assert.IsTrue(m_Compiler.Recompile(),
          $"Cycle {cycle}: constant change in {module} must compile:\n"
          + string.Join("\n", m_Compiler.LastErrors));

        // Verify the compiled JS contains the new value
        var jsPath = Path.Combine(m_OutDir, module + ".js");
        var jsContent = File.ReadAllText(jsPath);
        Assert.IsTrue(
          jsContent.Contains($"VALUE = {m_CurrentValues[module]}") ||
          jsContent.Contains($"VALUE={m_CurrentValues[module]}"),
          $"Cycle {cycle}: compiled JS for {module} missing VALUE={m_CurrentValues[module]}");
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
        Assert.IsFalse(m_Compiler.Recompile(),
          $"Cycle {cycle}: should fail with syntax error");
        Assert.IsNotEmpty(m_Compiler.LastErrors,
          $"Cycle {cycle}: failed compile but no errors reported");

        ApplyMutation(target, MutationType.FixSyntaxError, rng);
        Assert.IsTrue(m_Compiler.Recompile(),
          $"Cycle {cycle}: should succeed after fix:\n"
          + string.Join("\n", m_Compiler.LastErrors));
      }
    }

    [Test]
    public void MixedMutations_LedgerStaysConsistent()
    {
      var rng = new System.Random(456);

      // Mutate several modules with constant changes
      for (var i = 0; i < 5; i++)
      {
        var module = ModuleNames[rng.Next(ModuleNames.Length)];
        ApplyMutation(module, MutationType.ChangeConstant, rng);
      }

      Assert.IsTrue(m_Compiler.Recompile(),
        "Recompile after mixed mutations must succeed:\n"
        + string.Join("\n", m_Compiler.LastErrors));

      // Verify each compiled module has the expected VALUE
      foreach (var mod in ModuleNames)
      {
        var jsPath = Path.Combine(m_OutDir, mod + ".js");
        Assert.IsTrue(File.Exists(jsPath), $"Missing compiled JS for {mod}");
        var jsContent = File.ReadAllText(jsPath);
        var expected = m_CurrentValues[mod];
        Assert.IsTrue(
          jsContent.Contains($"VALUE = {expected}") ||
          jsContent.Contains($"VALUE={expected}"),
          $"Module {mod}: expected VALUE={expected} in compiled output");
      }
    }
  }
}
