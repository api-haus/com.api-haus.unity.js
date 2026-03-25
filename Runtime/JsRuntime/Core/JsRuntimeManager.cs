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
  /// Delegates module management to JsModuleManager and entity state to JsEntityStateStore.
  /// </summary>
  public class JsRuntimeManager : IDisposable
  {
    static bool s_shimDirty;

    static JsRuntimeManager s_Instance;
    static int s_InstanceVersion;

    public static JsRuntimeManager Instance => s_Instance;
    public static int InstanceVersion => s_InstanceVersion;

    public static void RestoreInstance(JsRuntimeManager vm)
    {
      if (vm != null && vm.IsValid)
        s_Instance = vm;
    }

    JSRuntime m_Runtime;
    JSContext m_Context;

    readonly List<string> m_CapturedExceptions = new();
    public IReadOnlyList<string> CapturedExceptions => m_CapturedExceptions;
    public void ClearCapturedExceptions() => m_CapturedExceptions.Clear();

    readonly Dictionary<FixedString64Bytes, string> m_StringCache = new();
    readonly Dictionary<FixedString32Bytes, string> m_StringCache32 = new();
    readonly Dictionary<string, byte[]> m_EncodedStringCache = new();

    // ── Sub-objects ──

    public JsModuleManager Modules { get; }
    public JsEntityStateStore States { get; }

    // ── String interning ──

    public string Intern(in FixedString64Bytes fs)
    {
      if (m_StringCache.TryGetValue(fs, out var s))
        return s;
      s = fs.ToString();
      m_StringCache[fs] = s;
      return s;
    }

    public string Intern(in FixedString32Bytes fs)
    {
      if (m_StringCache32.TryGetValue(fs, out var s))
        return s;
      s = fs.ToString();
      m_StringCache32[fs] = s;
      return s;
    }

    // ── Properties ──

    public JSRuntime Runtime => m_Runtime;
    public JSContext Context => m_Context;
    public bool IsValid => !m_Context.IsNull;
    public IDisposable BridgeState { get; set; }

    // ── Pre-cached byte arrays ──

    static readonly byte[] s_onInit = QJS.U8("onInit");
    static readonly byte[] s_onTick = QJS.U8("onTick");
    static readonly byte[] s_onEvent = QJS.U8("onEvent");
    static readonly byte[] s_onCommand = QJS.U8("onCommand");
    static readonly byte[] s_onUpdate = QJS.U8("onUpdate");
    static readonly byte[] s_onDestroy = QJS.U8("onDestroy");
    static readonly byte[] s_tickComponents = QJS.U8("__tickComponents");
    static readonly byte[] s_flushRefRw = QJS.U8("__flushRefRw");
    static readonly byte[] s_componentInit = QJS.U8("__componentInit");
    static readonly byte[] s_groupUpdate = QJS.U8("update");
    static readonly byte[] s_groupFixedUpdate = QJS.U8("fixedUpdate");
    static readonly byte[] s_groupLateUpdate = QJS.U8("lateUpdate");

    public static byte[] GroupUpdateBytes => s_groupUpdate;
    public static byte[] GroupFixedUpdateBytes => s_groupFixedUpdate;
    public static byte[] GroupLateUpdateBytes => s_groupLateUpdate;
    public static byte[] OnUpdateBytes => s_onUpdate;
    public static byte[] OnDestroyBytes => s_onDestroy;

    // ── Constructor ──

    public JsRuntimeManager(string basePath = null)
    {
      if (s_shimDirty)
      {
        QJSShim.qjs_shim_reset();
        s_shimDirty = false;
      }

      m_Runtime = QJS.JS_NewRuntime();
      m_Context = QJS.JS_NewContext(m_Runtime);

      Modules = new JsModuleManager(this);
      States = new JsEntityStateStore(this);

      if (!string.IsNullOrEmpty(basePath))
        JsScriptSearchPaths.AddSearchPath(basePath, 0);

      JsModuleLoader.Install(m_Context);
      JsTranspiler.Initialize(m_Context);

      s_InstanceVersion++;
      s_Instance = this;
    }

    public static JsRuntimeManager GetOrCreate(string basePath = null)
    {
      if (s_Instance != null && s_Instance.IsValid)
        return s_Instance;

      return new JsRuntimeManager(basePath);
    }

    // ── Forwarding methods (delegates to sub-objects) ──

    public bool HasScript(string scriptId) => Modules.Has(scriptId);

    public unsafe bool SetLastLoadedModule(string scriptId) => Modules.SetLastLoaded(scriptId);

    public unsafe void ComponentReload(string scriptName) => Modules.ComponentReload(scriptName);

    public bool LoadScript(string scriptName) => Modules.Load(scriptName);

    public bool LoadScript(JsScriptLoadResult result) => Modules.Load(result);

    public bool LoadScriptFromString(string scriptId, string source) =>
      Modules.LoadFromString(scriptId, source);

    public unsafe bool LoadScriptAsModule(string scriptId, string source, string filename) =>
      Modules.LoadAsModule(scriptId, source, filename);

    public unsafe bool ReloadScript(string scriptName, string source, string filename) =>
      Modules.Reload(scriptName, source, filename);

    public bool SimulateHotReload(string scriptName) => Modules.SimulateHotReload(scriptName);

    public unsafe string VerifyModuleHealth() => Modules.VerifyHealth();

    public unsafe int CreateEntityState(string scriptName, int entityId) =>
      States.Create(scriptName, entityId);

    public void ReleaseEntityState(int stateRef) => States.Release(stateRef);

    public bool ValidateStateRef(int stateRef) => States.Validate(stateRef);

    public unsafe void UpdateStateTimings(int stateRef, float deltaTime, double elapsedTime) =>
      States.UpdateTimings(stateRef, deltaTime, elapsedTime);

    // ── Invocation ──

    unsafe bool InvokeExport(
      string scriptName,
      byte[] funcNameBytes,
      int stateRef,
      JSValue* extraArgv,
      int extraArgc,
      string errorContext
    )
    {
      if (!Modules.ScriptRefs.TryGetValue(scriptName, out var scriptObj))
        return false;

      fixed (byte* pFuncName = funcNameBytes)
      {
        var func = QJS.JS_GetPropertyStr(m_Context, scriptObj, pFuncName);
        if (QJS.JS_IsFunction(m_Context, func) == 0)
        {
          QJS.JS_FreeValue(m_Context, func);
          return true;
        }

        if (!States.Refs.TryGetValue(stateRef, out var stateVal))
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

    public unsafe bool CallFunction(string scriptName, string funcName, int stateRef)
    {
      return InvokeExport(scriptName, GetOrCacheBytes(funcName), stateRef, null, 0, scriptName);
    }

    public unsafe bool CallFunction(string scriptName, byte[] funcNameBytes, int stateRef)
    {
      return InvokeExport(scriptName, funcNameBytes, stateRef, null, 0, scriptName);
    }

    public bool CallInit(string scriptName, int stateRef)
    {
      return CallFunction(scriptName, "onInit", stateRef);
    }

    public unsafe bool CallTick(
      string scriptName,
      int stateRef,
      float deltaTime,
      double elapsedTime = 0.0
    )
    {
      States.UpdateTimings(stateRef, deltaTime, elapsedTime);
      return InvokeExport(scriptName, s_onTick, stateRef, null, 0, scriptName);
    }

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

        var ok = InvokeExport(scriptName, s_onEvent, stateRef, extras, 4, scriptName);
        QJS.JS_FreeValue(m_Context, extras[0]);
        return ok;
      }
    }

    public unsafe bool CallCommand(string scriptName, int stateRef, string command)
    {
      var cmdBytes = GetOrCacheBytes(command);
      fixed (byte* pCmd = cmdBytes)
      {
        var extras = stackalloc JSValue[1];
        extras[0] = QJS.JS_NewString(m_Context, pCmd);
        var ok = InvokeExport(scriptName, s_onCommand, stateRef, extras, 1, scriptName);
        QJS.JS_FreeValue(m_Context, extras[0]);
        return ok;
      }
    }

    // ── Component ticking ──

    public unsafe void TickComponents(string group, float dt)
    {
      TickComponents(GetOrCacheBytes(group), dt);
    }

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

    // ── Registration ──

    public void RegisterBridgeNow(Action<JSContext> registration)
    {
      if (!IsValid)
      {
        Log.Error("[JsRuntime] Cannot register bridge — context is not valid");
        return;
      }

      registration(m_Context);
    }

    // ── Utilities ──

    internal byte[] GetOrCacheBytes(string value)
    {
      if (m_EncodedStringCache.TryGetValue(value, out var bytes))
        return bytes;
      bytes = QJS.U8(value);
      m_EncodedStringCache[value] = bytes;
      return bytes;
    }

    // ── Eval (used by tests) ──

    public unsafe JSValue EvalGlobal(JSContext ctx, string code, string filename)
    {
      return QJS.EvalGlobal(ctx, code, filename);
    }

    // ── Dispose ──

    public void Dispose()
    {
      BridgeState?.Dispose();

      m_StringCache.Clear();
      m_StringCache32.Clear();
      m_EncodedStringCache.Clear();

      States.DisposeAll();
      Modules.DisposeAll();

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

    // ── Exception handling ──

    static readonly byte[] s_stackProp = QJS.U8("stack");

    internal unsafe void LogException(string context)
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
