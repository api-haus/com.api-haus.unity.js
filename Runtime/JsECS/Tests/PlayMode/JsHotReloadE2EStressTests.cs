namespace UnityJS.Entities.PlayModeTests
{
  using System;
  using System.Collections.Concurrent;
  using System.Collections.Generic;
  using System.Diagnostics;
  using System.IO;
  using System.Runtime.InteropServices;
  using System.Text;
  using System.Threading;
  using NUnit.Framework;
  using QJS;
  using Runtime;

  [TestFixture]
  [Timeout(300000)]
  public unsafe class JsHotReloadE2EStressTests
  {
    // ── Module metadata ──

    static readonly string[] ModuleNames =
    {
      "core", "math_utils", "transform", "physics",
      "ai_state", "ai_behavior", "renderer", "main_system"
    };

    // Baseline VALUE constants per module (must match fixture src/)
    static readonly Dictionary<string, int> BaselineValues = new()
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

    // Modules that are not main_system (can be mutated independently)
    static readonly string[] MutableModules =
    {
      "core", "math_utils", "transform", "physics",
      "ai_state", "ai_behavior", "renderer"
    };

    // ── Mutation types ──

    enum MutationType
    {
      SetConstant,
      InjectSyntaxError,
      FixSyntaxError,
      TouchComment,
    }

    struct Mutation
    {
      public string Module;
      public MutationType Type;
      public int Value;
    }

    // ── Expected-state ledger ──

    class ModuleState
    {
      public int ConstantValue;
      public bool HasSyntaxError;
    }

    Dictionary<string, ModuleState> m_Ledger;
    List<(int Round, Mutation Mutation)> m_AuditLog;

    // ── Test infrastructure ──

    string m_FixtureSrcDir;
    string m_WorkDir;
    string m_OutDir;
    Process m_TscProcess;
    JsRuntimeManager m_Vm;
    int m_TouchCounter;

    string m_NodePath;
    ConcurrentQueue<string> m_TscOutputLines;
    ManualResetEventSlim m_TscWatchReady;

    static string FixtureRoot =>
      Path.GetFullPath(
        Path.Combine(
          UnityEngine.Application.dataPath, "..",
          "Packages", "unity.js", "tests~", "hot-reload-fixture"
        )
      );

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
      // Find node binary
      m_NodePath = FindNode();
      Assert.IsNotNull(m_NodePath, "node must be available for hot-reload E2E tests");

      // Verify fixture exists
      m_FixtureSrcDir = Path.Combine(FixtureRoot, "src");
      Assert.IsTrue(
        Directory.Exists(m_FixtureSrcDir),
        $"Fixture src/ not found at {m_FixtureSrcDir}"
      );
    }

    [SetUp]
    public void SetUp()
    {
      // Create temp working copy
      m_WorkDir = Path.Combine(Path.GetTempPath(), "unity_js_hotreload_" + Guid.NewGuid().ToString("N")[..8]);
      var srcCopy = Path.Combine(m_WorkDir, "src");
      m_OutDir = Path.Combine(m_WorkDir, "out");
      Directory.CreateDirectory(srcCopy);
      Directory.CreateDirectory(m_OutDir);

      // Copy fixture sources
      foreach (var file in Directory.GetFiles(m_FixtureSrcDir, "*.ts"))
        File.Copy(file, Path.Combine(srcCopy, Path.GetFileName(file)));

      // Write temp tsconfig
      var tsconfig = Path.Combine(m_WorkDir, "tsconfig.json");
      File.WriteAllText(
        tsconfig,
        @"{
  ""compilerOptions"": {
    ""target"": ""ES2020"",
    ""module"": ""ES2020"",
    ""moduleResolution"": ""node"",
    ""rootDir"": ""src"",
    ""outDir"": ""out"",
    ""strict"": true,
    ""esModuleInterop"": true,
    ""skipLibCheck"": true,
    ""declaration"": false,
    ""sourceMap"": false
  },
  ""include"": [""src/**/*.ts""]
}"
      );

      // Start tsc --watch
      var projectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
      var tscBin = Path.Combine(projectRoot, "node_modules", ".bin", "tsc");
      var psi = new ProcessStartInfo
      {
        FileName = m_NodePath,
        Arguments = $"\"{tscBin}\" --watch --preserveWatchOutput -p \"{tsconfig}\"",
        WorkingDirectory = m_WorkDir,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
      };

      m_TscOutputLines = new ConcurrentQueue<string>();
      m_TscWatchReady = new ManualResetEventSlim(false);

      m_TscProcess = Process.Start(psi);
      Assert.IsNotNull(m_TscProcess, "Failed to start tsc --watch");

      m_TscProcess.OutputDataReceived += (_, e) =>
      {
        if (e.Data == null) return;
        m_TscOutputLines.Enqueue(e.Data);
        if (e.Data.Contains("Watching for file changes"))
          m_TscWatchReady.Set();
      };
      m_TscProcess.ErrorDataReceived += (_, e) =>
      {
        if (e.Data != null)
          m_TscOutputLines.Enqueue(e.Data);
      };
      m_TscProcess.BeginOutputReadLine();
      m_TscProcess.BeginErrorReadLine();

      // Wait for initial compilation
      WaitForTscWatchReady(30000);

      // Verify output files exist
      foreach (var mod in ModuleNames)
        Assert.IsTrue(
          File.Exists(Path.Combine(m_OutDir, mod + ".js")),
          $"Expected {mod}.js in tsc output after initial compile"
        );

      // Set up VM
      JsScriptSearchPaths.Reset();
      m_Vm = new JsRuntimeManager();
      JsScriptSourceRegistry.Register(
        new FileSystemScriptSource("test-fixture", m_OutDir, 0)
      );

      // Load all modules
      foreach (var mod in ModuleNames)
      {
        var jsPath = Path.Combine(m_OutDir, mod + ".js");
        var source = File.ReadAllText(jsPath);
        var scriptId = "fixture:" + mod;
        Assert.IsTrue(
          m_Vm.LoadScriptAsModule(scriptId, source, jsPath),
          $"Failed to load fixture module {mod}"
        );
      }

      // Initialize ledger
      m_Ledger = new Dictionary<string, ModuleState>();
      foreach (var kv in BaselineValues)
        m_Ledger[kv.Key] = new ModuleState { ConstantValue = kv.Value };

      m_AuditLog = new List<(int, Mutation)>();
      m_TouchCounter = 0;

      // Verify baseline
      CallOnUpdateAndAssertLedger(-1);
    }

    [TearDown]
    public void TearDown()
    {
      m_Vm?.Dispose();
      JsScriptSearchPaths.Reset();

      if (m_TscProcess != null)
      {
        try
        {
          if (!m_TscProcess.HasExited)
            m_TscProcess.Kill();
        }
        catch
        {
          // Already gone
        }

        m_TscProcess = null;
      }

      m_TscWatchReady?.Dispose();
      m_TscWatchReady = null;

      if (m_WorkDir != null && Directory.Exists(m_WorkDir))
      {
        try
        {
          Directory.Delete(m_WorkDir, true);
        }
        catch
        {
          // Best effort cleanup
        }
      }
    }

    // ── Tests ──

    [Test]
    public void StressReload_SequentialMutations_LedgerStaysConsistent()
    {
      var rng = new Random(42);
      var types = new[] { MutationType.SetConstant, MutationType.TouchComment };

      for (var round = 0; round < 50; round++)
      {
        var mod = MutableModules[rng.Next(MutableModules.Length)];
        var type = types[rng.Next(types.Length)];
        var value = type == MutationType.SetConstant ? rng.Next(1, 9999) : 0;

        var mutation = new Mutation { Module = mod, Type = type, Value = value };
        ApplyMutation(mutation);
        m_AuditLog.Add((round, mutation));

        if (type == MutationType.SetConstant)
          m_Ledger[mod].ConstantValue = value;

        WaitForTscRecompile();
        ReloadModule(mod);
        CallOnUpdateAndAssertLedger(round);
      }
    }

    [Test]
    public void StressReload_ConcurrentMutations_LedgerStaysConsistent()
    {
      var rng = new Random(123);

      for (var round = 0; round < 30; round++)
      {
        var count = rng.Next(2, 5);
        var mutated = new HashSet<string>();

        for (var i = 0; i < count; i++)
        {
          string mod;
          do
          {
            mod = MutableModules[rng.Next(MutableModules.Length)];
          } while (mutated.Contains(mod));

          mutated.Add(mod);
          var value = rng.Next(1, 9999);
          var mutation = new Mutation { Module = mod, Type = MutationType.SetConstant, Value = value };
          ApplyMutation(mutation);
          m_AuditLog.Add((round, mutation));
          m_Ledger[mod].ConstantValue = value;
        }

        WaitForTscRecompile();

        foreach (var mod in mutated)
          ReloadModule(mod);

        CallOnUpdateAndAssertLedger(round);
      }
    }

    [Test]
    public void StressReload_RapidBurst_NoStaleRefs()
    {
      var rng = new Random(777);
      var targets = new[] { "core", "math_utils", "transform", "physics" };

      for (var round = 0; round < 20; round++)
      {
        // Mutate all 4 targets within the same iteration (as fast as possible)
        foreach (var mod in targets)
        {
          var value = rng.Next(1, 9999);
          var mutation = new Mutation { Module = mod, Type = MutationType.SetConstant, Value = value };
          ApplyMutation(mutation);
          m_AuditLog.Add((round, mutation));
          m_Ledger[mod].ConstantValue = value;
        }

        WaitForTscRecompile();

        // Reload only the mutated modules
        foreach (var mod in targets)
          ReloadModule(mod);

        CallOnUpdateAndAssertLedger(round);
      }
    }

    [Test]
    public void StressReload_SyntaxErrorAndRecovery()
    {
      foreach (var mod in MutableModules)
      {
        // Inject syntax error
        var errorMutation = new Mutation { Module = mod, Type = MutationType.InjectSyntaxError };
        ApplyMutation(errorMutation);
        m_AuditLog.Add((0, errorMutation));
        m_Ledger[mod].HasSyntaxError = true;

        // Wait for tsc to process — it will error, but NOT update the .js output
        WaitForTscError();

        // The .js file is unchanged (tsc doesn't emit on error), so no reload needed.
        // Verify that calling onUpdate still works with the old (valid) module.
        CallOnUpdateAndAssertLedger(-1);

        // Fix syntax error
        var fixMutation = new Mutation { Module = mod, Type = MutationType.FixSyntaxError };
        ApplyMutation(fixMutation);
        m_AuditLog.Add((1, fixMutation));
        m_Ledger[mod].HasSyntaxError = false;

        WaitForTscRecompile();

        // Reload the fixed module
        ReloadModule(mod);
        CallOnUpdateAndAssertLedger(-1);
      }
    }

    // ── Mutation application ──

    void ApplyMutation(Mutation mutation)
    {
      var tsPath = Path.Combine(m_WorkDir, "src", mutation.Module + ".ts");
      var lines = new List<string>(File.ReadAllLines(tsPath));

      switch (mutation.Type)
      {
        case MutationType.SetConstant:
          ReplaceSlotLine(lines, "CONST_SLOT", $"export const VALUE = {mutation.Value};");
          break;

        case MutationType.InjectSyntaxError:
          ReplaceSlotLine(lines, "CONST_SLOT", "export const VALUE = ;"); // parse error
          break;

        case MutationType.FixSyntaxError:
          ReplaceSlotLine(
            lines, "CONST_SLOT",
            $"export const VALUE = {m_Ledger[mutation.Module].ConstantValue};"
          );
          break;

        case MutationType.TouchComment:
          m_TouchCounter++;
          ReplaceSlotLine(lines, "COMMENT_SLOT", $"// touch {m_TouchCounter}");
          break;
      }

      File.WriteAllLines(tsPath, lines);
    }

    static void ReplaceSlotLine(List<string> lines, string slotName, string replacement)
    {
      var marker = $"// --- {slotName} ---";
      for (var i = 0; i < lines.Count; i++)
      {
        if (!lines[i].TrimStart().StartsWith(marker, StringComparison.Ordinal))
          continue;

        // Replace the line after the marker
        if (i + 1 < lines.Count)
          lines[i + 1] = replacement;
        return;
      }

      throw new InvalidOperationException($"Slot marker '{slotName}' not found in file");
    }

    // ── Module reload ──

    void ReloadModule(string moduleName)
    {
      var jsPath = Path.Combine(m_OutDir, moduleName + ".js");
      if (!File.Exists(jsPath))
        return;

      var source = File.ReadAllText(jsPath);
      var scriptId = "fixture:" + moduleName;
      m_Vm.ReloadScript(scriptId, source, jsPath);
    }

    // ── Verification ──

    void CallOnUpdateAndAssertLedger(int round)
    {
      // Call main_system.onUpdate which sums all version() calls into globalThis.__result
      var stateRef = m_Vm.CreateEntityState("fixture:main_system", -1);
      Assert.IsTrue(
        m_Vm.CallFunction("fixture:main_system", "onUpdate", stateRef),
        $"Round {round}: onUpdate should succeed"
      );
      m_Vm.ReleaseEntityState(stateRef);

      var actual = ReadGlobalInt("__result");
      var expected = ComputeExpectedSum();

      if (actual != expected)
      {
        var sb = new StringBuilder();
        sb.AppendLine($"Round {round}: Ledger mismatch! Expected {expected}, got {actual}");
        sb.AppendLine("Ledger state:");
        foreach (var kv in m_Ledger)
          sb.AppendLine(
            $"  {kv.Key}: VALUE={kv.Value.ConstantValue}, error={kv.Value.HasSyntaxError}");
        sb.AppendLine("Audit log:");
        foreach (var (r, m) in m_AuditLog)
          sb.AppendLine($"  Round {r}: {m.Type} on {m.Module} (value={m.Value})");
        Assert.Fail(sb.ToString());
      }
    }

    int ComputeExpectedSum()
    {
      var sum = 0;
      foreach (var kv in m_Ledger)
        sum += kv.Value.ConstantValue;
      return sum;
    }

    int ReadGlobalInt(string name)
    {
      var code = $"globalThis.{name} || 0";
      var src = Encoding.UTF8.GetBytes(code + '\0');
      var file = Encoding.UTF8.GetBytes("<test>\0");
      fixed (byte* pSrc = src, pFile = file)
      {
        var val = QJS.JS_Eval(m_Vm.Context, pSrc, src.Length - 1, pFile, QJS.JS_EVAL_TYPE_GLOBAL);
        int result;
        QJS.JS_ToInt32(m_Vm.Context, &result, val);
        QJS.JS_FreeValue(m_Vm.Context, val);
        return result;
      }
    }

    // ── tsc process helpers ──

    void WaitForTscWatchReady(int timeoutMs)
    {
      if (!m_TscWatchReady.Wait(timeoutMs))
      {
        var output = new StringBuilder();
        while (m_TscOutputLines.TryDequeue(out var line))
          output.AppendLine(line);
        Assert.Fail(
          $"Timed out waiting for tsc --watch ready ({timeoutMs}ms). Output:\n{output}");
      }

      // Reset for next wait
      m_TscWatchReady.Reset();
    }

    void WaitForTscRecompile()
    {
      WaitForTscWatchReady(15000);
    }

    void WaitForTscError()
    {
      // tsc --watch emits "Watching for file changes" even after errors
      WaitForTscWatchReady(15000);
    }

    static string FindNode()
    {
      var candidates = new[]
      {
        "/usr/bin/node",
        "/usr/local/bin/node",
        "/home/" + Environment.UserName + "/.nvm/current/bin/node",
        "node", // PATH fallback
      };

      foreach (var candidate in candidates)
      {
        try
        {
          var psi = new ProcessStartInfo
          {
            FileName = candidate,
            Arguments = "--version",
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
          };
          using var proc = Process.Start(psi);
          proc?.WaitForExit(5000);
          if (proc is { ExitCode: 0 })
            return candidate;
        }
        catch
        {
          // Try next
        }
      }

      return null;
    }
  }
}
