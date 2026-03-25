namespace UnityJS.Entities.Systems
{
  using System;
  using System.Collections.Concurrent;
  using System.IO;
  using Runtime;
  using Unity.Collections;
  using Unity.Entities;
  using Unity.Logging;
  using UnityEngine;

  [UpdateInGroup(typeof(InitializationSystemGroup))]
  public partial class JsHotReloadSystem : SystemBase
  {
    FileSystemWatcher m_ScriptsWatcher;
    FileSystemWatcher m_DataWatcher;
    FileSystemWatcher m_ComponentsWatcher;
    FileSystemWatcher m_SystemsWatcher;
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
      var componentsPath = Path.Combine(jsPath, "components");
      var systemsPath = Path.Combine(jsPath, "systems");

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

      if (Directory.Exists(componentsPath))
        m_ComponentsWatcher = CreateWatcher(componentsPath, m_ReloadQueue);

      if (Directory.Exists(systemsPath))
        m_SystemsWatcher = CreateWatcher(systemsPath, m_ReloadQueue);

      m_Initialized = true;
    }

    FileSystemWatcher CreateWatcher(string path, ConcurrentQueue<string> queue)
    {
      var watcher = new FileSystemWatcher(path)
      {
        Filter = "*.ts",
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

          // Transpile .ts files
          if (filePath.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
          {
            source = JsTranspiler.Transpile(vm.Context, source);
            if (source == null)
            {
              Log.Error("[JsHotReload] Transpilation failed for {0} — fix the error and save again", (FixedString128Bytes)fileName);
              continue;
            }
          }

          if (vm.ReloadScript(fileName, source, filePath))
            Log.Debug("[JsHotReload] Reloaded: {0}", (FixedString128Bytes)fileName);
        }
        catch (Exception ex)
        {
          Log.Error("[JsHotReload] Error reloading {0}: {1}", (FixedString128Bytes)filePath, (FixedString128Bytes)ex.Message);
        }
    }

    protected override void OnDestroy()
    {
      DisposeWatcher(ref m_ScriptsWatcher);
      DisposeWatcher(ref m_DataWatcher);
      DisposeWatcher(ref m_ComponentsWatcher);
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
