namespace UnityJS.Entities.Systems
{
  using System;
  using System.Collections.Concurrent;
  using System.IO;
  using Runtime;
  using Unity.Collections;
  using Unity.Entities;
  using UnityEngine;

  [ExecuteAlways]
  [AddComponentMenu("")]
  public class TsFileWatch : MonoBehaviour
  {
    static TsFileWatch s_Instance;

    public static TsFileWatch Instance
    {
      get
      {
        if (s_Instance == null)
          Init();
        return s_Instance;
      }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    static void Init()
    {
      if (s_Instance != null)
        return;
      var go = new GameObject("TsFileWatch")
      {
        hideFlags =
          HideFlags.DontSave
          | HideFlags.NotEditable
          | HideFlags.HideInInspector
          | HideFlags.HideInHierarchy,
      };
      if (Application.isPlaying)
        DontDestroyOnLoad(go);
      go.AddComponent<TsFileWatch>();
    }

    static readonly string[] k_WatchDirs = { "systems", "components", "scripts", "data", "types" };

    FileSystemWatcher[] m_Watchers;
    readonly ConcurrentQueue<string> m_Queue = new();

    void OnEnable()
    {
      if (s_Instance == null)
        s_Instance = this;
      if (s_Instance != this)
      {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.update += DelayedDestroy;
#endif
        return;
      }

      var jsPath = Path.Combine(Application.streamingAssetsPath, "unity.js");
      m_Watchers = new FileSystemWatcher[k_WatchDirs.Length];

      for (var i = 0; i < k_WatchDirs.Length; i++)
      {
        var dir = Path.Combine(jsPath, k_WatchDirs[i]);
        if (Directory.Exists(dir))
          m_Watchers[i] = CreateWatcher(dir);
      }
    }

    void OnDisable()
    {
      if (s_Instance != this)
        return;

      if (m_Watchers != null)
      {
        for (var i = 0; i < m_Watchers.Length; i++)
          DisposeWatcher(ref m_Watchers[i]);
        m_Watchers = null;
      }

      s_Instance = null;
    }

#if UNITY_EDITOR
    void DelayedDestroy()
    {
      UnityEditor.EditorApplication.update -= DelayedDestroy;
      if (gameObject)
        DestroyImmediate(gameObject);
    }
#endif

    void Update() => Flush();

    FileSystemWatcher CreateWatcher(string path)
    {
      var watcher = new FileSystemWatcher(path)
      {
        Filter = "*.ts",
        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
        EnableRaisingEvents = true,
        IncludeSubdirectories = true,
      };
      watcher.Changed += OnFileEvent;
      watcher.Created += OnFileEvent;
      return watcher;
    }

    void OnFileEvent(object sender, FileSystemEventArgs e)
    {
      if (e.ChangeType is WatcherChangeTypes.Changed or WatcherChangeTypes.Created)
        m_Queue.Enqueue(e.FullPath);
    }

    void Flush()
    {
      while (m_Queue.TryDequeue(out var filePath))
      {
        try
        {
          string source;
          try { source = File.ReadAllText(filePath); }
          catch (IOException) { continue; } // file still being written

          var jsSource = JsTranspiler.Transpile(source, filePath);
          if (jsSource == null)
            continue; // error already tracked + logged by JsTranspiler

          var vm = JsRuntimeManager.Instance;
          if (vm == null || !vm.IsValid)
            continue; // edit-mode: validation-only — error cleared, status bar updates via event

          ReloadIntoVm(vm, filePath, jsSource);
        }
        catch (Exception ex)
        {
          Debug.LogError($"[TsFileWatch] Error processing {filePath}: {ex.Message}");
        }
      }
    }

    static void ReloadIntoVm(JsRuntimeManager vm, string filePath, string jsSource)
    {
      var fileName = Path.GetFileNameWithoutExtension(filePath);
      var parentDir = Path.GetFileName(Path.GetDirectoryName(filePath));

      string scriptName;

      if (parentDir == "systems")
      {
        scriptName = "system:" + fileName;
        if (!vm.HasScript(scriptName))
          return;

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
          return;

        if (vm.ReloadScript(scriptName, jsSource, filePath))
          vm.ComponentReload(scriptName);
      }
      else if (parentDir == "data")
      {
        scriptName = $"data.{fileName}";
        if (!vm.HasScript(scriptName))
          return;

        vm.ReloadScript(scriptName, jsSource, filePath);
      }
      else
      {
        scriptName = fileName;
        if (!vm.HasScript(scriptName))
          return;

        vm.ReloadScript(scriptName, jsSource, filePath);
      }
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
