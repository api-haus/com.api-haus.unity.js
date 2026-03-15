#if UNITY_EDITOR
using System.IO;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace UnityJS.Editor
{
  class TscBuildPreprocessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport
  {
    public int callbackOrder => 0;

    static string StreamingAssetsSystemsPath =>
      Path.Combine(Application.dataPath, "StreamingAssets", "unity.js", "systems");

    static string TscBuildSystemsPath =>
      Path.Combine(Application.dataPath, "..", "Library", "TscBuild", "systems");

    public void OnPreprocessBuild(BuildReport report)
    {
      if (!TscCompiler.RunTsc())
        throw new BuildFailedException("[TscBuildPreprocessor] TypeScript compilation failed");

      var src = TscBuildSystemsPath;
      var dst = StreamingAssetsSystemsPath;

      if (!Directory.Exists(src))
        throw new BuildFailedException($"[TscBuildPreprocessor] No compiled JS at {src}");

      if (!Directory.Exists(dst))
        Directory.CreateDirectory(dst);

      foreach (var jsFile in Directory.GetFiles(src, "*.js", SearchOption.AllDirectories))
      {
        var relative = jsFile
          .Substring(src.Length)
          .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var target = Path.Combine(dst, relative);
        var targetDir = Path.GetDirectoryName(target);
        if (targetDir != null && !Directory.Exists(targetDir))
          Directory.CreateDirectory(targetDir);
        File.Copy(jsFile, target, true);
      }

      UnityEditor.AssetDatabase.Refresh();
      Debug.Log("[TscBuildPreprocessor] Copied compiled JS to StreamingAssets for build");
    }

    public void OnPostprocessBuild(BuildReport report)
    {
      var dst = StreamingAssetsSystemsPath;
      if (!Directory.Exists(dst))
        return;

      foreach (var jsFile in Directory.GetFiles(dst, "*.js"))
      {
        File.Delete(jsFile);
        var meta = jsFile + ".meta";
        if (File.Exists(meta))
          File.Delete(meta);
      }

      UnityEditor.AssetDatabase.Refresh();
      Debug.Log("[TscBuildPreprocessor] Cleaned up temporary JS from StreamingAssets");
    }
  }
}
#endif
