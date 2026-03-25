namespace UnityJS.Runtime
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using System.Text;

  /// <summary>
  /// Script source backed by a filesystem directory.
  /// Discovers .ts files and transpiles them on read via JsTranspiler.
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

    public IReadOnlyList<string> DiscoverSystems() => DiscoverInFolder("systems");

    public IReadOnlyList<string> DiscoverScripts() => DiscoverInFolder("scripts");

    IReadOnlyList<string> DiscoverInFolder(string folder)
    {
      var dir = Path.Combine(m_BasePath, folder);
      if (!Directory.Exists(dir))
        return Array.Empty<string>();

      var files = Directory.GetFiles(dir, "*.ts", SearchOption.AllDirectories);
      var names = new List<string>(files.Length);
      foreach (var file in files)
        names.Add(Path.GetFileNameWithoutExtension(file));

      // Also discover .js files (pre-compiled or hand-written JS mods)
      var jsFiles = Directory.GetFiles(dir, "*.js", SearchOption.AllDirectories);
      foreach (var file in jsFiles)
      {
        var name = Path.GetFileNameWithoutExtension(file);
        if (!names.Contains(name))
          names.Add(name);
      }

      return names;
    }

    static string ResolvePath(string basePath, string scriptName)
    {
      // Try .ts first, fall back to .js
      var tsPath = Path.Combine(basePath, scriptName + ".ts");
      if (File.Exists(tsPath))
        return tsPath;

      var jsPath = Path.Combine(basePath, scriptName + ".js");
      if (File.Exists(jsPath))
        return jsPath;

      return null;
    }

    static string ReadAndTranspile(string filePath)
    {
      var raw = File.ReadAllText(filePath);
      if (filePath.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
      {
        var ctx = JsRuntimeManager.Instance?.Context ?? default;
        if (ctx.IsNull)
          return raw; // No runtime yet — return raw (shouldn't happen in normal flow)
        return JsTranspiler.Transpile(ctx, raw);
      }
      return raw;
    }

    public bool TryReadScript(string scriptName, out string source, out string resolvedId)
    {
      source = null;
      resolvedId = null;

      if (string.IsNullOrEmpty(scriptName))
        return false;

      // Try direct path: {basePath}/{scriptName}
      var path = ResolvePath(m_BasePath, scriptName);
      if (path != null)
      {
        source = ReadAndTranspile(path);
        if (source == null) return false;
        resolvedId = Path.GetFullPath(path);
        return true;
      }

      // Try under systems/
      path = ResolvePath(Path.Combine(m_BasePath, "systems"), scriptName);
      if (path != null)
      {
        source = ReadAndTranspile(path);
        if (source == null) return false;
        resolvedId = Path.GetFullPath(path);
        return true;
      }

      // Try under scripts/
      path = ResolvePath(Path.Combine(m_BasePath, "scripts"), scriptName);
      if (path != null)
      {
        source = ReadAndTranspile(path);
        if (source == null) return false;
        resolvedId = Path.GetFullPath(path);
        return true;
      }

      return false;
    }

    public bool HasModule(string relativePathWithExtension)
    {
      if (string.IsNullOrEmpty(relativePathWithExtension))
        return false;

      var fullPath = Path.Combine(m_BasePath, relativePathWithExtension);
      if (File.Exists(fullPath))
        return true;

      // Try .ts variant if given .js
      if (relativePathWithExtension.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
      {
        var tsPath = Path.Combine(m_BasePath,
          relativePathWithExtension[..^3] + ".ts");
        return File.Exists(tsPath);
      }

      return false;
    }

    public bool TryReadModule(string relativePathWithExtension, out byte[] data)
    {
      data = null;
      if (string.IsNullOrEmpty(relativePathWithExtension))
        return false;

      var fullPath = Path.Combine(m_BasePath, relativePathWithExtension);

      // Try exact path first
      if (File.Exists(fullPath))
      {
        if (fullPath.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
        {
          var source = ReadAndTranspile(fullPath);
          if (source == null) return false;
          data = Encoding.UTF8.GetBytes(source);
        }
        else
        {
          data = File.ReadAllBytes(fullPath);
        }
        return true;
      }

      // Try .ts variant if given .js
      if (relativePathWithExtension.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
      {
        var tsPath = Path.Combine(m_BasePath,
          relativePathWithExtension[..^3] + ".ts");
        if (File.Exists(tsPath))
        {
          var source = ReadAndTranspile(tsPath);
          if (source == null) return false;
          data = Encoding.UTF8.GetBytes(source);
          return true;
        }
      }

      return false;
    }

    public string GetAbsoluteModulePath(string relativePathWithExtension)
    {
      var fullPath = Path.Combine(m_BasePath, relativePathWithExtension);
      if (File.Exists(fullPath))
        return Path.GetFullPath(fullPath);

      // Try .ts variant if given .js
      if (relativePathWithExtension.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
      {
        var tsPath = Path.Combine(m_BasePath,
          relativePathWithExtension[..^3] + ".ts");
        if (File.Exists(tsPath))
          return Path.GetFullPath(tsPath);
      }

      return Path.GetFullPath(fullPath);
    }
  }
}
