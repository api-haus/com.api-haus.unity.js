namespace UnityJS.Runtime
{
  using System.IO;
  using UnityEngine;

  /// <summary>
  /// Scans for mod directories next to the executable and registers them as script sources.
  /// </summary>
  public static class JsModDiscovery
  {
    public static void ScanModsDirectory()
    {
      var modsDir = Path.Combine(Application.dataPath, "..", "mods");
      if (!Directory.Exists(modsDir))
        return;

      foreach (var dir in Directory.GetDirectories(modsDir))
      {
        var name = Path.GetFileName(dir);
        JsScriptSourceRegistry.Register(new FileSystemScriptSource($"mod:{name}", dir, 0));
      }
    }
  }
}
