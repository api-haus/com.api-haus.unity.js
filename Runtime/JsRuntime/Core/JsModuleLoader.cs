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

    static string StripVersionSuffix(string path)
    {
      var idx = path.IndexOf("?v=", StringComparison.Ordinal);
      return idx >= 0 ? path.Substring(0, idx) : path;
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
            // Virtual path — resolve manually without Path.GetFullPath
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
          // Synthetic built-in module — pass specifier through as-is
          resolved = nameStr;
        }
        else
        {
          // Bare module import — try registry then search paths
          var searchName = nameStr;
          if (!searchName.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
            searchName += ".js";

          if (JsScriptSourceRegistry.TryFindModule(searchName, out var resolvedPath))
            resolved = resolvedPath;
          else if (JsScriptSearchPaths.TryFindScript(searchName, out var foundPath, out _))
            resolved = foundPath;
          else
            resolved = nameStr;
        }

        if (!resolved.StartsWith("unity.js/") && !resolved.EndsWith(".js", StringComparison.OrdinalIgnoreCase))
          resolved += ".js";

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

          var moduleData = Encoding.UTF8.GetBytes(source + '\0');
          var moduleLen = moduleData.Length - 1;

          if (outBuf == null)
            return moduleLen;

          if (moduleLen > outBufLen)
            return 0;

          Marshal.Copy(moduleData, 0, (nint)outBuf, moduleLen);
          return moduleLen;
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
        if (!File.Exists(fsPath))
          return 0;

        var data = File.ReadAllBytes(fsPath);

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
      // Keep delegates alive for the lifetime of the process
      s_normalizeDelegate = Normalize;
      s_readFileDelegate = ReadFile;
      QJSShim.qjs_shim_set_module_loader(ctx, s_normalizeDelegate, s_readFileDelegate);
    }
  }
}
