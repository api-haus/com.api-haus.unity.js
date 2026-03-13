namespace UnityJS.Runtime
{
  using System;
  using System.Collections.Generic;
  using System.Text;

  /// <summary>
  /// In-memory script source. Can be populated from TextAssets, AssetBundles, or programmatically.
  /// </summary>
  public class BundleScriptSource : IJsScriptSource
  {
    readonly string m_SourceId;
    readonly int m_Priority;
    readonly Dictionary<string, string> m_Scripts = new();

    public string SourceId => m_SourceId;
    public int Priority => m_Priority;

    public BundleScriptSource(string sourceId, int priority)
    {
      m_SourceId = sourceId;
      m_Priority = priority;
    }

    /// <summary>
    /// Register a script. Name should be the logical path without .js extension
    /// (e.g. "systems/my_system" or "utils/helper").
    /// </summary>
    public void Add(string name, string source)
    {
      m_Scripts[name] = source;
    }

    public void Remove(string name)
    {
      m_Scripts.Remove(name);
    }

    public IReadOnlyList<string> DiscoverSystems()
    {
      var systems = new List<string>();
      foreach (var key in m_Scripts.Keys)
      {
        if (key.StartsWith("systems/", StringComparison.Ordinal))
          systems.Add(key.Substring("systems/".Length));
      }
      return systems;
    }

    public bool TryReadScript(string scriptName, out string source, out string resolvedId)
    {
      source = null;
      resolvedId = null;

      if (string.IsNullOrEmpty(scriptName))
        return false;

      // Try direct lookup
      if (m_Scripts.TryGetValue(scriptName, out source))
      {
        resolvedId = $"bundle://{m_SourceId}/{scriptName}.js";
        return true;
      }

      // Try with systems/ prefix
      var systemsKey = "systems/" + scriptName;
      if (m_Scripts.TryGetValue(systemsKey, out source))
      {
        resolvedId = $"bundle://{m_SourceId}/{systemsKey}.js";
        return true;
      }

      return false;
    }

    public bool HasModule(string relativePathWithExtension)
    {
      if (string.IsNullOrEmpty(relativePathWithExtension))
        return false;
      var key = StripJsExtension(relativePathWithExtension);
      return m_Scripts.ContainsKey(key);
    }

    public bool TryReadModule(string relativePathWithExtension, out byte[] data)
    {
      data = null;
      if (string.IsNullOrEmpty(relativePathWithExtension))
        return false;

      var key = StripJsExtension(relativePathWithExtension);
      if (!m_Scripts.TryGetValue(key, out var source))
        return false;

      data = Encoding.UTF8.GetBytes(source);
      return true;
    }

    static string StripJsExtension(string path)
    {
      if (path.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
        return path.Substring(0, path.Length - 3);
      return path;
    }
  }
}
