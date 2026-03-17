namespace UnityJS.Runtime
{
  using System;
  using System.Collections.Generic;
  using System.Runtime.InteropServices;
  using System.Text;
  using QJS;
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
    public static JsRuntimeManager Instance => s_Instance;

    JSRuntime m_Runtime;
    JSContext m_Context;
    readonly Dictionary<string, JSValue> m_ScriptRefs = new();
    readonly Dictionary<string, int> m_ReloadVersions = new();

    int m_NextStateId = 1;
    readonly Dictionary<int, JSValue> m_StateRefs = new();

    public JSRuntime Runtime => m_Runtime;
    public JSContext Context => m_Context;
    public bool IsValid => !m_Context.IsNull;

    /// <summary>
    /// Returns true if a script module with this ID is already loaded.
    /// </summary>
    public bool HasScript(string scriptId)
    {
      return m_ScriptRefs.ContainsKey(scriptId);
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
    static readonly byte[] s_tickComponents = QJS.U8("__tickComponents");
    static readonly byte[] s_flushRefRw = QJS.U8("__flushRefRw");
    static readonly byte[] s_componentInit = QJS.U8("__componentInit");
    static readonly byte[] s_lastLoadedModule = QJS.U8("__lastLoadedModule");

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

        var nameBytes = Encoding.UTF8.GetBytes(scriptName + '\0');
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
    /// </summary>
    public unsafe bool CallFunction(string scriptName, string funcName, int stateRef)
    {
      var funcNameBytes = Encoding.UTF8.GetBytes(funcName + '\0');
      return InvokeExport(
        scriptName,
        funcNameBytes,
        stateRef,
        null,
        0,
        $"CallFunction({scriptName}.{funcName})"
      );
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
      return InvokeExport(scriptName, s_onTick, stateRef, null, 0, $"CallTick({scriptName})");
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
      var eventNameBytes = Encoding.UTF8.GetBytes(eventName + '\0');
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
          $"CallEvent({scriptName}, {eventName})"
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
      var cmdBytes = Encoding.UTF8.GetBytes(command + '\0');
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
          $"CallCommand({scriptName}, {command})"
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
      var global = QJS.JS_GetGlobalObject(m_Context);
      fixed (byte* pName = s_tickComponents)
      {
        var func = QJS.JS_GetPropertyStr(m_Context, global, pName);
        if (QJS.JS_IsFunction(m_Context, func) != 0)
        {
          var groupBytes = Encoding.UTF8.GetBytes(group + '\0');
          fixed (byte* pGroup = groupBytes)
          {
            var argv = stackalloc JSValue[2];
            argv[0] = QJS.JS_NewString(m_Context, pGroup);
            argv[1] = QJS.NewFloat64(m_Context, dt);

            var result = QJS.JS_Call(m_Context, func, global, 2, argv);
            if (QJS.IsException(result))
              LogException($"TickComponents({group})");

            QJS.JS_FreeValue(m_Context, result);
            QJS.JS_FreeValue(m_Context, argv[0]);
          }
        }

        QJS.JS_FreeValue(m_Context, func);
      }

      QJS.JS_FreeValue(m_Context, global);
    }

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
    /// Calls globalThis.__componentInit(scriptName, entityId).
    /// Returns true if the module's default export was a Component class (handled).
    /// </summary>
    public unsafe bool TryComponentInit(string scriptName, int entityId)
    {
      var global = QJS.JS_GetGlobalObject(m_Context);
      var handled = false;

      fixed (byte* pName = s_componentInit)
      {
        var func = QJS.JS_GetPropertyStr(m_Context, global, pName);
        if (QJS.JS_IsFunction(m_Context, func) != 0)
        {
          var scriptBytes = Encoding.UTF8.GetBytes(scriptName + '\0');
          fixed (byte* pScript = scriptBytes)
          {
            var argv = stackalloc JSValue[2];
            argv[0] = QJS.JS_NewString(m_Context, pScript);
            argv[1] = QJS.NewInt32(m_Context, entityId);

            var result = QJS.JS_Call(m_Context, func, global, 2, argv);
            if (QJS.IsException(result))
              LogException($"TryComponentInit({scriptName})");
            else
              handled = QJS.JS_ToBool(m_Context, result) != 0;

            QJS.JS_FreeValue(m_Context, result);
            QJS.JS_FreeValue(m_Context, argv[0]);
          }
        }

        QJS.JS_FreeValue(m_Context, func);
      }

      QJS.JS_FreeValue(m_Context, global);
      return handled;
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

      if (!string.IsNullOrEmpty(stack))
        Log.Error("[JsRuntime] Exception in {0}: {1}\n{2}", context, msg, stack);
      else
        Log.Error("[JsRuntime] Exception in {0}: {1}", context, msg);
    }
  }
}
