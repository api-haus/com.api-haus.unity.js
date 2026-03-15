namespace UnityJS.Runtime
{
  using System;
  using System.Collections.Generic;
  using System.Runtime.InteropServices;
  using System.Text;
  using QJS;
  using UnityEngine;

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
        Debug.LogError($"[JsRuntime] Failed to find script '{scriptName}': {result.error}");
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
        Debug.LogError($"[JsRuntime] Failed to read source for '{result.scriptId}'");
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
        return true;
      }
    }

    /// <summary>
    /// Creates a JS state object for an entity script instance.
    /// Returns a monotonic int key into the state dictionary.
    /// </summary>
    static byte[] U8(string s)
    {
      return Encoding.UTF8.GetBytes(s + '\0');
    }

    static readonly byte[] s_script = U8("_script");
    static readonly byte[] s_entityId = U8("entityId");
    static readonly byte[] s_deltaTime = U8("deltaTime");
    static readonly byte[] s_elapsedTime = U8("elapsedTime");
    static readonly byte[] s_onInit = U8("onInit");
    static readonly byte[] s_onTick = U8("onTick");
    static readonly byte[] s_onEvent = U8("onEvent");
    static readonly byte[] s_onCommand = U8("onCommand");

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
    /// Reloads a script by freeing the old ref and re-evaluating.
    /// </summary>
    public unsafe bool ReloadScript(string scriptName, string source, string filename)
    {
      if (m_ScriptRefs.TryGetValue(scriptName, out var old))
      {
        QJS.JS_FreeValue(m_Context, old);
        m_ScriptRefs.Remove(scriptName);
      }

      return LoadScriptAsModule(scriptName, source, filename);
    }

    public void RegisterBridgeNow(Action<JSContext> registration)
    {
      if (!IsValid)
      {
        Debug.LogError("[JsRuntime] Cannot register bridge — context is not valid");
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

    unsafe void LogException(string context)
    {
      var exc = QJS.JS_GetException(m_Context);
      var ptr = QJS.JS_ToCString(m_Context, exc);
      var msg = Marshal.PtrToStringUTF8((nint)ptr);
      QJS.JS_FreeCString(m_Context, ptr);
      QJS.JS_FreeValue(m_Context, exc);
      Debug.LogError($"[JsRuntime] Exception in {context}: {msg}");
    }
  }
}
