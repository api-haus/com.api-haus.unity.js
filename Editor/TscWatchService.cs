#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using Unity.Logging;
using UnityEngine;

namespace UnityJS.Editor
{
  public enum TscWatchState { Dead, Idle, Compiling, Success, Error }

  /// <summary>
  /// Persistent tsc --watch process manager. Replaces one-shot compilation on domain reload
  /// with a long-lived watcher that recompiles on every .ts save.
  /// </summary>
  [InitializeOnLoad]
  static class TscWatchService
  {
    public static TscWatchState State { get; private set; } = TscWatchState.Dead;
    public static event Action StateChanged;

    static void SetState(TscWatchState newState)
    {
      if (State == newState) return;
      State = newState;
      EditorApplication.delayCall += () => StateChanged?.Invoke();
    }

    const string PidKey = "TscWatchService.Pid";
    const int MaxConsecutiveFails = 3;

    static Process s_Process;
    static int s_ConsecutiveFails;
    static long s_StartTimeTicks;
    static string s_NodePath;

    static readonly List<string> s_LastErrors = new();
    public static bool LastCompilationSucceeded { get; private set; } = true;
    public static IReadOnlyList<string> LastErrors => s_LastErrors;

    static TscWatchService()
    {
      EditorApplication.delayCall += EnsureWatching;
      EditorApplication.quitting += StopWatch;
    }

    static void EnsureWatching()
    {
      if (s_Process != null && !s_Process.HasExited)
        return;

      // Kill adopted process from previous domain reload — we can't re-attach
      // OutputDataReceived after domain reload, so start fresh every time.
      var storedPid = SessionState.GetInt(PidKey, -1);
      if (storedPid > 0)
      {
        try
        {
          var existing = Process.GetProcessById(storedPid);
          if (!existing.HasExited)
            existing.Kill();
        }
        catch
        {
          // Process gone
        }

        SessionState.EraseInt(PidKey);
      }

      StartWatch();
    }

    static void StartWatch()
    {
      var tsconfigPath = TscCompiler.TsconfigPath;
      if (!File.Exists(tsconfigPath))
      {
        Log.Warning("[TscWatchService] tsconfig.json not found — falling back to one-shot compile");
        SetState(TscWatchState.Dead);
        TscCompiler.OnDomainReload();
        return;
      }

      if (s_NodePath == null)
        s_NodePath = FindNode();

      if (s_NodePath == null)
      {
        Log.Warning("[TscWatchService] node not found — falling back to one-shot compile");
        SetState(TscWatchState.Dead);
        TscCompiler.OnDomainReload();
        return;
      }

      var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
      var tscPath = Path.Combine(projectRoot, "node_modules", ".bin", "tsc");
      if (!File.Exists(tscPath))
      {
        Log.Warning("[TscWatchService] node_modules/.bin/tsc not found — falling back to one-shot compile");
        SetState(TscWatchState.Dead);
        TscCompiler.OnDomainReload();
        return;
      }

      TscCompiler.CleanOutDir();

      var psi = new ProcessStartInfo
      {
        FileName = s_NodePath,
        Arguments = $"\"{tscPath}\" --watch --preserveWatchOutput -p \"{tsconfigPath}\"",
        WorkingDirectory = projectRoot,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
      };

      try
      {
        s_Process = Process.Start(psi);
        if (s_Process == null)
        {
          Log.Error("[TscWatchService] Failed to start tsc --watch process");
          SetState(TscWatchState.Dead);
          return;
        }

        s_StartTimeTicks = DateTime.UtcNow.Ticks;
        SessionState.SetInt(PidKey, s_Process.Id);

        s_Process.EnableRaisingEvents = true;
        s_Process.Exited += OnProcessExited;
        s_Process.OutputDataReceived += OnOutput;
        s_Process.ErrorDataReceived += OnError;
        s_Process.BeginOutputReadLine();
        s_Process.BeginErrorReadLine();

        SetState(TscWatchState.Idle);
        Log.Debug("[TscWatchService] Started tsc --watch (PID {0})", s_Process.Id);
      }
      catch (Exception ex)
      {
        Log.Error("[TscWatchService] Failed to start tsc --watch: {0}", ex.Message);
        SetState(TscWatchState.Dead);
      }
    }

    static void OnOutput(object sender, DataReceivedEventArgs e)
    {
      if (string.IsNullOrEmpty(e.Data))
        return;

      // tsc --watch emits error diagnostics to stdout, not stderr
      if (e.Data.Contains("error TS"))
      {
        s_LastErrors.Add(e.Data);
        LastCompilationSucceeded = false;
        SetState(TscWatchState.Error);
        Log.Error("[TscWatchService] {0}", e.Data);
        return;
      }

      if (e.Data.Contains("File change detected"))
      {
        s_LastErrors.Clear();
        LastCompilationSucceeded = true;
        SetState(TscWatchState.Compiling);
        Log.Debug("[TscWatchService] Recompiling...");
      }
      else if (e.Data.Contains("Watching for file changes"))
      {
        s_ConsecutiveFails = 0;
        if (e.Data.Contains("Found 0 errors"))
        {
          Log.Debug("[TscWatchService] Compilation successful");
          SetState(TscWatchState.Success);
        }
        else if (s_LastErrors.Count > 0)
        {
          Log.Debug("[TscWatchService] Compilation finished with errors");
          SetState(TscWatchState.Error);
        }
        else
        {
          Log.Debug("[TscWatchService] tsc --watch ready");
          SetState(TscWatchState.Success);
        }
      }
    }

    static void OnError(object sender, DataReceivedEventArgs e)
    {
      if (string.IsNullOrEmpty(e.Data))
        return;

      if (e.Data.Contains("error TS"))
        Log.Error("[TscWatchService] {0}", e.Data);
      else
        Log.Warning("[TscWatchService] stderr: {0}", e.Data);
    }

    static void OnProcessExited(object sender, EventArgs e)
    {
      var exitCode = -1;
      try { exitCode = s_Process?.ExitCode ?? -1; } catch { }
      SetState(TscWatchState.Dead);
      Log.Warning("[TscWatchService] tsc --watch exited (code {0})", exitCode);

      SessionState.EraseInt(PidKey);

      var elapsed = DateTime.UtcNow.Ticks - s_StartTimeTicks;
      if (elapsed < 5 * TimeSpan.TicksPerSecond)
      {
        s_ConsecutiveFails++;
        if (s_ConsecutiveFails >= MaxConsecutiveFails)
        {
          Log.Error(
            "[TscWatchService] tsc --watch crashed {0} times in a row — giving up", MaxConsecutiveFails);
          return;
        }
      }

      EditorApplication.delayCall += EnsureWatching;
    }

    [MenuItem("Tools/JS/Restart tsc --watch")]
    static void RestartMenu()
    {
      StopWatch();
      s_ConsecutiveFails = 0;
      EditorApplication.delayCall += EnsureWatching;
    }

    [MenuItem("Tools/JS/Stop tsc --watch")]
    static void StopMenu()
    {
      StopWatch();
      Log.Debug("[TscWatchService] Stopped tsc --watch");
    }

    static void StopWatch()
    {
      if (s_Process == null)
        return;

      try
      {
        if (!s_Process.HasExited)
          s_Process.Kill();
      }
      catch
      {
        // Already exited
      }

      s_Process = null;
      SessionState.EraseInt(PidKey);
      SetState(TscWatchState.Dead);
    }

    static string FindNode()
    {
      var candidates = new[]
      {
        "/usr/bin/node",
        "/usr/local/bin/node",
        "/home/" + Environment.UserName + "/.nvm/current/bin/node",
        "node",
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
#endif
