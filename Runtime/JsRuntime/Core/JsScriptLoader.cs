namespace UnityJS.Runtime
{
  using System;
  using System.IO;
  using Unity.Collections;
  using UnityEngine;

  public enum JsScriptSourceType : byte
  {
    NONE = 0,
    STRING = 1,
    STREAMING_ASSETS = 2,
    FILE_PATH = 3,
  }

  /// <summary>
  /// Burst-compatible result structure for script loading operations.
  /// </summary>
  public struct JsScriptLoadResult
  {
    public FixedString64Bytes scriptId;
    public FixedString512Bytes filePath;
    public JsScriptSourceType sourceType;
    public bool isValid;
    public FixedString128Bytes error;

    public static JsScriptLoadResult Success(
      FixedString64Bytes scriptId,
      JsScriptSourceType sourceType,
      FixedString512Bytes filePath = default
    )
    {
      return new JsScriptLoadResult
      {
        scriptId = scriptId,
        filePath = filePath,
        sourceType = sourceType,
        isValid = true,
        error = default,
      };
    }

    public static JsScriptLoadResult Failure(FixedString128Bytes error)
    {
      return new JsScriptLoadResult
      {
        scriptId = default,
        filePath = default,
        sourceType = JsScriptSourceType.NONE,
        isValid = false,
        error = error,
      };
    }
  }

  /// <summary>
  /// Centralized utility for loading JS/TS scripts from various sources.
  /// </summary>
  public static class JsScriptLoader
  {
    static readonly string s_streamingAssetsJsPath = Path.Combine(
      Application.streamingAssetsPath,
      "unity.js"
    );

    public static JsScriptLoadResult ValidateScriptId(string scriptId)
    {
      if (string.IsNullOrEmpty(scriptId))
        return JsScriptLoadResult.Failure("Script ID cannot be empty");

      if (scriptId.Length > FixedString64Bytes.UTF8MaxLengthInBytes)
        return JsScriptLoadResult.Failure("Script ID too long (max 64 bytes)");

      return JsScriptLoadResult.Success(
        new FixedString64Bytes(scriptId),
        JsScriptSourceType.STRING
      );
    }

    /// <summary>
    /// Try to find a script file (.ts first, then .js) under StreamingAssets/unity.js/.
    /// </summary>
    public static JsScriptLoadResult FromStreamingAssets(string relativePath)
    {
      if (string.IsNullOrEmpty(relativePath))
        return JsScriptLoadResult.Failure("Relative path cannot be empty");

      var normalized = NormalizePath(relativePath);
      if (normalized.Length > FixedString64Bytes.UTF8MaxLengthInBytes)
        return JsScriptLoadResult.Failure("Script ID too long (max 64 bytes)");

      // Try .ts first, fall back to .js
      var filePath = Path.Combine(s_streamingAssetsJsPath, normalized + ".ts");
      if (!File.Exists(filePath))
      {
        filePath = Path.Combine(s_streamingAssetsJsPath, normalized + ".js");
        if (!File.Exists(filePath))
          return JsScriptLoadResult.Failure($"File not found: {normalized}");
      }

      if (filePath.Length > FixedString512Bytes.UTF8MaxLengthInBytes)
        return JsScriptLoadResult.Failure("File path too long (max 512 bytes)");

      return JsScriptLoadResult.Success(
        new FixedString64Bytes(normalized),
        JsScriptSourceType.STREAMING_ASSETS,
        new FixedString512Bytes(filePath)
      );
    }

    public static JsScriptLoadResult FromFile(string filePath)
    {
      if (string.IsNullOrEmpty(filePath))
        return JsScriptLoadResult.Failure("File path cannot be empty");

      var resolvedPath = ResolvePath(filePath);
      if (string.IsNullOrEmpty(resolvedPath))
        return JsScriptLoadResult.Failure("Failed to resolve path");

      if (!File.Exists(resolvedPath))
        return JsScriptLoadResult.Failure($"File not found: {filePath}");

      var scriptId = Path.GetFileNameWithoutExtension(resolvedPath);
      if (string.IsNullOrEmpty(scriptId))
        return JsScriptLoadResult.Failure("Could not extract script ID from path");

      if (scriptId.Length > FixedString64Bytes.UTF8MaxLengthInBytes)
        return JsScriptLoadResult.Failure("Script ID too long (max 64 bytes)");

      if (resolvedPath.Length > FixedString512Bytes.UTF8MaxLengthInBytes)
        return JsScriptLoadResult.Failure("File path too long (max 512 bytes)");

      return JsScriptLoadResult.Success(
        new FixedString64Bytes(scriptId),
        JsScriptSourceType.FILE_PATH,
        new FixedString512Bytes(resolvedPath)
      );
    }

    public static JsScriptLoadResult FromSearchPaths(string relativePath)
    {
      if (string.IsNullOrEmpty(relativePath))
        return JsScriptLoadResult.Failure("Relative path cannot be empty");

      var normalized = NormalizePath(relativePath);
      if (normalized.Length > FixedString64Bytes.UTF8MaxLengthInBytes)
        return JsScriptLoadResult.Failure("Script ID too long (max 64 bytes)");

      // Try .ts first, fall back to .js
      var relativeFilePath = normalized + ".ts";
      if (!JsScriptSearchPaths.TryFindScript(relativeFilePath, out var filePath, out _))
      {
        relativeFilePath = normalized + ".js";
        if (!JsScriptSearchPaths.TryFindScript(relativeFilePath, out filePath, out _))
          return JsScriptLoadResult.Failure($"File not found in any search path: {normalized}");
      }

      if (filePath.Length > FixedString512Bytes.UTF8MaxLengthInBytes)
        return JsScriptLoadResult.Failure("File path too long (max 512 bytes)");

      return JsScriptLoadResult.Success(
        new FixedString64Bytes(normalized),
        JsScriptSourceType.FILE_PATH,
        new FixedString512Bytes(filePath)
      );
    }

    public static bool TryReadSource(in JsScriptLoadResult result, out string source)
    {
      source = null;

      if (!result.isValid)
        return false;

      if (
        result.sourceType != JsScriptSourceType.STREAMING_ASSETS
        && result.sourceType != JsScriptSourceType.FILE_PATH
      )
        return false;

      var filePath = result.filePath.ToString();
      if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        return false;

      try
      {
        source = File.ReadAllText(filePath);

        // Transpile .ts files
        if (filePath.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
          source = JsTranspiler.Transpile(source, filePath);

        return source != null;
      }
      catch (Exception)
      {
        return false;
      }
    }

    static string NormalizePath(string path)
    {
      if (string.IsNullOrEmpty(path))
        return string.Empty;

      var normalized = path.Replace('\\', '/').Trim('/');

      if (normalized.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
        normalized = normalized[..^3];
      else if (normalized.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
        normalized = normalized[..^3];

      return normalized;
    }

    static string ResolvePath(string path)
    {
      if (string.IsNullOrEmpty(path))
        return null;

      if (Path.IsPathRooted(path))
        return path;

      var fromProject = Path.Combine(Application.dataPath, "..", path);
      if (File.Exists(fromProject))
        return Path.GetFullPath(fromProject);

      var fromAssets = Path.Combine(Application.dataPath, path);
      if (File.Exists(fromAssets))
        return Path.GetFullPath(fromAssets);

      return Path.GetFullPath(path);
    }
  }
}
