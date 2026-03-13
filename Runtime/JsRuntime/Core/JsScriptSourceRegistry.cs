namespace UnityJS.Runtime
{
  using System;
  using System.Collections.Generic;

  /// <summary>
  /// Central registry for JS script sources. Thread-safe, static.
  /// Sources are ordered by priority (lower = higher precedence).
  /// </summary>
  public static class JsScriptSourceRegistry
  {
    static readonly List<IJsScriptSource> s_sources = new();
    static readonly object s_lock = new();

    public static void Register(IJsScriptSource source)
    {
      if (source == null)
        return;

      lock (s_lock)
      {
        // Replace existing source with same ID
        for (var i = s_sources.Count - 1; i >= 0; i--)
        {
          if (s_sources[i].SourceId == source.SourceId)
          {
            s_sources.RemoveAt(i);
            break;
          }
        }

        // Insert sorted by priority (ascending — lower priority value = higher precedence)
        var inserted = false;
        for (var i = 0; i < s_sources.Count; i++)
        {
          if (source.Priority < s_sources[i].Priority)
          {
            s_sources.Insert(i, source);
            inserted = true;
            break;
          }
        }

        if (!inserted)
          s_sources.Add(source);
      }
    }

    public static void Unregister(string sourceId)
    {
      if (string.IsNullOrEmpty(sourceId))
        return;

      lock (s_lock)
      {
        for (var i = s_sources.Count - 1; i >= 0; i--)
        {
          if (s_sources[i].SourceId == sourceId)
          {
            s_sources.RemoveAt(i);
            return;
          }
        }
      }
    }

    public static IReadOnlyList<IJsScriptSource> GetSources()
    {
      lock (s_lock)
      {
        return s_sources.ToArray();
      }
    }

    /// <summary>
    /// Discovers all system scripts across all sources.
    /// First source to provide a name wins (lower priority overrides higher).
    /// </summary>
    public static List<(string name, IJsScriptSource source)> DiscoverAllSystems()
    {
      var result = new List<(string, IJsScriptSource)>();
      var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

      lock (s_lock)
      {
        foreach (var source in s_sources)
        {
          var systems = source.DiscoverSystems();
          foreach (var name in systems)
          {
            if (seen.Add(name))
              result.Add((name, source));
          }
        }
      }

      return result;
    }

    /// <summary>
    /// Tries sources in priority order to read a script by name.
    /// </summary>
    public static bool TryReadScript(string scriptName, out string source, out string resolvedId)
    {
      source = null;
      resolvedId = null;

      lock (s_lock)
      {
        foreach (var src in s_sources)
        {
          if (src.TryReadScript(scriptName, out source, out resolvedId))
            return true;
        }
      }

      return false;
    }

    /// <summary>
    /// Finds a module across all sources by relative path (with .js extension).
    /// Returns the resolved path (absolute for filesystem, bundle:// for bundles).
    /// </summary>
    public static bool TryFindModule(string relPathWithExt, out string resolvedPath)
    {
      resolvedPath = null;

      lock (s_lock)
      {
        foreach (var source in s_sources)
        {
          if (!source.HasModule(relPathWithExt))
            continue;

          if (source is FileSystemScriptSource fs)
            resolvedPath = fs.GetAbsoluteModulePath(relPathWithExt);
          else
            resolvedPath = $"bundle://{source.SourceId}/{relPathWithExt}";
          return true;
        }
      }

      return false;
    }

    /// <summary>
    /// Reads module bytes from a bundle:// resolved path.
    /// </summary>
    public static bool TryReadModuleBytes(string resolvedPath, out byte[] data)
    {
      data = null;

      if (
        string.IsNullOrEmpty(resolvedPath)
        || !resolvedPath.StartsWith("bundle://", StringComparison.Ordinal)
      )
        return false;

      var rest = resolvedPath.Substring("bundle://".Length);
      var slashIdx = rest.IndexOf('/');
      if (slashIdx < 0)
        return false;

      var sourceId = rest.Substring(0, slashIdx);
      var relPath = rest.Substring(slashIdx + 1);

      lock (s_lock)
      {
        foreach (var source in s_sources)
        {
          if (source.SourceId != sourceId)
            continue;
          return source.TryReadModule(relPath, out data);
        }
      }

      return false;
    }

    /// <summary>
    /// Reset all sources. For testing only.
    /// </summary>
    public static void Reset()
    {
      lock (s_lock)
      {
        s_sources.Clear();
      }
    }
  }
}
