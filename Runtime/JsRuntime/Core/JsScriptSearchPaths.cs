using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("UnityJS.Runtime.Tests")]
[assembly: InternalsVisibleTo("UnityJS.Entities.PlayModeTests")]

namespace UnityJS.Runtime
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using UnityEngine;

  /// <summary>
  /// Static registry for JS script search paths.
  /// Delegates to JsScriptSourceRegistry internally. Public API unchanged.
  /// </summary>
  public static class JsScriptSearchPaths
  {
    static readonly object s_searchPathLock = new();
    static bool s_initialized;
    static string s_defaultScriptsPath;

    // Track which search paths we've registered as FileSystemScriptSources
    static readonly Dictionary<string, string> s_pathToSourceId = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSession()
    {
      s_initialized = false;
      s_defaultScriptsPath = null;
      s_pathToSourceId.Clear();
    }

    public static string DefaultScriptsPath
    {
      get
      {
        if (s_defaultScriptsPath == null)
          s_defaultScriptsPath = Path.Combine(Application.streamingAssetsPath, "unity.js");
        return s_defaultScriptsPath;
      }
    }

    public static void Initialize()
    {
      lock (s_searchPathLock)
      {
        if (s_initialized)
          return;

        RegisterPathAsSource(DefaultScriptsPath, "default", 100);

#if UNITY_EDITOR
        // Register Library/TscBuild for baked script names (full path like "Assets/StreamingAssets/unity.js/components/slime_wander")
        var tscBuildRoot = Path.Combine(Application.dataPath, "..", "Library", "TscBuild");
        RegisterPathAsSource(tscBuildRoot, "tsc-build", 50);
        // Also register the mirrored StreamingAssets subtree for system discovery (relative names like "systems/character_input")
        var tscBuildScripts = Path.Combine(tscBuildRoot, "Assets", "StreamingAssets", "unity.js");
        RegisterPathAsSource(tscBuildScripts, "tsc-build-scripts", 45);
#endif

        s_initialized = true;
      }
    }

    public static void AddSearchPath(string absolutePath, int priority = 0)
    {
      lock (s_searchPathLock)
      {
        if (!s_initialized)
          Initialize();
        if (string.IsNullOrEmpty(absolutePath))
          return;

        // Remove existing registration for this path
        RemovePathSource(absolutePath);

        var sourceId = "searchpath:" + absolutePath;
        RegisterPathAsSource(absolutePath, sourceId, priority);
      }
    }

    public static void RemoveSearchPath(string absolutePath)
    {
      lock (s_searchPathLock)
      {
        if (absolutePath == DefaultScriptsPath)
          return;
        RemovePathSource(absolutePath);
      }
    }

    public static void ClearSearchPaths()
    {
      lock (s_searchPathLock)
      {
        // Remove all non-default sources registered through this API
        var toRemove = new List<string>(s_pathToSourceId.Keys);
        foreach (var path in toRemove)
        {
          if (path == DefaultScriptsPath)
            continue;
          RemovePathSource(path);
        }
      }
    }

    public static IReadOnlyList<string> GetSearchPaths()
    {
      lock (s_searchPathLock)
      {
        if (!s_initialized)
          Initialize();

        var paths = new List<string>();
        var sources = JsScriptSourceRegistry.GetSources();
        foreach (var source in sources)
          if (source is FileSystemScriptSource fs)
            paths.Add(fs.BasePath);
        return paths;
      }
    }

    public static bool TryFindScript(
      string relativePath,
      out string foundPath,
      out string searchedBasePath
    )
    {
      foundPath = string.Empty;
      searchedBasePath = string.Empty;

      if (string.IsNullOrEmpty(relativePath))
        return false;

      lock (s_searchPathLock)
      {
        if (!s_initialized)
          Initialize();
      }

      // Delegate to registry — search through all filesystem sources
      var sources = JsScriptSourceRegistry.GetSources();
      foreach (var source in sources)
      {
        if (source is not FileSystemScriptSource fs)
          continue;

        var fullPath = Path.Combine(fs.BasePath, relativePath);
        if (File.Exists(fullPath))
        {
          foundPath = fullPath;
          searchedBasePath = fs.BasePath;
          return true;
        }
      }

      return false;
    }

    public static bool ScriptExists(string relativePath)
    {
      return TryFindScript(relativePath, out _, out _);
    }

    public static string GetScriptPath(string relativePath)
    {
      if (TryFindScript(relativePath, out var foundPath, out _))
        return foundPath;

      return Path.Combine(DefaultScriptsPath, relativePath);
    }

    /// <summary>
    /// Removes all registered search paths and returns them for later restoration.
    /// Used by fixture tests to isolate script discovery.
    /// </summary>
    public static List<(string path, string sourceId, int priority)> RemoveAllSources()
    {
      lock (s_searchPathLock)
      {
        var saved = new List<(string, string, int)>();
        var sources = JsScriptSourceRegistry.GetSources();
        foreach (var source in sources)
          if (source is FileSystemScriptSource fs)
            saved.Add((fs.BasePath, fs.SourceId, fs.Priority));

        // Unregister directly by sourceId to avoid path normalization issues
        foreach (var (_, sourceId, _) in saved)
          JsScriptSourceRegistry.Unregister(sourceId);
        s_pathToSourceId.Clear();

        return saved;
      }
    }

    /// <summary>
    /// Restores previously saved search paths. Used after fixture test isolation.
    /// </summary>
    public static void RestoreSources(List<(string path, string sourceId, int priority)> saved)
    {
      lock (s_searchPathLock)
      {
        foreach (var (path, sourceId, priority) in saved)
          RegisterPathAsSource(path, sourceId, priority);
      }
    }

    /// <summary>
    /// Reset state for testing. Not thread-safe — call only in test SetUp.
    /// </summary>
    internal static void Reset()
    {
      lock (s_searchPathLock)
      {
        s_pathToSourceId.Clear();
        JsScriptSourceRegistry.Reset();
        s_initialized = false;
        s_defaultScriptsPath = null;
      }
    }

    static void RegisterPathAsSource(string absolutePath, string sourceId, int priority)
    {
      var source = new FileSystemScriptSource(sourceId, absolutePath, priority);
      JsScriptSourceRegistry.Register(source);
      s_pathToSourceId[absolutePath] = sourceId;
    }

    static void RemovePathSource(string absolutePath)
    {
      if (s_pathToSourceId.TryGetValue(absolutePath, out var sourceId))
      {
        JsScriptSourceRegistry.Unregister(sourceId);
        s_pathToSourceId.Remove(absolutePath);
      }
    }
  }
}
