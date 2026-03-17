#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using Unity.Logging;
using UnityEngine;

namespace UnityJS.Editor
{
  class TscBuildPreprocessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport
  {
    public int callbackOrder => 0;

    static string TscBuildRoot => TscCompiler.OutDir;

    static string StreamingAssetsRoot =>
      Path.Combine(Application.dataPath, "StreamingAssets", "unity.js");

    static readonly List<string> s_CopiedFiles = new();

    public void OnPreprocessBuild(BuildReport report)
    {
      if (!TscCompiler.RunTsc())
        throw new BuildFailedException("[TscBuildPreprocessor] TypeScript compilation failed");

      var src = TscBuildRoot;
      if (!Directory.Exists(src))
        throw new BuildFailedException($"[TscBuildPreprocessor] No compiled JS at {src}");

      s_CopiedFiles.Clear();

      foreach (var jsFile in Directory.GetFiles(src, "*.js", SearchOption.AllDirectories))
      {
        var relative = jsFile
          .Substring(src.Length)
          .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var target = Path.Combine(StreamingAssetsRoot, relative);
        var targetDir = Path.GetDirectoryName(target);
        if (targetDir != null && !Directory.Exists(targetDir))
          Directory.CreateDirectory(targetDir);
        File.Copy(jsFile, target, true);
        s_CopiedFiles.Add(target);
      }

      UnityEditor.AssetDatabase.Refresh();
      Log.Debug("[TscBuildPreprocessor] Copied {0} compiled JS file(s) to StreamingAssets for build", s_CopiedFiles.Count);
    }

    public void OnPostprocessBuild(BuildReport report)
    {
      foreach (var file in s_CopiedFiles)
      {
        if (File.Exists(file))
          File.Delete(file);
        var meta = file + ".meta";
        if (File.Exists(meta))
          File.Delete(meta);
      }

      s_CopiedFiles.Clear();

      UnityEditor.AssetDatabase.Refresh();
      Log.Debug("[TscBuildPreprocessor] Cleaned up temporary JS from StreamingAssets");
    }
  }
}
#endif
