namespace UnityJS.Runtime
{
  using System;
  using System.Collections.Generic;
  using QJS;
  using UnityEngine;

  public static unsafe class JsTranspiler
  {
    static readonly byte[] s_transpileFn = QJS.U8("__transpileTS");
    static JSRuntime s_rt;
    static JSContext s_ctx;
    static readonly Dictionary<string, string> s_errors = new();

    public static bool IsInitialized { get; private set; }
    public static string LastError { get; private set; }
    public static int ErrorCount => s_errors.Count;
    public static int SuccessCount { get; private set; }
    public static IReadOnlyDictionary<string, string> Errors => s_errors;

    public static event Action OnStateChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void DomainReloadCleanup()
    {
      Dispose();
    }

    public static void EnsureInitialized()
    {
      if (IsInitialized)
        return;

      var asset = Resources.Load<TextAsset>("sucrase.bundle.js");
      if (asset == null)
      {
        Debug.LogError("[JsTranspiler] Failed to load sucrase.bundle.js from Resources");
        return;
      }

      s_rt = QJS.JS_NewRuntime();
      s_ctx = QJS.JS_NewContext(s_rt);

      var result = QJS.EvalGlobal(s_ctx, asset.text, "sucrase.bundle.js");
      if (QJS.IsException(result))
      {
        Debug.LogError($"[JsTranspiler] Sucrase bundle eval failed: {QJS.GetExceptionMessage(s_ctx)}");
        Dispose();
        return;
      }
      QJS.JS_FreeValue(s_ctx, result);

      const string wrapper = @"
globalThis.__transpileTS = function(source) {
  return sucrase.transform(source, {
    transforms: ['typescript'],
    disableESTransforms: true
  }).code;
};";

      result = QJS.EvalGlobal(s_ctx, wrapper, "sucrase-wrapper.js");
      if (QJS.IsException(result))
      {
        Debug.LogError($"[JsTranspiler] Wrapper eval failed: {QJS.GetExceptionMessage(s_ctx)}");
        Dispose();
        return;
      }
      QJS.JS_FreeValue(s_ctx, result);

      IsInitialized = true;
      Debug.Log("[JsTranspiler] Sucrase initialized");
    }

    public static string Transpile(string tsSource, string filePath)
    {
      EnsureInitialized();
      if (!IsInitialized)
        return null;

      var errCountBefore = s_errors.Count;
      var global = QJS.JS_GetGlobalObject(s_ctx);

      fixed (byte* pName = s_transpileFn)
      {
        var func = QJS.JS_GetPropertyStr(s_ctx, global, pName);
        if (QJS.JS_IsFunction(s_ctx, func) == 0)
        {
          QJS.JS_FreeValue(s_ctx, func);
          QJS.JS_FreeValue(s_ctx, global);
          Debug.LogError("[JsTranspiler] __transpileTS is not a function");
          return null;
        }

        var sourceBytes = QJS.U8(tsSource);
        fixed (byte* pSource = sourceBytes)
        {
          var argv = stackalloc JSValue[1];
          argv[0] = QJS.JS_NewString(s_ctx, pSource);

          var result = QJS.JS_Call(s_ctx, func, global, 1, argv);
          QJS.JS_FreeValue(s_ctx, argv[0]);

          if (QJS.IsException(result))
          {
            var ex = QJS.JS_GetException(s_ctx);
            var errorMsg = QJS.ToManagedString(s_ctx, ex);
            QJS.JS_FreeValue(s_ctx, ex);

            LastError = errorMsg;
            s_errors[filePath] = errorMsg;
            Debug.LogError($"[JsTranspiler] Transpilation failed ({filePath}): {errorMsg}");

            QJS.JS_FreeValue(s_ctx, result);
            QJS.JS_FreeValue(s_ctx, func);
            QJS.JS_FreeValue(s_ctx, global);

            if (s_errors.Count != errCountBefore)
              OnStateChanged?.Invoke();
            return null;
          }

          var jsSource = QJS.ToManagedString(s_ctx, result);
          QJS.JS_FreeValue(s_ctx, result);
          QJS.JS_FreeValue(s_ctx, func);
          QJS.JS_FreeValue(s_ctx, global);

          var hadError = s_errors.Remove(filePath);
          SuccessCount++;

          if (hadError)
            OnStateChanged?.Invoke();
          return jsSource;
        }
      }
    }

    public static void Dispose()
    {
      IsInitialized = false;
      SuccessCount = 0;
      LastError = null;
      s_errors.Clear();

      if (!s_ctx.IsNull)
      {
        QJS.JS_FreeContext(s_ctx);
        s_ctx = default;
      }
      if (!s_rt.IsNull)
      {
        QJS.JS_FreeRuntime(s_rt);
        s_rt = default;
      }
    }
  }
}
