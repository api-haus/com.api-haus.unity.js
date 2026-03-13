#if UNITY_EDITOR
namespace UnityJS.Editor
{
  using System;
  using System.Collections.Concurrent;
  using System.IO;
  using Unity.Entities;
  using Unity.Logging;
  using UnityEngine;
  using UnityJS.Entities.Systems;
  using UnityJS.Runtime;

  [UpdateInGroup(typeof(InitializationSystemGroup))]
  public partial class JsHotReloadSystem : SystemBase
  {
    FileSystemWatcher m_ScriptsWatcher;
    FileSystemWatcher m_DataWatcher;
    FileSystemWatcher m_SystemsWatcher;
    readonly ConcurrentQueue<string> m_ReloadQueue = new();
    readonly ConcurrentQueue<string> m_SystemReloadQueue = new();
    bool m_Initialized;

    protected override void OnCreate()
    {
      m_Initialized = false;
    }

    protected override void OnStartRunning()
    {
      if (m_Initialized)
        return;

      var jsPath = Path.Combine(Application.streamingAssetsPath, "unity.js");
      var scriptsPath = Path.Combine(jsPath, "scripts");
      var dataPath = Path.Combine(jsPath, "data");
      var systemsPath = Path.Combine(Application.dataPath, "..", "Library", "TscBuild", "systems");

      if (!Directory.Exists(jsPath))
      {
        Directory.CreateDirectory(jsPath);
        Directory.CreateDirectory(scriptsPath);
        Directory.CreateDirectory(dataPath);
      }

      if (!Directory.Exists(systemsPath))
        Directory.CreateDirectory(systemsPath);

      if (Directory.Exists(scriptsPath))
      {
        m_ScriptsWatcher = CreateWatcher(scriptsPath, m_ReloadQueue);
      }

      if (Directory.Exists(dataPath))
      {
        m_DataWatcher = CreateWatcher(dataPath, m_ReloadQueue);
      }

      if (Directory.Exists(systemsPath))
      {
        m_SystemsWatcher = CreateWatcher(systemsPath, m_SystemReloadQueue);
      }

      m_Initialized = true;
    }

    FileSystemWatcher CreateWatcher(string path, ConcurrentQueue<string> queue)
    {
      var watcher = new FileSystemWatcher(path)
      {
        Filter = "*.js",
        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
        EnableRaisingEvents = true,
        IncludeSubdirectories = true,
      };

      watcher.Changed += (_, e) =>
      {
        if (e.ChangeType is WatcherChangeTypes.Changed or WatcherChangeTypes.Created)
          queue.Enqueue(e.FullPath);
      };
      watcher.Created += (_, e) =>
      {
        if (e.ChangeType is WatcherChangeTypes.Changed or WatcherChangeTypes.Created)
          queue.Enqueue(e.FullPath);
      };

      return watcher;
    }

    protected override void OnUpdate()
    {
      var vm = JsRuntimeManager.Instance;
      if (vm == null || !vm.IsValid)
        return;

      while (m_ReloadQueue.TryDequeue(out var filePath))
      {
        try
        {
          var fileName = Path.GetFileNameWithoutExtension(filePath);
          var directory = Path.GetDirectoryName(filePath);

          if (directory != null && directory.Contains("data"))
          {
            fileName = $"data.{fileName}";
          }

          var source = File.ReadAllText(filePath);
          if (vm.ReloadScript(fileName, source, filePath))
          {
            Log.Debug($"[JsHotReload] Reloaded: {fileName}");
          }
        }
        catch (Exception ex)
        {
          Log.Error($"[JsHotReload] Error reloading {filePath}: {ex.Message}");
        }
      }

      // Reload system-level JS scripts
      if (m_SystemReloadQueue.Count > 0)
      {
        var runner = World.GetExistingSystemManaged<JsSystemRunner>();
        if (runner != null)
        {
          while (m_SystemReloadQueue.TryDequeue(out var filePath))
          {
            try
            {
              var systemName = Path.GetFileNameWithoutExtension(filePath);
              runner.ReloadSystem(systemName);
            }
            catch (Exception ex)
            {
              Log.Error($"[JsHotReload] Error reloading system {filePath}: {ex.Message}");
            }
          }
        }
      }
    }

    protected override void OnDestroy()
    {
      DisposeWatcher(ref m_ScriptsWatcher);
      DisposeWatcher(ref m_DataWatcher);
      DisposeWatcher(ref m_SystemsWatcher);
    }

    static void DisposeWatcher(ref FileSystemWatcher watcher)
    {
      if (watcher == null)
        return;
      watcher.EnableRaisingEvents = false;
      watcher.Dispose();
      watcher = null;
    }
  }
}
#endif
