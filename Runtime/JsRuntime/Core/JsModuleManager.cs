namespace UnityJS.Runtime
{
  using System.Collections.Generic;
  using System.Text;
  using QJS;
  using Unity.Logging;

  /// <summary>
  /// Manages ES module loading, caching, hot-reload, and verification.
  /// Extracted from JsRuntimeManager.
  /// </summary>
  public class JsModuleManager
  {
    static readonly byte[] s_lastLoadedModule = QJS.U8("__lastLoadedModule");
    static readonly byte[] s_componentReload = QJS.U8("__componentReload");
    static readonly byte[] s_verifyModuleExports = QJS.U8("__verifyModuleExports");

    readonly JsRuntimeManager m_Owner;
    internal readonly Dictionary<string, JSValue> ScriptRefs = new();
    readonly Dictionary<string, int> m_ReloadVersions = new();

    internal JsModuleManager(JsRuntimeManager owner)
    {
      m_Owner = owner;
    }

    public bool Has(string scriptId)
    {
      return ScriptRefs.ContainsKey(scriptId);
    }

    public unsafe bool SetLastLoaded(string scriptId)
    {
      if (!ScriptRefs.TryGetValue(scriptId, out var ns))
        return false;
      var ctx = m_Owner.Context;
      var global = QJS.JS_GetGlobalObject(ctx);
      fixed (byte* pLast = s_lastLoadedModule)
        QJS.JS_SetPropertyStr(ctx, global, pLast, QJS.JS_DupValue(ctx, ns));
      QJS.JS_FreeValue(ctx, global);
      return true;
    }

    public unsafe void ComponentReload(string scriptName)
    {
      SetLastLoaded(scriptName);
      var ctx = m_Owner.Context;
      var global = QJS.JS_GetGlobalObject(ctx);
      fixed (byte* p = s_componentReload)
      {
        var func = QJS.JS_GetPropertyStr(ctx, global, p);
        if (QJS.JS_IsFunction(ctx, func) != 0)
        {
          var nameBytes = m_Owner.GetOrCacheBytes(scriptName);
          fixed (byte* pName = nameBytes)
          {
            var argv = stackalloc JSValue[1];
            argv[0] = QJS.JS_NewString(ctx, pName);
            var result = QJS.JS_Call(ctx, func, global, 1, argv);
            if (QJS.IsException(result))
              m_Owner.LogException(scriptName);
            QJS.JS_FreeValue(ctx, result);
            QJS.JS_FreeValue(ctx, argv[0]);
          }
        }
        QJS.JS_FreeValue(ctx, func);
      }
      QJS.JS_FreeValue(ctx, global);
    }

    public bool Load(string scriptName)
    {
      var result = JsScriptLoader.FromSearchPaths(scriptName);
      if (!result.isValid)
      {
        Log.Error("[JsRuntime] Failed to find script '{0}': {1}", scriptName, result.error);
        return false;
      }

      return Load(result);
    }

    public bool Load(JsScriptLoadResult result)
    {
      if (!result.isValid)
        return false;

      if (!JsScriptLoader.TryReadSource(in result, out var source))
      {
        Log.Error("[JsRuntime] Failed to read source for '{0}'", result.scriptId);
        return false;
      }

      var scriptId = result.scriptId.ToString();
      var filePath = result.filePath.ToString();
      return LoadAsModule(scriptId, source, filePath);
    }

    public bool LoadFromString(string scriptId, string source)
    {
      return LoadAsModule(scriptId, source, scriptId);
    }

    public unsafe bool LoadAsModule(string scriptId, string source, string filename)
    {
      var ctx = m_Owner.Context;
      var sourceBytes = Encoding.UTF8.GetBytes(source + '\0');
      var sourceLen = sourceBytes.Length - 1;
      var filenameBytes = Encoding.UTF8.GetBytes(filename + '\0');

      fixed (
        byte* pSource = sourceBytes,
          pFilename = filenameBytes
      )
      {
        var ns = QJSShim.qjs_shim_eval_module(ctx, pSource, sourceLen, pFilename);

        if (QJS.IsException(ns))
        {
          m_Owner.LogException($"LoadScriptAsModule({scriptId})");
          return false;
        }

        if (ScriptRefs.TryGetValue(scriptId, out var old))
          QJS.JS_FreeValue(ctx, old);
        ScriptRefs[scriptId] = ns;

        if (!scriptId.StartsWith("__"))
        {
          var global = QJS.JS_GetGlobalObject(ctx);
          fixed (byte* pLast = s_lastLoadedModule)
            QJS.JS_SetPropertyStr(ctx, global, pLast, QJS.JS_DupValue(ctx, ns));
          QJS.JS_FreeValue(ctx, global);
        }

        return true;
      }
    }

    public unsafe bool Reload(string scriptName, string source, string filename)
    {
      var ctx = m_Owner.Context;
      if (ScriptRefs.TryGetValue(scriptName, out var old))
      {
        QJS.JS_FreeValue(ctx, old);
        ScriptRefs.Remove(scriptName);
      }

      m_ReloadVersions.TryGetValue(filename, out var version);
      version++;
      m_ReloadVersions[filename] = version;
      var versionedFilename = filename + "?v=" + version;

      return LoadAsModule(scriptName, source, versionedFilename);
    }

    public bool SimulateHotReload(string scriptName)
    {
      if (!JsScriptSourceRegistry.TryReadScript(scriptName, out var source, out var resolvedId))
      {
        Log.Error("[JsRuntime] SimulateHotReload: script not found: {0}", scriptName);
        return false;
      }

      return Reload(scriptName, source, resolvedId);
    }

    public unsafe string VerifyHealth()
    {
      var ctx = m_Owner.Context;
      var global = QJS.JS_GetGlobalObject(ctx);
      JSValue checkFn;
      fixed (byte* p = s_verifyModuleExports)
        checkFn = QJS.JS_GetPropertyStr(ctx, global, p);

      if (QJS.JS_IsFunction(ctx, checkFn) == 0)
      {
        QJS.JS_FreeValue(ctx, checkFn);
        QJS.JS_FreeValue(ctx, global);
        return null;
      }

      foreach (var (scriptId, ns) in ScriptRefs)
      {
        if (scriptId.StartsWith("__"))
          continue;
        var argv = stackalloc JSValue[1];
        argv[0] = QJS.JS_DupValue(ctx, ns);
        var result = QJS.JS_Call(ctx, checkFn, global, 1, argv);
        QJS.JS_FreeValue(ctx, argv[0]);

        if (QJS.IsException(result))
        {
          m_Owner.LogException($"VerifyModuleHealth({scriptId})");
          QJS.JS_FreeValue(ctx, result);
          QJS.JS_FreeValue(ctx, checkFn);
          QJS.JS_FreeValue(ctx, global);
          return $"Exception verifying '{scriptId}'";
        }

        if (result != QJS.JS_NULL && result != QJS.JS_UNDEFINED)
        {
          var err = QJS.ToManagedString(ctx, result);
          QJS.JS_FreeValue(ctx, result);
          QJS.JS_FreeValue(ctx, checkFn);
          QJS.JS_FreeValue(ctx, global);
          return $"TDZ in '{scriptId}': {err}";
        }
        QJS.JS_FreeValue(ctx, result);
      }

      QJS.JS_FreeValue(ctx, checkFn);
      QJS.JS_FreeValue(ctx, global);
      return null;
    }

    internal void DisposeAll()
    {
      var ctx = m_Owner.Context;
      foreach (var kv in ScriptRefs)
        QJS.JS_FreeValue(ctx, kv.Value);
      ScriptRefs.Clear();
    }
  }
}
