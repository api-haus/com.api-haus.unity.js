#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using Unity.Logging;
using UnityEngine;

namespace UnityJS.Editor
{
  public enum TscState
  {
    Dead,
    Compiling,
    Success,
    Error,
  }

  public sealed class TscCompiler
  {
    const string EpochKey = "TscCompiler.Epoch";

    public static TscCompiler Instance { get; internal set; }

    public string SourceRoot { get; }
    public string TsconfigPath { get; }
    public string OutDir { get; }

    public TscState State { get; private set; }
    public event Action StateChanged;
    public int Epoch { get; private set; }
    public bool LastCompilationSucceeded { get; private set; }

    readonly List<string> m_Errors = new();
    public IReadOnlyList<string> LastErrors => m_Errors;

    public TscCompiler(string sourceRoot, string outDir = null)
    {
      SourceRoot = Path.GetFullPath(sourceRoot);
      TsconfigPath = Path.Combine(SourceRoot, "tsconfig.json");
      OutDir =
        outDir != null
          ? Path.GetFullPath(outDir)
          : Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "TscBuild"));
      Epoch = SessionState.GetInt(EpochKey, 0);
      LastCompilationSucceeded = true;
      State = File.Exists(TsconfigPath) ? TscState.Success : TscState.Dead;
    }

    void SetState(TscState newState)
    {
      if (State == newState)
        return;
      State = newState;
      EditorApplication.delayCall += () => StateChanged?.Invoke();
    }

    public bool Recompile()
    {
      if (!File.Exists(TsconfigPath))
      {
        SetState(TscState.Dead);
        return false;
      }

      SetState(TscState.Compiling);

      if (Directory.Exists(OutDir))
        Directory.Delete(OutDir, true);

      var (success, errors) = RunTscProcess();

      m_Errors.Clear();
      m_Errors.AddRange(errors);
      LastCompilationSucceeded = success;

      if (success)
      {
        Epoch++;
        SessionState.SetInt(EpochKey, Epoch);
      }

      SetState(success ? TscState.Success : TscState.Error);
      return success;
    }

    public bool IsStale()
    {
      if (!Directory.Exists(OutDir))
        return true;

      foreach (var subdir in new[] { "systems", "components", "types" })
      {
        var tsDir = Path.Combine(SourceRoot, subdir);
        if (!Directory.Exists(tsDir))
          continue;

        var outSubdir = Path.Combine(OutDir, subdir);
        var tsFiles = Directory.GetFiles(tsDir, "*.ts", SearchOption.AllDirectories);
        foreach (var ts in tsFiles)
        {
          var relative = ts.Substring(tsDir.Length)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
          var jsName = Path.ChangeExtension(relative, ".js");
          var jsPath = Path.Combine(outSubdir, jsName);
          if (!File.Exists(jsPath))
            return true;
          if (File.GetLastWriteTimeUtc(ts) > File.GetLastWriteTimeUtc(jsPath))
            return true;
        }

        // Check for dead .js files with no corresponding .ts
        if (!Directory.Exists(outSubdir))
          continue;
        var jsFiles = Directory.GetFiles(outSubdir, "*.js", SearchOption.AllDirectories);
        foreach (var js in jsFiles)
        {
          var relative = js.Substring(outSubdir.Length)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
          var tsName = Path.ChangeExtension(relative, ".ts");
          var tsPath = Path.Combine(tsDir, tsName);
          if (!File.Exists(tsPath))
            return true;
        }
      }

      return false;
    }

    public bool RecompileIfStale()
    {
      if (!IsStale())
        return false;
      return Recompile();
    }

    (bool success, List<string> errors) RunTscProcess()
    {
      var errors = new List<string>();
      var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
      var psi = new ProcessStartInfo
      {
        FileName = "npx",
        Arguments = $"tsc -p \"{TsconfigPath}\"",
        WorkingDirectory = projectRoot,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
      };

      using var proc = Process.Start(psi);
      if (proc == null)
        return (false, new List<string> { "Failed to start tsc process" });

      var stdout = proc.StandardOutput.ReadToEnd();
      var stderr = proc.StandardError.ReadToEnd();
      proc.WaitForExit(30000);

      foreach (var line in (stdout + "\n" + stderr).Split('\n'))
        if (line.Contains("error TS"))
          errors.Add(line.Trim());

      var success = proc.ExitCode == 0;
      if (success)
      {
        var jsCount = Directory.Exists(OutDir)
          ? Directory.GetFiles(OutDir, "*.js", SearchOption.AllDirectories).Length
          : 0;
        Log.Debug("[TscCompiler] Compiled {0} file(s) to Library/TscBuild/", jsCount);
      }
      else
      {
        Log.Error(
          "[TscCompiler] tsc failed with {0} error(s):\n{1}",
          errors.Count,
          (stdout + "\n" + stderr).Trim()
        );
      }

      return (success, errors);
    }

    [MenuItem("Tools/JS/Recompile TypeScript")]
    static void RecompileMenu() => Instance?.Recompile();
  }
}
#endif
