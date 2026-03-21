namespace UnityJS.Runtime
{
  using System;
  using System.Collections.Generic;
  using System.IO;

  /// <summary>
  /// Script source backed by a filesystem directory.
  /// </summary>
  public class FileSystemScriptSource : IJsScriptSource
  {
    readonly string m_SourceId;
    readonly string m_BasePath;
    readonly int m_Priority;

    public string SourceId => m_SourceId;
    public int Priority => m_Priority;
    public string BasePath => m_BasePath;

    public FileSystemScriptSource(string sourceId, string basePath, int priority)
    {
      m_SourceId = sourceId;
      m_BasePath = basePath;
      m_Priority = priority;
    }

    public IReadOnlyList<string> DiscoverSystems()
    {
      var systemsDir = Path.Combine(m_BasePath, "systems");
      if (!Directory.Exists(systemsDir))
        return Array.Empty<string>();

      var files = Directory.GetFiles(systemsDir, "*.js", SearchOption.AllDirectories);
      var names = new List<string>(files.Length);
      foreach (var file in files)
        names.Add(Path.GetFileNameWithoutExtension(file));
      return names;
    }

    public bool TryReadScript(string scriptName, out string source, out string resolvedId)
    {
      source = null;
      resolvedId = null;

      if (string.IsNullOrEmpty(scriptName))
        return false;

      // Try direct path: {basePath}/{scriptName}.js
      var directPath = Path.Combine(m_BasePath, scriptName + ".js");
      if (File.Exists(directPath))
      {
        source = File.ReadAllText(directPath);
        resolvedId = Path.GetFullPath(directPath);
        return true;
      }

      // Try under systems/: {basePath}/systems/{scriptName}.js
      var systemsPath = Path.Combine(m_BasePath, "systems", scriptName + ".js");
      if (File.Exists(systemsPath))
      {
        source = File.ReadAllText(systemsPath);
        resolvedId = Path.GetFullPath(systemsPath);
        return true;
      }

      return false;
    }

    public bool HasModule(string relativePathWithExtension)
    {
      if (string.IsNullOrEmpty(relativePathWithExtension))
        return false;
      return File.Exists(Path.Combine(m_BasePath, relativePathWithExtension));
    }

    public bool TryReadModule(string relativePathWithExtension, out byte[] data)
    {
      data = null;
      if (string.IsNullOrEmpty(relativePathWithExtension))
        return false;

      var fullPath = Path.Combine(m_BasePath, relativePathWithExtension);
      if (!File.Exists(fullPath))
        return false;

      data = File.ReadAllBytes(fullPath);
      return true;
    }

    /// <summary>
    /// Returns the absolute path for a module relative to this source's base path.
    /// </summary>
    public string GetAbsoluteModulePath(string relativePathWithExtension)
    {
      return Path.GetFullPath(Path.Combine(m_BasePath, relativePathWithExtension));
    }
  }
}
