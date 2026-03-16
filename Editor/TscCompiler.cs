#if UNITY_EDITOR
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UnityJS.Editor
{
  static class TscCompiler
  {
    internal static string TsconfigPath =>
      Path.GetFullPath(
        Path.Combine(Application.dataPath, "StreamingAssets", "unity.js", "tsconfig.json")
      );

    internal static string OutDir =>
      Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Library", "TscBuild"));

    internal static void OnDomainReload()
    {
      if (ShouldCompile())
        RunTsc();
    }

    [MenuItem("Tools/JS/Compile TypeScript")]
    public static void CompileMenu()
    {
      RunTsc();
    }

    static bool ShouldCompile()
    {
      var systemsOut = Path.Combine(OutDir, "systems");
      if (!Directory.Exists(systemsOut))
        return true;

      var tsDir = Path.GetFullPath(
        Path.Combine(Application.dataPath, "StreamingAssets", "unity.js", "systems")
      );
      if (!Directory.Exists(tsDir))
        return false;

      var tsFiles = Directory.GetFiles(tsDir, "*.ts", SearchOption.AllDirectories);
      foreach (var ts in tsFiles)
      {
        var relative = ts.Substring(tsDir.Length)
          .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var jsName = Path.ChangeExtension(relative, ".js");
        var jsPath = Path.Combine(systemsOut, jsName);
        if (!File.Exists(jsPath))
          return true;
        if (File.GetLastWriteTimeUtc(ts) > File.GetLastWriteTimeUtc(jsPath))
          return true;
      }

      return false;
    }

    internal static bool RunTsc()
    {
      var tsconfigPath = TsconfigPath;
      if (!File.Exists(tsconfigPath))
      {
        Debug.LogWarning($"[TscCompiler] tsconfig.json not found at {tsconfigPath}");
        return false;
      }

      var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
      var npx = "npx";

      var psi = new ProcessStartInfo
      {
        FileName = npx,
        Arguments = $"tsc -p \"{tsconfigPath}\"",
        WorkingDirectory = projectRoot,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
      };

      using var proc = Process.Start(psi);
      if (proc == null)
      {
        Debug.LogError("[TscCompiler] Failed to start tsc process");
        return false;
      }

      var stdout = proc.StandardOutput.ReadToEnd();
      var stderr = proc.StandardError.ReadToEnd();
      proc.WaitForExit(30000);

      if (proc.ExitCode != 0)
      {
        var output = (stdout + "\n" + stderr).Trim();
        var errorCount = output.Split('\n').Count(l => l.Contains("error TS"));
        Debug.LogError($"[TscCompiler] tsc failed with {errorCount} error(s):\n{output}");
        return false;
      }

      var systemsOut = Path.Combine(OutDir, "systems");
      var jsCount = Directory.Exists(systemsOut)
        ? Directory.GetFiles(systemsOut, "*.js", SearchOption.AllDirectories).Length
        : 0;
      Debug.Log($"[TscCompiler] Compiled {jsCount} system(s) to Library/TscBuild/");
      return true;
    }
  }
}
#endif
