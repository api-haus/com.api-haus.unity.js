namespace UnityJS.Runtime
{
  using System;
  using System.IO;
  using System.Runtime.InteropServices;
  using System.Text;
  using AOT;
  using QJS;

  /// <summary>
  /// ES module resolution. Two MonoPInvokeCallback methods called from C shim trampolines.
  /// </summary>
  public static unsafe class JsModuleLoader
  {
    static QJSNormalizeCallback s_normalizeDelegate;
    static QJSReadFileCallback s_readFileDelegate;
    static JSContext s_ctx;

    static string StripVersionSuffix(string path)
    {
      var idx = path.IndexOf("?v=", StringComparison.Ordinal);
      return idx >= 0 ? path.Substring(0, idx) : path;
    }

    /// <summary>
    /// Resolve a file extension for a path without one.
    /// Prefers .ts on disk, falls back to .js.
    /// </summary>
    static string ResolveExtension(string pathWithoutExt)
    {
      if (File.Exists(pathWithoutExt + ".ts"))
        return pathWithoutExt + ".ts";
      return pathWithoutExt + ".js";
    }

    [MonoPInvokeCallback(typeof(QJSNormalizeCallback))]
    static int Normalize(JSContext ctx, byte* baseName, byte* name, byte* outBuf, int outBufLen)
    {
      try
      {
        var nameStr = Marshal.PtrToStringUTF8((nint)name);
        if (string.IsNullOrEmpty(nameStr))
          return 0;

        string resolved;

        if (nameStr.StartsWith("./") || nameStr.StartsWith("../"))
        {
          var baseStr = StripVersionSuffix(Marshal.PtrToStringUTF8((nint)baseName) ?? "");

          if (baseStr.StartsWith("bundle://"))
          {
            var baseDir = baseStr.Substring(0, baseStr.LastIndexOf('/'));
            var relative = nameStr;
            while (relative.StartsWith("../"))
            {
              baseDir = baseDir.Substring(0, baseDir.LastIndexOf('/'));
              relative = relative.Substring(3);
            }

            if (relative.StartsWith("./"))
              relative = relative.Substring(2);
            resolved = baseDir + "/" + relative;
          }
          else
          {
            var baseDir = Path.GetDirectoryName(baseStr) ?? "";
            resolved = Path.GetFullPath(Path.Combine(baseDir, nameStr));
          }
        }
        else if (nameStr.StartsWith("unity.js/"))
        {
          resolved = nameStr;
        }
        else
        {
          // Bare module import — try .ts first, then .js
          var searchNameTs = nameStr;
          if (!searchNameTs.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
              && !searchNameTs.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
            searchNameTs += ".ts";

          if (JsScriptSourceRegistry.TryFindModule(searchNameTs, out var resolvedPath))
            resolved = resolvedPath;
          else if (JsScriptSearchPaths.TryFindScript(searchNameTs, out var foundPath, out _))
            resolved = foundPath;
          else
          {
            // Fall back to .js
            var searchNameJs = nameStr;
            if (!searchNameJs.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
              searchNameJs += ".js";

            if (JsScriptSourceRegistry.TryFindModule(searchNameJs, out resolvedPath))
              resolved = resolvedPath;
            else if (JsScriptSearchPaths.TryFindScript(searchNameJs, out foundPath, out _))
              resolved = foundPath;
            else
              resolved = nameStr;
          }
        }

        // Append extension if missing — prefer .ts on disk, fall back to .js
        if (!resolved.StartsWith("unity.js/")
            && !resolved.StartsWith("bundle://")
            && !resolved.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
            && !resolved.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
        {
          resolved = ResolveExtension(resolved);
        }

        var bytes = Encoding.UTF8.GetBytes(resolved);
        if (bytes.Length > outBufLen)
          return 0;

        Marshal.Copy(bytes, 0, (nint)outBuf, bytes.Length);
        return bytes.Length;
      }
      catch
      {
        return 0;
      }
    }

    [MonoPInvokeCallback(typeof(QJSReadFileCallback))]
    static int ReadFile(byte* name, byte* outBuf, int outBufLen)
    {
      try
      {
        var nameStr = Marshal.PtrToStringUTF8((nint)name);
        if (string.IsNullOrEmpty(nameStr))
          return 0;

        // Handle unity.js/* synthetic modules
        if (nameStr.StartsWith("unity.js/"))
        {
          var source = JsBuiltinModules.GetModuleSource(nameStr);
          if (source == null)
            return 0;

          var moduleData = Encoding.UTF8.GetBytes(source);

          if (outBuf == null)
            return moduleData.Length;

          if (moduleData.Length > outBufLen)
            return 0;

          Marshal.Copy(moduleData, 0, (nint)outBuf, moduleData.Length);
          return moduleData.Length;
        }

        // Handle bundle:// virtual paths
        if (nameStr.StartsWith("bundle://"))
        {
          if (!JsScriptSourceRegistry.TryReadModuleBytes(nameStr, out var bundleData))
            return 0;

          if (outBuf == null)
            return bundleData.Length;

          if (bundleData.Length > outBufLen)
            return 0;

          Marshal.Copy(bundleData, 0, (nint)outBuf, bundleData.Length);
          return bundleData.Length;
        }

        // Filesystem path — strip ?v=N version suffix used for cache invalidation
        var fsPath = StripVersionSuffix(nameStr);

        // Try .ts variant if .js not found
        if (!File.Exists(fsPath) && fsPath.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
        {
          var tsPath = fsPath[..^3] + ".ts";
          if (File.Exists(tsPath))
            fsPath = tsPath;
        }

        if (!File.Exists(fsPath))
          return 0;

        byte[] data;
        if (fsPath.EndsWith(".ts", StringComparison.OrdinalIgnoreCase))
        {
          // Transpile .ts files on the fly
          var tsSource = File.ReadAllText(fsPath);
          var jsSource = JsTranspiler.Transpile(tsSource, fsPath);
          if (jsSource == null)
            return 0;
          data = Encoding.UTF8.GetBytes(jsSource);
        }
        else
        {
          data = File.ReadAllBytes(fsPath);
        }

        // Size query
        if (outBuf == null)
          return data.Length;

        if (data.Length > outBufLen)
          return 0;

        Marshal.Copy(data, 0, (nint)outBuf, data.Length);
        return data.Length;
      }
      catch
      {
        return 0;
      }
    }

    public static void Install(JSContext ctx)
    {
      s_ctx = ctx;
      s_normalizeDelegate = Normalize;
      s_readFileDelegate = ReadFile;
      QJSShim.qjs_shim_set_module_loader(ctx, s_normalizeDelegate, s_readFileDelegate);
    }

    public static void Uninstall()
    {
      s_ctx = default;
    }
  }
}
