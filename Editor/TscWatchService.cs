#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using Unity.Logging;
using UnityEngine;

namespace UnityJS.Editor
{
  /// <summary>
  /// Persistent tsc --watch process manager. Replaces one-shot compilation on domain reload
  /// with a long-lived watcher that recompiles on every .ts save.
  /// </summary>
  [InitializeOnLoad]
  static class TscWatchService
  {
    const string PidKey = "TscWatchService.Pid";
    const int MaxConsecutiveFails = 3;

    static Process s_Process;
    static int s_ConsecutiveFails;
    static long s_StartTimeTicks;
    static string s_NodePath;

    static TscWatchService()
    {
      EditorApplication.delayCall += EnsureWatching;
      EditorApplication.quitting += StopWatch;
    }

    static void EnsureWatching()
    {
      if (s_Process != null && !s_Process.HasExited)
        return;

      // Try to adopt process from previous domain reload
      var storedPid = SessionState.GetInt(PidKey, -1);
      if (storedPid > 0)
      {
        try
        {
          var existing = Process.GetProcessById(storedPid);
          if (!existing.HasExited)
          {
            s_Process = existing;
            return;
          }
        }
        catch
        {
          // Process gone — start fresh
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
        TscCompiler.OnDomainReload();
        return;
      }

      if (s_NodePath == null)
        s_NodePath = FindNode();

      if (s_NodePath == null)
      {
        Log.Warning("[TscWatchService] node not found — falling back to one-shot compile");
        TscCompiler.OnDomainReload();
        return;
      }

      var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
      var tscPath = Path.Combine(projectRoot, "node_modules", ".bin", "tsc");
      if (!File.Exists(tscPath))
      {
        Log.Warning("[TscWatchService] node_modules/.bin/tsc not found — falling back to one-shot compile");
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

        Log.Debug("[TscWatchService] Started tsc --watch (PID {0})", s_Process.Id);
      }
      catch (Exception ex)
      {
        Log.Error("[TscWatchService] Failed to start tsc --watch: {0}", ex.Message);
      }
    }

    static void OnOutput(object sender, DataReceivedEventArgs e)
    {
      if (string.IsNullOrEmpty(e.Data))
        return;

      // tsc --watch emits error diagnostics to stdout, not stderr
      if (e.Data.Contains("error TS"))
      {
        Log.Error("[TscWatchService] {0}", e.Data);
        return;
      }

      if (e.Data.Contains("File change detected"))
      {
        Log.Debug("[TscWatchService] Recompiling...");
      }
      else if (e.Data.Contains("Watching for file changes"))
      {
        s_ConsecutiveFails = 0;
        // "Found 0 errors. Watching..." → success
        // "Found N error(s). Watching..." → errors already logged individually above
        // Initial "Watching for file changes" → startup
        if (e.Data.Contains("Found 0 errors"))
          Log.Debug("[TscWatchService] Compilation successful");
        else
          Log.Debug("[TscWatchService] tsc --watch ready");
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
