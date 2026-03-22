namespace UnityJS.Runtime
{
  using System;
  using System.Collections.Generic;
  using System.Runtime.InteropServices;
  using System.Text;
  using QJS;
  using Unity.Collections;
  using Unity.Logging;

  /// <summary>
  /// Core runtime host. Owns JSRuntime + JSContext lifecycle.
  /// </summary>
  public class JsRuntimeManager : IDisposable
  {
    /// <summary>
    /// Whether the native shim callback table needs reset before next VM creation.
    /// Set true after the first VM is disposed; checked in the constructor.
    /// </summary>
    static bool s_shimDirty;

    static JsRuntimeManager s_Instance;
    static int s_InstanceVersion;
    public static JsRuntimeManager Instance => s_Instance;
    public static int InstanceVersion => s_InstanceVersion;

    /// <summary>
    /// Restores a previously saved VM as the global singleton.
    /// Used by tests that temporarily replace the singleton with a test VM.
    /// </summary>
    public static void RestoreInstance(JsRuntimeManager vm)
    {
      if (vm != null && vm.IsValid)
      {
        s_Instance = vm;
        // Don't increment version — the VM didn't change, it was just temporarily shadowed
      }
    }

    JSRuntime m_Runtime;
    JSContext m_Context;
    readonly Dictionary<string, JSValue> m_ScriptRefs = new();
    readonly Dictionary<string, int> m_ReloadVersions = new();

    int m_NextStateId = 1;
    readonly Dictionary<int, JSValue> m_StateRefs = new();

    readonly List<string> m_CapturedExceptions = new();
    public IReadOnlyList<string> CapturedExceptions => m_CapturedExceptions;

    public void ClearCapturedExceptions() => m_CapturedExceptions.Clear();

    readonly Dictionary<FixedString64Bytes, string> m_StringCache = new();
    readonly Dictionary<FixedString32Bytes, string> m_StringCache32 = new();

    /// <summary>
    /// Returns a cached managed string for a FixedString64. One allocation per unique
    /// value across the VM lifetime; zero per frame.
    /// </summary>
    public string Intern(in FixedString64Bytes fs)
    {
      if (m_StringCache.TryGetValue(fs, out var s))
        return s;
      s = fs.ToString();
      m_StringCache[fs] = s;
      return s;
    }

    /// <summary>
    /// Returns a cached managed string for a FixedString32.
    /// </summary>
    public string Intern(in FixedString32Bytes fs)
    {
      if (m_StringCache32.TryGetValue(fs, out var s))
        return s;
      s = fs.ToString();
      m_StringCache32[fs] = s;
      return s;
    }

    public JSRuntime Runtime => m_Runtime;
    public JSContext Context => m_Context;
    public bool IsValid => !m_Context.IsNull;

    /// <summary>
    /// Instance-scoped bridge state. Set by the Entities layer on first bridge registration.
    /// Disposed atomically with the VM. Typed as object to avoid circular assembly reference.
    /// </summary>
    public IDisposable BridgeState { get; set; }

    /// <summary>
    /// Returns true if a script module with this ID is already loaded.
    /// </summary>
    public bool HasScript(string scriptId)
    {
      return m_ScriptRefs.ContainsKey(scriptId);
    }

    public unsafe bool SetLastLoadedModule(string scriptId)
    {
      if (!m_ScriptRefs.TryGetValue(scriptId, out var ns))
        return false;
      var global = QJS.JS_GetGlobalObject(m_Context);
      fixed (byte* pLast = s_lastLoadedModule)
        QJS.JS_SetPropertyStr(m_Context, global, pLast, QJS.JS_DupValue(m_Context, ns));
      QJS.JS_FreeValue(m_Context, global);
      return true;
    }

    /// <summary>
    /// Swaps prototypes on existing Component instances after a hot-reload.
    /// Calls globalThis.__componentReload(scriptName) which sets Object.setPrototypeOf
    /// on every live instance to the newly loaded class's prototype.
    /// </summary>
    public unsafe void ComponentReload(string scriptName)
    {
      SetLastLoadedModule(scriptName);
      var global = QJS.JS_GetGlobalObject(m_Context);
      fixed (byte* p = s_componentReload)
      {
        var func = QJS.JS_GetPropertyStr(m_Context, global, p);
        if (QJS.JS_IsFunction(m_Context, func) != 0)
        {
          var nameBytes = GetOrCacheBytes(scriptName);
          fixed (byte* pName = nameBytes)
          {
            var argv = stackalloc JSValue[1];
            argv[0] = QJS.JS_NewString(m_Context, pName);
            var result = QJS.JS_Call(m_Context, func, global, 1, argv);
            if (QJS.IsException(result))
              LogException(scriptName);
            QJS.JS_FreeValue(m_Context, result);
            QJS.JS_FreeValue(m_Context, argv[0]);
          }
        }
        QJS.JS_FreeValue(m_Context, func);
      }
      QJS.JS_FreeValue(m_Context, global);
    }

    public JsRuntimeManager(string basePath = null)
    {
      if (s_shimDirty)
      {
        QJSShim.qjs_shim_reset();
        s_shimDirty = false;
      }

      m_Runtime = QJS.JS_NewRuntime();
      m_Context = QJS.JS_NewContext(m_Runtime);

      if (!string.IsNullOrEmpty(basePath))
        JsScriptSearchPaths.AddSearchPath(basePath, 0);

      JsModuleLoader.Install(m_Context);

      s_InstanceVersion++;
      s_Instance = this;
    }

    public static JsRuntimeManager GetOrCreate(string basePath = null)
    {
      if (s_Instance != null && s_Instance.IsValid)
        return s_Instance;

      return new JsRuntimeManager(basePath);
    }

    public bool LoadScript(string scriptName)
    {
      var result = JsScriptLoader.FromSearchPaths(scriptName);
      if (!result.isValid)
      {
        Log.Error("[JsRuntime] Failed to find script '{0}': {1}", scriptName, result.error);
        return false;
      }

      return LoadScript(result);
    }

    public bool LoadScript(JsScriptLoadResult result)
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
      return LoadScriptAsModule(scriptId, source, filePath);
    }

    public unsafe bool LoadScriptFromString(string scriptId, string source)
    {
      return LoadScriptAsModule(scriptId, source, scriptId);
    }

    /// <summary>
    /// Loads a script as an ES module, storing the module namespace object
    /// in m_ScriptRefs for later CallFunction use.
    /// </summary>
    public unsafe bool LoadScriptAsModule(string scriptId, string source, string filename)
    {
      var sourceBytes = Encoding.UTF8.GetBytes(source + '\0');
      var sourceLen = sourceBytes.Length - 1;
      var filenameBytes = Encoding.UTF8.GetBytes(filename + '\0');

      fixed (
        byte* pSource = sourceBytes,
          pFilename = filenameBytes
      )
      {
        var ns = QJSShim.qjs_shim_eval_module(m_Context, pSource, sourceLen, pFilename);

        if (QJS.IsException(ns))
        {
          LogException($"LoadScriptAsModule({scriptId})");
          return false;
        }

        if (m_ScriptRefs.TryGetValue(scriptId, out var old))
          QJS.JS_FreeValue(m_Context, old);
        m_ScriptRefs[scriptId] = ns;

        // Set globalThis.__lastLoadedModule for Component class detection
        // Only for user scripts, not internal glue modules (prefixed with __)
        if (!scriptId.StartsWith("__"))
        {
          var global = QJS.JS_GetGlobalObject(m_Context);
          fixed (byte* pLast = s_lastLoadedModule)
            QJS.JS_SetPropertyStr(m_Context, global, pLast, QJS.JS_DupValue(m_Context, ns));
          QJS.JS_FreeValue(m_Context, global);
        }

        return true;
      }
    }

    /// <summary>
    /// Creates a JS state object for an entity script instance.
    /// Returns a monotonic int key into the state dictionary.
    /// </summary>
    static readonly byte[] s_script = QJS.U8("_script");
    static readonly byte[] s_entityId = QJS.U8("entityId");
    static readonly byte[] s_deltaTime = QJS.U8("deltaTime");
    static readonly byte[] s_elapsedTime = QJS.U8("elapsedTime");
    static readonly byte[] s_onInit = QJS.U8("onInit");
    static readonly byte[] s_onTick = QJS.U8("onTick");
    static readonly byte[] s_onEvent = QJS.U8("onEvent");
    static readonly byte[] s_onCommand = QJS.U8("onCommand");
    static readonly byte[] s_onUpdate = QJS.U8("onUpdate");
    static readonly byte[] s_onDestroy = QJS.U8("onDestroy");
    static readonly byte[] s_tickComponents = QJS.U8("__tickComponents");
    static readonly byte[] s_flushRefRw = QJS.U8("__flushRefRw");
    static readonly byte[] s_componentInit = QJS.U8("__componentInit");
    static readonly byte[] s_lastLoadedModule = QJS.U8("__lastLoadedModule");
    static readonly byte[] s_componentReload = QJS.U8("__componentReload");
    static readonly byte[] s_verifyModuleExports = QJS.U8("__verifyModuleExports");

    static readonly byte[] s_groupUpdate = QJS.U8("update");
    static readonly byte[] s_groupFixedUpdate = QJS.U8("fixedUpdate");
    static readonly byte[] s_groupLateUpdate = QJS.U8("lateUpdate");

    readonly Dictionary<string, byte[]> m_EncodedStringCache = new();

    public unsafe int CreateEntityState(string scriptName, int entityId)
    {
      var state = QJS.JS_NewObject(m_Context);

      fixed (
        byte* pEntityId = s_entityId,
          pDeltaTime = s_deltaTime,
          pElapsedTime = s_elapsedTime,
          pScript = s_script
      )
      {
        QJS.JS_SetPropertyStr(m_Context, state, pEntityId, QJS.NewInt32(m_Context, entityId));
        QJS.JS_SetPropertyStr(m_Context, state, pDeltaTime, QJS.NewFloat64(m_Context, 0.0));
        QJS.JS_SetPropertyStr(m_Context, state, pElapsedTime, QJS.NewFloat64(m_Context, 0.0));

        var nameBytes = GetOrCacheBytes(scriptName);
        fixed (byte* pName = nameBytes)
        {
          var nameVal = QJS.JS_NewString(m_Context, pName);
          QJS.JS_SetPropertyStr(m_Context, state, pScript, nameVal);
        }
      }

      var id = m_NextStateId++;
      m_StateRefs[id] = QJS.JS_DupValue(m_Context, state);
      QJS.JS_FreeValue(m_Context, state);
      return id;
    }

    /// <summary>
    /// Releases a JS entity state, freeing the JS value.
    /// </summary>
    public void ReleaseEntityState(int stateRef)
    {
      if (m_StateRefs.Remove(stateRef, out var val))
        QJS.JS_FreeValue(m_Context, val);
    }

    /// <summary>
    /// Checks whether a state ref is still valid.
    /// </summary>
    public bool ValidateStateRef(int stateRef)
    {
      return m_StateRefs.ContainsKey(stateRef);
    }

    /// <summary>
    /// Core invocation: resolves an export on a script module and calls it.
    /// Returns true on success or if the function doesn't exist (missing export is not an error).
    /// Caller must supply pre-encoded funcNameBytes (null-terminated UTF-8).
    /// argv[0] is always the state object; extra args (1..argc-1) are caller-owned and freed by caller.
    /// </summary>
    unsafe bool InvokeExport(
      string scriptName,
      byte[] funcNameBytes,
      int stateRef,
      JSValue* extraArgv,
      int extraArgc,
      string errorContext
    )
    {
      if (!m_ScriptRefs.TryGetValue(scriptName, out var scriptObj))
        return false;

      fixed (byte* pFuncName = funcNameBytes)
      {
        var func = QJS.JS_GetPropertyStr(m_Context, scriptObj, pFuncName);
        if (QJS.JS_IsFunction(m_Context, func) == 0)
        {
          QJS.JS_FreeValue(m_Context, func);
          return true; // missing func is not an error
        }

        if (!m_StateRefs.TryGetValue(stateRef, out var stateVal))
          stateVal = QJS.JS_UNDEFINED;

        var totalArgc = 1 + extraArgc;
        var argv = stackalloc JSValue[totalArgc];
        argv[0] = stateVal;
        for (var i = 0; i < extraArgc; i++)
          argv[1 + i] = extraArgv[i];

        var result = QJS.JS_Call(m_Context, func, scriptObj, totalArgc, argv);
        if (QJS.IsException(result))
        {
          LogException(errorContext);
          QJS.JS_FreeValue(m_Context, result);
          QJS.JS_FreeValue(m_Context, func);
          return false;
        }

        QJS.JS_FreeValue(m_Context, result);
        QJS.JS_FreeValue(m_Context, func);
        return true;
      }
    }

    /// <summary>
    /// Calls a named function on a script's module namespace object.
    /// Uses pre-cached byte array for the function name to avoid per-call allocation.
    /// </summary>
    public unsafe bool CallFunction(string scriptName, string funcName, int stateRef)
    {
      return InvokeExport(
        scriptName,
        GetOrCacheBytes(funcName),
        stateRef,
        null,
        0,
        scriptName
      );
    }

    /// <summary>
    /// Calls a named function using a pre-encoded byte array for the function name.
    /// Zero-allocation hot path.
    /// </summary>
    public unsafe bool CallFunction(string scriptName, byte[] funcNameBytes, int stateRef)
    {
      return InvokeExport(scriptName, funcNameBytes, stateRef, null, 0, scriptName);
    }

    byte[] GetOrCacheBytes(string value)
    {
      if (m_EncodedStringCache.TryGetValue(value, out var bytes))
        return bytes;
      bytes = QJS.U8(value);
      m_EncodedStringCache[value] = bytes;
      return bytes;
    }

    /// <summary>
    /// Calls OnInit on the script with the given state.
    /// </summary>
    public bool CallInit(string scriptName, int stateRef)
    {
      return CallFunction(scriptName, "onInit", stateRef);
    }

    /// <summary>
    /// Updates deltaTime and elapsedTime on a persistent state object.
    /// </summary>
    public unsafe void UpdateStateTimings(int stateRef, float deltaTime, double elapsedTime)
    {
      if (!m_StateRefs.TryGetValue(stateRef, out var stateVal))
        return;

      fixed (
        byte* pDt = s_deltaTime,
          pElapsed = s_elapsedTime
      )
      {
        QJS.JS_SetPropertyStr(m_Context, stateVal, pDt, QJS.NewFloat64(m_Context, deltaTime));
        QJS.JS_SetPropertyStr(
          m_Context,
          stateVal,
          pElapsed,
          QJS.NewFloat64(m_Context, elapsedTime)
        );
      }
    }

    /// <summary>
    /// Calls onTick on the script with the given state. Updates deltaTime/elapsedTime in-place.
    /// </summary>
    public unsafe bool CallTick(
      string scriptName,
      int stateRef,
      float deltaTime,
      double elapsedTime = 0.0
    )
    {
      UpdateStateTimings(stateRef, deltaTime, elapsedTime);
      return InvokeExport(scriptName, s_onTick, stateRef, null, 0, scriptName);
    }

    /// <summary>
    /// Calls onEvent on the script.
    /// </summary>
    public unsafe bool CallEvent(
      string scriptName,
      int stateRef,
      string eventName,
      int sourceId,
      int targetId,
      int intParam
    )
    {
      var eventNameBytes = GetOrCacheBytes(eventName);
      fixed (byte* pEventName = eventNameBytes)
      {
        var extras = stackalloc JSValue[4];
        extras[0] = QJS.JS_NewString(m_Context, pEventName);
        extras[1] = QJS.NewInt32(m_Context, sourceId);
        extras[2] = QJS.NewInt32(m_Context, targetId);
        extras[3] = QJS.NewInt32(m_Context, intParam);

        var ok = InvokeExport(
          scriptName,
          s_onEvent,
          stateRef,
          extras,
          4,
          scriptName
        );

        QJS.JS_FreeValue(m_Context, extras[0]);
        return ok;
      }
    }

    /// <summary>
    /// Calls onCommand on the script.
    /// </summary>
    public unsafe bool CallCommand(string scriptName, int stateRef, string command)
    {
      var cmdBytes = GetOrCacheBytes(command);
      fixed (byte* pCmd = cmdBytes)
      {
        var extras = stackalloc JSValue[1];
        extras[0] = QJS.JS_NewString(m_Context, pCmd);

        var ok = InvokeExport(
          scriptName,
          s_onCommand,
          stateRef,
          extras,
          1,
          scriptName
        );

        QJS.JS_FreeValue(m_Context, extras[0]);
        return ok;
      }
    }

    /// <summary>
    /// Calls globalThis.__tickComponents(group, dt) — one call per tick group per frame.
    /// </summary>
    public unsafe void TickComponents(string group, float dt)
    {
      TickComponents(GetOrCacheBytes(group), dt);
    }

    /// <summary>
    /// Ticks components using a pre-encoded group name. Zero-allocation hot path.
    /// </summary>
    public unsafe void TickComponents(byte[] groupBytes, float dt)
    {
      var global = QJS.JS_GetGlobalObject(m_Context);
      fixed (byte* pName = s_tickComponents)
      {
        var func = QJS.JS_GetPropertyStr(m_Context, global, pName);
        if (QJS.JS_IsFunction(m_Context, func) != 0)
        {
          fixed (byte* pGroup = groupBytes)
          {
            var argv = stackalloc JSValue[2];
            argv[0] = QJS.JS_NewString(m_Context, pGroup);
            argv[1] = QJS.NewFloat64(m_Context, dt);

            var result = QJS.JS_Call(m_Context, func, global, 2, argv);
            if (QJS.IsException(result))
              LogException("TickComponents");

            QJS.JS_FreeValue(m_Context, result);
            QJS.JS_FreeValue(m_Context, argv[0]);
          }
        }

        QJS.JS_FreeValue(m_Context, func);
      }

      QJS.JS_FreeValue(m_Context, global);
    }

    /// <summary>Pre-cached group byte arrays for zero-allocation TickComponents calls.</summary>
    public static byte[] GroupUpdateBytes => s_groupUpdate;
    public static byte[] GroupFixedUpdateBytes => s_groupFixedUpdate;
    public static byte[] GroupLateUpdateBytes => s_groupLateUpdate;
    public static byte[] OnUpdateBytes => s_onUpdate;
    public static byte[] OnDestroyBytes => s_onDestroy;

    /// <summary>
    /// Calls globalThis.__flushRefRw() — writes back any pending RefRW component data.
    /// </summary>
    public unsafe void FlushRefRw()
    {
      var global = QJS.JS_GetGlobalObject(m_Context);
      fixed (byte* pName = s_flushRefRw)
      {
        var func = QJS.JS_GetPropertyStr(m_Context, global, pName);
        if (QJS.JS_IsFunction(m_Context, func) != 0)
        {
          var result = QJS.JS_Call(m_Context, func, global, 0, null);
          if (QJS.IsException(result))
            LogException("FlushRefRw");
          QJS.JS_FreeValue(m_Context, result);
        }

        QJS.JS_FreeValue(m_Context, func);
      }

      QJS.JS_FreeValue(m_Context, global);
    }

    /// <summary>
    /// Calls globalThis.__componentInit(scriptName, entityId, propsJson).
    /// Returns true if the module's default export was a Component class (handled).
    /// </summary>
    public unsafe bool TryComponentInit(
      string scriptName,
      int entityId,
      string propertiesJson = null
    )
    {
      var global = QJS.JS_GetGlobalObject(m_Context);
      var handled = false;

      fixed (byte* pName = s_componentInit)
      {
        var func = QJS.JS_GetPropertyStr(m_Context, global, pName);
        if (QJS.JS_IsFunction(m_Context, func) != 0)
        {
          var scriptBytes = GetOrCacheBytes(scriptName);
          fixed (byte* pScript = scriptBytes)
          {
            var argc = propertiesJson != null ? 3 : 2;
            var argv = stackalloc JSValue[3];
            argv[0] = QJS.JS_NewString(m_Context, pScript);
            argv[1] = QJS.NewInt32(m_Context, entityId);

            if (propertiesJson != null)
            {
              var propsBytes = GetOrCacheBytes(propertiesJson);
              fixed (byte* pProps = propsBytes)
                argv[2] = QJS.JS_NewString(m_Context, pProps);
            }
            else
            {
              argv[2] = QJS.JS_UNDEFINED;
            }

            var result = QJS.JS_Call(m_Context, func, global, argc, argv);
            if (QJS.IsException(result))
              LogException(scriptName);
            else
              handled = QJS.JS_ToBool(m_Context, result) != 0;

            QJS.JS_FreeValue(m_Context, result);
            QJS.JS_FreeValue(m_Context, argv[0]);
            if (propertiesJson != null)
              QJS.JS_FreeValue(m_Context, argv[2]);
          }
        }

        QJS.JS_FreeValue(m_Context, func);
      }

      QJS.JS_FreeValue(m_Context, global);
      return handled;
    }

    public unsafe string VerifyModuleHealth()
    {
      var global = QJS.JS_GetGlobalObject(m_Context);
      JSValue checkFn;
      fixed (byte* p = s_verifyModuleExports)
        checkFn = QJS.JS_GetPropertyStr(m_Context, global, p);

      if (QJS.JS_IsFunction(m_Context, checkFn) == 0)
      {
        QJS.JS_FreeValue(m_Context, checkFn);
        QJS.JS_FreeValue(m_Context, global);
        return null;
      }

      foreach (var (scriptId, ns) in m_ScriptRefs)
      {
        if (scriptId.StartsWith("__"))
          continue;
        var argv = stackalloc JSValue[1];
        argv[0] = QJS.JS_DupValue(m_Context, ns);
        var result = QJS.JS_Call(m_Context, checkFn, global, 1, argv);
        QJS.JS_FreeValue(m_Context, argv[0]);

        if (QJS.IsException(result))
        {
          LogException($"VerifyModuleHealth({scriptId})");
          QJS.JS_FreeValue(m_Context, result);
          QJS.JS_FreeValue(m_Context, checkFn);
          QJS.JS_FreeValue(m_Context, global);
          return $"Exception verifying '{scriptId}'";
        }

        if (result != QJS.JS_NULL && result != QJS.JS_UNDEFINED)
        {
          var err = QJS.ToManagedString(m_Context, result);
          QJS.JS_FreeValue(m_Context, result);
          QJS.JS_FreeValue(m_Context, checkFn);
          QJS.JS_FreeValue(m_Context, global);
          return $"TDZ in '{scriptId}': {err}";
        }
        QJS.JS_FreeValue(m_Context, result);
      }

      QJS.JS_FreeValue(m_Context, checkFn);
      QJS.JS_FreeValue(m_Context, global);
      return null;
    }

    /// <summary>
    /// Reloads a script by freeing the old ref and re-evaluating with a versioned
    /// filename. QuickJS caches modules by filename — appending ?v=N ensures the
    /// re-evaluated module gets a fresh cache entry instead of resolving to the stale one.
    /// </summary>
    public unsafe bool ReloadScript(string scriptName, string source, string filename)
    {
      if (m_ScriptRefs.TryGetValue(scriptName, out var old))
      {
        QJS.JS_FreeValue(m_Context, old);
        m_ScriptRefs.Remove(scriptName);
      }

      m_ReloadVersions.TryGetValue(filename, out var version);
      version++;
      m_ReloadVersions[filename] = version;
      var versionedFilename = filename + "?v=" + version;

      return LoadScriptAsModule(scriptName, source, versionedFilename);
    }

    /// <summary>
    /// Simulates a hot reload for a script, following the same path as
    /// JsHotReloadSystem: re-read source from disk, call ReloadScript().
    /// </summary>
    public bool SimulateHotReload(string scriptName)
    {
      if (!JsScriptSourceRegistry.TryReadScript(scriptName, out var source, out var resolvedId))
      {
        Log.Error("[JsRuntime] SimulateHotReload: script not found: {0}", scriptName);
        return false;
      }

      return ReloadScript(scriptName, source, resolvedId);
    }

    public void RegisterBridgeNow(Action<JSContext> registration)
    {
      if (!IsValid)
      {
        Log.Error("[JsRuntime] Cannot register bridge — context is not valid");
        return;
      }

      registration(m_Context);
    }

    public void Dispose()
    {
      BridgeState?.Dispose();

      m_StringCache.Clear();
      m_StringCache32.Clear();
      m_EncodedStringCache.Clear();

      foreach (var kv in m_StateRefs)
        QJS.JS_FreeValue(m_Context, kv.Value);
      m_StateRefs.Clear();

      foreach (var kv in m_ScriptRefs)
        QJS.JS_FreeValue(m_Context, kv.Value);
      m_ScriptRefs.Clear();

      JsBuiltinModules.ClearCache();

      if (!m_Context.IsNull)
      {
        JsStateExtensions.ClearVectorPrototypes(m_Context);
        QJS.JS_FreeContext(m_Context);
        m_Context = default;
      }

      if (!m_Runtime.IsNull)
      {
        QJS.JS_FreeRuntime(m_Runtime);
        m_Runtime = default;
      }

      if (s_Instance == this)
        s_Instance = null;

      s_shimDirty = true;
    }

    static readonly byte[] s_stackProp = QJS.U8("stack");

    unsafe void LogException(string context)
    {
      var exc = QJS.JS_GetException(m_Context);
      var msg = QJS.ToManagedString(m_Context, exc);

      string stack = null;
      fixed (byte* pStack = s_stackProp)
        stack = QJS.GetStringProperty(m_Context, exc, pStack);

      QJS.JS_FreeValue(m_Context, exc);

      m_CapturedExceptions.Add($"{context}: {msg}");

      if (!string.IsNullOrEmpty(stack))
        UnityEngine.Debug.LogError($"[JsRuntime] Exception in {context}: {msg}\n{stack}");
      else
        UnityEngine.Debug.LogError($"[JsRuntime] Exception in {context}: {msg}");
    }
  }
}
