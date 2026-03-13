namespace UnityJS.Runtime
{
  using System;
  using System.Collections.Generic;
  using System.Runtime.InteropServices;
  using System.Text;
  using UnityEngine;
  using UnityJS.QJS;

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
    public bool HasScript(string scriptId) => m_ScriptRefs.ContainsKey(scriptId);

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
    static readonly byte[] s_script =
    {
      (byte)'_',
      (byte)'s',
      (byte)'c',
      (byte)'r',
      (byte)'i',
      (byte)'p',
      (byte)'t',
      0,
    };
    static readonly byte[] s_entityId =
    {
      (byte)'e',
      (byte)'n',
      (byte)'t',
      (byte)'i',
      (byte)'t',
      (byte)'y',
      (byte)'I',
      (byte)'d',
      0,
    };
    static readonly byte[] s_deltaTime =
    {
      (byte)'d',
      (byte)'e',
      (byte)'l',
      (byte)'t',
      (byte)'a',
      (byte)'T',
      (byte)'i',
      (byte)'m',
      (byte)'e',
      0,
    };
    static readonly byte[] s_elapsedTime =
    {
      (byte)'e',
      (byte)'l',
      (byte)'a',
      (byte)'p',
      (byte)'s',
      (byte)'e',
      (byte)'d',
      (byte)'T',
      (byte)'i',
      (byte)'m',
      (byte)'e',
      0,
    };
    static readonly byte[] s_onInit =
    {
      (byte)'o',
      (byte)'n',
      (byte)'I',
      (byte)'n',
      (byte)'i',
      (byte)'t',
      0,
    };
    static readonly byte[] s_onTick =
    {
      (byte)'o',
      (byte)'n',
      (byte)'T',
      (byte)'i',
      (byte)'c',
      (byte)'k',
      0,
    };
    static readonly byte[] s_onEvent =
    {
      (byte)'o',
      (byte)'n',
      (byte)'E',
      (byte)'v',
      (byte)'e',
      (byte)'n',
      (byte)'t',
      0,
    };
    static readonly byte[] s_onCommand =
    {
      (byte)'o',
      (byte)'n',
      (byte)'C',
      (byte)'o',
      (byte)'m',
      (byte)'m',
      (byte)'a',
      (byte)'n',
      (byte)'d',
      0,
    };

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
    /// Calls a named function on a script's module namespace object.
    /// </summary>
    public unsafe bool CallFunction(string scriptName, string funcName, int stateRef)
    {
      if (!m_ScriptRefs.TryGetValue(scriptName, out var scriptObj))
        return false;

      var funcNameBytes = Encoding.UTF8.GetBytes(funcName + '\0');
      fixed (byte* pFuncName = funcNameBytes)
      {
        var func = QJS.JS_GetPropertyStr(m_Context, scriptObj, pFuncName);
        if (QJS.JS_IsFunction(m_Context, func) == 0)
        {
          QJS.JS_FreeValue(m_Context, func);
          return true; // missing func is not an error
        }

        // Build argv: [stateRef JSValue]
        if (!m_StateRefs.TryGetValue(stateRef, out var stateVal))
          stateVal = QJS.JS_UNDEFINED;

        var argv = stackalloc JSValue[1];
        argv[0] = stateVal;

        var result = QJS.JS_Call(m_Context, func, scriptObj, 1, argv);
        if (QJS.IsException(result))
        {
          LogException($"CallFunction({scriptName}.{funcName})");
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
      if (!m_ScriptRefs.TryGetValue(scriptName, out var scriptObj))
        return false;

      fixed (byte* pFuncName = s_onTick)
      {
        var func = QJS.JS_GetPropertyStr(m_Context, scriptObj, pFuncName);
        if (QJS.JS_IsFunction(m_Context, func) == 0)
        {
          QJS.JS_FreeValue(m_Context, func);
          return true;
        }

        if (!m_StateRefs.TryGetValue(stateRef, out var stateVal))
          stateVal = QJS.JS_UNDEFINED;

        // Update deltaTime / elapsedTime in-place on the persistent state
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

        var argv = stackalloc JSValue[1];
        argv[0] = stateVal;

        var result = QJS.JS_Call(m_Context, func, scriptObj, 1, argv);
        if (QJS.IsException(result))
        {
          LogException($"CallTick({scriptName})");
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
      if (!m_ScriptRefs.TryGetValue(scriptName, out var scriptObj))
        return false;

      fixed (byte* pFuncName = s_onEvent)
      {
        var func = QJS.JS_GetPropertyStr(m_Context, scriptObj, pFuncName);
        if (QJS.JS_IsFunction(m_Context, func) == 0)
        {
          QJS.JS_FreeValue(m_Context, func);
          return true;
        }

        if (!m_StateRefs.TryGetValue(stateRef, out var stateVal))
          stateVal = QJS.JS_UNDEFINED;

        var eventNameBytes = Encoding.UTF8.GetBytes(eventName + '\0');
        fixed (byte* pEventName = eventNameBytes)
        {
          var argv = stackalloc JSValue[5];
          argv[0] = stateVal;
          argv[1] = QJS.JS_NewString(m_Context, pEventName);
          argv[2] = QJS.NewInt32(m_Context, sourceId);
          argv[3] = QJS.NewInt32(m_Context, targetId);
          argv[4] = QJS.NewInt32(m_Context, intParam);

          var result = QJS.JS_Call(m_Context, func, scriptObj, 5, argv);

          // Free the event name string we created
          QJS.JS_FreeValue(m_Context, argv[1]);

          if (QJS.IsException(result))
          {
            LogException($"CallEvent({scriptName}, {eventName})");
            QJS.JS_FreeValue(m_Context, result);
            QJS.JS_FreeValue(m_Context, func);
            return false;
          }

          QJS.JS_FreeValue(m_Context, result);
          QJS.JS_FreeValue(m_Context, func);
          return true;
        }
      }
    }

    /// <summary>
    /// Calls onCommand on the script.
    /// </summary>
    public unsafe bool CallCommand(string scriptName, int stateRef, string command)
    {
      if (!m_ScriptRefs.TryGetValue(scriptName, out var scriptObj))
        return false;

      fixed (byte* pFuncName = s_onCommand)
      {
        var func = QJS.JS_GetPropertyStr(m_Context, scriptObj, pFuncName);
        if (QJS.JS_IsFunction(m_Context, func) == 0)
        {
          QJS.JS_FreeValue(m_Context, func);
          return true;
        }

        if (!m_StateRefs.TryGetValue(stateRef, out var stateVal))
          stateVal = QJS.JS_UNDEFINED;

        var cmdBytes = Encoding.UTF8.GetBytes(command + '\0');
        fixed (byte* pCmd = cmdBytes)
        {
          var argv = stackalloc JSValue[2];
          argv[0] = stateVal;
          argv[1] = QJS.JS_NewString(m_Context, pCmd);

          var result = QJS.JS_Call(m_Context, func, scriptObj, 2, argv);

          QJS.JS_FreeValue(m_Context, argv[1]);

          if (QJS.IsException(result))
          {
            LogException($"CallCommand({scriptName}, {command})");
            QJS.JS_FreeValue(m_Context, result);
            QJS.JS_FreeValue(m_Context, func);
            return false;
          }

          QJS.JS_FreeValue(m_Context, result);
          QJS.JS_FreeValue(m_Context, func);
          return true;
        }
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
