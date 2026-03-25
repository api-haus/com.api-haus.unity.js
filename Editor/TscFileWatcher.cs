using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using Unity.Entities;
using Unity.Logging;
using UnityEngine;
using UnityJS.Entities.Systems;
using UnityJS.Runtime;

namespace UnityJS.Editor
{
  /// <summary>
  /// Watches .ts files for changes in the editor. During play mode, triggers hot reload
  /// by reading and transpiling changed files, then reloading them in the VM.
  /// </summary>
  [InitializeOnLoad]
  static class TsFileWatcher
  {
    const double PollIntervalSec = 1.0;
    static double s_LastPollTime;
    static readonly Dictionary<string, long> s_FileTimestamps = new();
    static string s_SourceRoot;

    static TsFileWatcher()
    {
      s_SourceRoot = Path.Combine(Application.streamingAssetsPath, "unity.js");
      RefreshSnapshot();
      EditorApplication.update += Poll;
    }

    static void Poll()
    {
      var now = EditorApplication.timeSinceStartup;
      if (now - s_LastPollTime < PollIntervalSec)
        return;
      s_LastPollTime = now;

      var changedFiles = GetChangedFiles();
      if (changedFiles.Count == 0)
        return;

      RefreshSnapshot();

      if (EditorApplication.isPlaying)
        ReloadChangedScripts(changedFiles);
    }

    static List<string> GetChangedFiles()
    {
      var changed = new List<string>();

      foreach (var subdir in new[] { "systems", "components", "scripts", "types" })
      {
        var tsDir = Path.Combine(s_SourceRoot, subdir);
        if (!Directory.Exists(tsDir))
          continue;

        foreach (var ts in Directory.GetFiles(tsDir, "*.ts", SearchOption.AllDirectories))
        {
          var ticks = File.GetLastWriteTimeUtc(ts).Ticks;
          if (s_FileTimestamps.TryGetValue(ts, out var cached))
          {
            if (ticks != cached)
              changed.Add(ts);
          }
          else
          {
            changed.Add(ts);
          }
        }
      }

      return changed;
    }

    static void RefreshSnapshot()
    {
      s_FileTimestamps.Clear();

      foreach (var subdir in new[] { "systems", "components", "scripts", "types" })
      {
        var tsDir = Path.Combine(s_SourceRoot, subdir);
        if (!Directory.Exists(tsDir))
          continue;

        foreach (var ts in Directory.GetFiles(tsDir, "*.ts", SearchOption.AllDirectories))
          s_FileTimestamps[ts] = File.GetLastWriteTimeUtc(ts).Ticks;
      }
    }

    static void ReloadChangedScripts(List<string> changedFiles)
    {
      var vm = JsRuntimeManager.Instance;
      if (vm == null || !vm.IsValid)
        return;

      foreach (var filePath in changedFiles)
      {
        try
        {
          var fileName = Path.GetFileNameWithoutExtension(filePath);
          var parentDir = Path.GetFileName(Path.GetDirectoryName(filePath));

          // Determine script name based on directory
          string scriptName;
          if (parentDir == "systems")
          {
            scriptName = "system:" + fileName;
            if (!vm.HasScript(scriptName))
              continue;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null)
            {
              var runnerHandle = world.Unmanaged.GetExistingUnmanagedSystem<JsSystemRunner>();
              if (runnerHandle != SystemHandle.Null)
              {
                ref var sysState = ref world.Unmanaged.ResolveSystemStateRef(runnerHandle);
                JsSystemDiscovery.ReloadSystem(ref sysState, fileName);
              }
            }
          }
          else if (parentDir == "components")
          {
            scriptName = $"components/{fileName}";
            if (!vm.HasScript(scriptName))
              continue;

            var tsSource = File.ReadAllText(filePath);
            var jsSource = JsTranspiler.Transpile(vm.Context, tsSource);
            if (jsSource == null)
            {
              Log.Error("[TsFileWatcher] Transpilation failed for {0}", (Unity.Collections.FixedString128Bytes)scriptName);
              continue;
            }

            if (vm.ReloadScript(scriptName, jsSource, filePath))
            {
              vm.ComponentReload(scriptName);
              Log.Debug("[TsFileWatcher] Reloaded component: {0}", (Unity.Collections.FixedString128Bytes)scriptName);
            }
          }
          else
          {
            // scripts/ or other directories
            scriptName = fileName;
            if (!vm.HasScript(scriptName))
              continue;

            var tsSource = File.ReadAllText(filePath);
            var jsSource = JsTranspiler.Transpile(vm.Context, tsSource);
            if (jsSource == null)
            {
              Log.Error("[TsFileWatcher] Transpilation failed for {0}", (Unity.Collections.FixedString128Bytes)scriptName);
              continue;
            }

            if (vm.ReloadScript(scriptName, jsSource, filePath))
              Log.Debug("[TsFileWatcher] Reloaded script: {0}", (Unity.Collections.FixedString128Bytes)scriptName);
          }
        }
        catch (Exception ex)
        {
          Debug.LogError($"[TsFileWatcher] Error reloading {filePath}: {ex.Message}");
        }
      }
    }
  }
}
