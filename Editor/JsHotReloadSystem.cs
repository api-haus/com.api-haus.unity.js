namespace UnityJS.Editor
{
  using System;
  using System.Collections.Concurrent;
  using System.IO;
  using Runtime;
  using Unity.Entities;
  using Unity.Logging;
  using UnityEngine;

  [UpdateInGroup(typeof(InitializationSystemGroup))]
  public partial class JsHotReloadSystem : SystemBase
  {
    FileSystemWatcher m_ScriptsWatcher;
    FileSystemWatcher m_DataWatcher;
    readonly ConcurrentQueue<string> m_ReloadQueue = new();
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

      if (!Directory.Exists(jsPath))
      {
        Directory.CreateDirectory(jsPath);
        Directory.CreateDirectory(scriptsPath);
        Directory.CreateDirectory(dataPath);
      }

      if (Directory.Exists(scriptsPath))
        m_ScriptsWatcher = CreateWatcher(scriptsPath, m_ReloadQueue);

      if (Directory.Exists(dataPath))
        m_DataWatcher = CreateWatcher(dataPath, m_ReloadQueue);

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
        try
        {
          var fileName = Path.GetFileNameWithoutExtension(filePath);
          var directory = Path.GetDirectoryName(filePath);

          if (directory != null && directory.Contains("data"))
            fileName = $"data.{fileName}";

          if (!vm.HasScript(fileName))
            continue;

          var source = File.ReadAllText(filePath);
          if (vm.ReloadScript(fileName, source, filePath))
            Log.Debug($"[JsHotReload] Reloaded: {fileName}");
        }
        catch (Exception ex)
        {
          Log.Error($"[JsHotReload] Error reloading {filePath}: {ex.Message}");
        }
    }

    protected override void OnDestroy()
    {
      DisposeWatcher(ref m_ScriptsWatcher);
      DisposeWatcher(ref m_DataWatcher);
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
