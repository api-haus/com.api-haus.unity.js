namespace UnityJS.Runtime
{
  using System.Collections.Generic;

  /// <summary>
  /// A source of JS scripts — filesystem directory, in-memory bundle, mod folder, etc.
  /// </summary>
  public interface IJsScriptSource
  {
    /// <summary>Unique identifier for this source (e.g. "default", "mod:mymod").</summary>
    string SourceId { get; }

    /// <summary>Lower priority wins (mods at 0 override built-in at 100).</summary>
    int Priority { get; }

    /// <summary>Return names of available system scripts (no extension, no path prefix).</summary>
    IReadOnlyList<string> DiscoverSystems();

    /// <summary>Return names of available standalone scripts (no extension, no path prefix).</summary>
    IReadOnlyList<string> DiscoverScripts();

    /// <summary>
    /// Try to read a script by name (no extension).
    /// Tries both root-level and systems/ subfolder.
    /// </summary>
    /// <param name="scriptName">Script name, may include path segments (e.g. "systems/foo").</param>
    /// <param name="source">The source code if found.</param>
    /// <param name="resolvedId">Filesystem path or virtual path (module cache key).</param>
    bool TryReadScript(string scriptName, out string source, out string resolvedId);

    /// <summary>Check if a module exists by relative path WITH .js extension.</summary>
    bool HasModule(string relativePathWithExtension);

    /// <summary>Read a module's bytes by relative path WITH .js extension.</summary>
    bool TryReadModule(string relativePathWithExtension, out byte[] data);
  }
}
