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
  [InitializeOnLoad]
  static class TscFileWatcher
  {
    const double PollIntervalSec = 1.0;
    static double s_LastPollTime;
    static readonly Dictionary<string, long> s_FileTimestamps = new();

    static TscFileWatcher()
    {
      var sourceRoot = Path.Combine(Application.streamingAssetsPath, "unity.js");
      TscCompiler.Instance = new TscCompiler(sourceRoot);

      TscCompiler.Instance.RecompileIfStale();
      RefreshSnapshot();

      EditorApplication.update += Poll;
    }

    static void Poll()
    {
      var compiler = TscCompiler.Instance;
      if (compiler == null || compiler.State == TscState.Dead)
        return;

      var now = EditorApplication.timeSinceStartup;
      if (now - s_LastPollTime < PollIntervalSec)
        return;
      s_LastPollTime = now;

      if (HasChanges(compiler.SourceRoot))
      {
        compiler.Recompile();
        RefreshSnapshot();

        if (EditorApplication.isPlaying)
          TriggerHotReload();
      }
    }

    static bool HasChanges(string sourceRoot)
    {
      foreach (var subdir in new[] { "systems", "components", "types" })
      {
        var tsDir = Path.Combine(sourceRoot, subdir);
        if (!Directory.Exists(tsDir))
          continue;

        foreach (var ts in Directory.GetFiles(tsDir, "*.ts", SearchOption.AllDirectories))
        {
          var ticks = File.GetLastWriteTimeUtc(ts).Ticks;
          if (s_FileTimestamps.TryGetValue(ts, out var cached))
          {
            if (ticks != cached)
              return true;
          }
          else
          {
            return true;
          }
        }
      }

      // Check for deleted files
      foreach (var path in s_FileTimestamps.Keys)
        if (!File.Exists(path))
          return true;

      return false;
    }

    static void RefreshSnapshot()
    {
      s_FileTimestamps.Clear();
      var compiler = TscCompiler.Instance;
      if (compiler == null)
        return;

      foreach (var subdir in new[] { "systems", "components", "types" })
      {
        var tsDir = Path.Combine(compiler.SourceRoot, subdir);
        if (!Directory.Exists(tsDir))
          continue;

        foreach (var ts in Directory.GetFiles(tsDir, "*.ts", SearchOption.AllDirectories))
          s_FileTimestamps[ts] = File.GetLastWriteTimeUtc(ts).Ticks;
      }
    }

    static void TriggerHotReload()
    {
      var vm = JsRuntimeManager.Instance;
      if (vm == null || !vm.IsValid)
        return;

      var compiler = TscCompiler.Instance;
      if (compiler == null || !Directory.Exists(compiler.OutDir))
        return;

      ReloadAllCompiledScripts(vm, compiler);
    }

    internal static void ReloadAllCompiledScripts(JsRuntimeManager vm, TscCompiler compiler)
    {
      var outDir = compiler.OutDir;

      // Reload systems
      var systemsDir = Path.Combine(outDir, "systems");
      if (Directory.Exists(systemsDir))
      {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world != null)
        {
          var runnerHandle = world.Unmanaged.GetExistingUnmanagedSystem<JsSystemRunner>();
          foreach (var jsFile in Directory.GetFiles(systemsDir, "*.js", SearchOption.AllDirectories))
          {
            var systemName = Path.GetFileNameWithoutExtension(jsFile);
            if (!vm.HasScript("system:" + systemName))
              continue;
            if (runnerHandle != SystemHandle.Null)
            {
              ref var sysState = ref world.Unmanaged.ResolveSystemStateRef(runnerHandle);
              JsSystemRunner.ReloadSystem(ref sysState, systemName);
            }
          }
        }
      }

      // Reload components
      var componentsDir = Path.Combine(outDir, "components");
      if (Directory.Exists(componentsDir))
      {
        foreach (
          var jsFile in Directory.GetFiles(componentsDir, "*.js", SearchOption.AllDirectories)
        )
        {
          var fileName = Path.GetFileNameWithoutExtension(jsFile);
          var scriptName = $"components/{fileName}";
          if (!vm.HasScript(scriptName))
            continue;
          var source = File.ReadAllText(jsFile);
          if (vm.ReloadScript(scriptName, source, jsFile))
          {
            vm.ComponentReload(scriptName);
            Log.Debug("[TscFileWatcher] Reloaded component: {0}", scriptName);
          }
        }
      }
    }
  }
}
