#if UNITY_EDITOR
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using Unity.Logging;
using UnityEngine;

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

    internal static void CleanOutDir()
    {
      if (Directory.Exists(OutDir))
        Directory.Delete(OutDir, true);
    }

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
      if (!Directory.Exists(OutDir))
        return true;

      var tsRoot = Path.GetFullPath(
        Path.Combine(Application.dataPath, "StreamingAssets", "unity.js")
      );

      foreach (var subdir in new[] { "systems", "components" })
      {
        var tsDir = Path.Combine(tsRoot, subdir);
        if (!Directory.Exists(tsDir))
          continue;

        var outSubdir = Path.Combine(OutDir, subdir);
        var tsFiles = Directory.GetFiles(tsDir, "*.ts", SearchOption.AllDirectories);
        foreach (var ts in tsFiles)
        {
          var relative = ts.Substring(tsDir.Length)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
          var jsName = Path.ChangeExtension(relative, ".js");
          var jsPath = Path.Combine(outSubdir, jsName);
          if (!File.Exists(jsPath))
            return true;
          if (File.GetLastWriteTimeUtc(ts) > File.GetLastWriteTimeUtc(jsPath))
            return true;
        }
      }

      return false;
    }

    internal static bool RunTsc()
    {
      CleanOutDir();
      var tsconfigPath = TsconfigPath;
      if (!File.Exists(tsconfigPath))
      {
        Log.Warning("[TscCompiler] tsconfig.json not found at {0}", tsconfigPath);
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
        Log.Error("[TscCompiler] Failed to start tsc process");
        return false;
      }

      var stdout = proc.StandardOutput.ReadToEnd();
      var stderr = proc.StandardError.ReadToEnd();
      proc.WaitForExit(30000);

      if (proc.ExitCode != 0)
      {
        var output = (stdout + "\n" + stderr).Trim();
        var errorCount = output.Split('\n').Count(l => l.Contains("error TS"));
        Log.Error("[TscCompiler] tsc failed with {0} error(s):\n{1}", errorCount, output);
        return false;
      }

      var jsCount = Directory.Exists(OutDir)
        ? Directory.GetFiles(OutDir, "*.js", SearchOption.AllDirectories).Length
        : 0;
      Log.Debug("[TscCompiler] Compiled {0} file(s) to Library/TscBuild/", jsCount);
      return true;
    }
  }
}
#endif
