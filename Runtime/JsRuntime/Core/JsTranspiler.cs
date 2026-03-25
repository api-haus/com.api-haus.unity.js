namespace UnityJS.Runtime
{
  using QJS;
  using UnityEngine;

  public static unsafe class JsTranspiler
  {
    static readonly byte[] s_transpileFn = QJS.U8("__transpileTS");

    public static bool IsInitialized { get; private set; }
    public static string LastError { get; private set; }
    public static int ErrorCount { get; private set; }
    public static int SuccessCount { get; private set; }

    public static void Initialize(JSContext ctx)
    {
      IsInitialized = false;
      ErrorCount = 0;
      SuccessCount = 0;
      LastError = null;

      var asset = Resources.Load<TextAsset>("sucrase.bundle.js");
      if (asset == null)
      {
        Debug.LogError("[JsTranspiler] Failed to load sucrase.bundle.js from Resources");
        return;
      }

      var result = QJS.EvalGlobal(ctx, asset.text, "sucrase.bundle.js");
      if (QJS.IsException(result))
      {
        Debug.LogError($"[JsTranspiler] Sucrase bundle eval failed: {QJS.GetExceptionMessage(ctx)}");
        return;
      }
      QJS.JS_FreeValue(ctx, result);

      const string wrapper = @"
globalThis.__transpileTS = function(source) {
  return sucrase.transform(source, {
    transforms: ['typescript'],
    disableESTransforms: true
  }).code;
};";

      result = QJS.EvalGlobal(ctx, wrapper, "sucrase-wrapper.js");
      if (QJS.IsException(result))
      {
        Debug.LogError($"[JsTranspiler] Wrapper eval failed: {QJS.GetExceptionMessage(ctx)}");
        return;
      }
      QJS.JS_FreeValue(ctx, result);

      IsInitialized = true;
      Debug.Log("[JsTranspiler] Sucrase initialized");
    }

    public static string Transpile(JSContext ctx, string tsSource)
    {
      if (!IsInitialized)
      {
        Debug.LogError("[JsTranspiler] Not initialized — cannot transpile");
        return null;
      }

      var global = QJS.JS_GetGlobalObject(ctx);

      fixed (byte* pName = s_transpileFn)
      {
        var func = QJS.JS_GetPropertyStr(ctx, global, pName);
        if (QJS.JS_IsFunction(ctx, func) == 0)
        {
          QJS.JS_FreeValue(ctx, func);
          QJS.JS_FreeValue(ctx, global);
          Debug.LogError("[JsTranspiler] __transpileTS is not a function");
          return null;
        }

        var sourceBytes = QJS.U8(tsSource);
        fixed (byte* pSource = sourceBytes)
        {
          var argv = stackalloc JSValue[1];
          argv[0] = QJS.JS_NewString(ctx, pSource);

          var result = QJS.JS_Call(ctx, func, global, 1, argv);
          QJS.JS_FreeValue(ctx, argv[0]);

          if (QJS.IsException(result))
          {
            var ex = QJS.JS_GetException(ctx);
            LastError = QJS.ToManagedString(ctx, ex);
            QJS.JS_FreeValue(ctx, ex);
            ErrorCount++;

            Debug.LogError($"[JsTranspiler] Transpilation failed: {LastError}");

            QJS.JS_FreeValue(ctx, result);
            QJS.JS_FreeValue(ctx, func);
            QJS.JS_FreeValue(ctx, global);
            return null;
          }

          var jsSource = QJS.ToManagedString(ctx, result);
          QJS.JS_FreeValue(ctx, result);
          QJS.JS_FreeValue(ctx, func);
          QJS.JS_FreeValue(ctx, global);

          SuccessCount++;
          return jsSource;
        }
      }
    }
  }
}
