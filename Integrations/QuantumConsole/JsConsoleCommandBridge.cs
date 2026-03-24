namespace UnityJS.Integration.QuantumConsole
{
  using System;
  using System.IO;
  using QFSW.QC;
  using UnityEngine;

  /// <summary>
  /// Scans StreamingAssets/unity.js/commands/*.js at startup, loads each as a module,
  /// and registers a QC command that calls its exported run() function.
  /// </summary>
  public static class JsConsoleCommandBridge
  {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void ScanAndRegister()
    {
      var dir = Path.Combine(Application.streamingAssetsPath, "unity.js", "commands");
      if (!Directory.Exists(dir))
        return;

      var files = Directory.GetFiles(dir, "*.js");
      foreach (var file in files)
      {
        var cmdName = Path.GetFileNameWithoutExtension(file);
        RegisterCommandFile(cmdName, file);
      }
    }

    static void RegisterCommandFile(string cmdName, string filePath)
    {
      Func<string, string> handler = arg =>
      {
        Debug.Log($"[JsConsoleCommand] {cmdName}({arg}) — full command harness not yet wired");
        return $"{cmdName}: not yet implemented";
      };

      var cmd = new LambdaCommandData(handler, cmdName, $"JS command: {cmdName}");
      QuantumConsoleProcessor.TryAddCommand(cmd);
    }
  }
}
