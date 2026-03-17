namespace UnityJS.Entities.Core
{
  using AOT;
  using QJS;
  using Unity.Logging;
  using static Runtime.QJSHelpers;

  /// <summary>
  /// Bridge functions for logging.
  /// JS API: log.debug(), log.info(), log.warning(), log.error(), log.trace()
  /// Implemented via _log_internal.dispatch + JS helper.
  /// </summary>
  static partial class JsECSBridge
  {
    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void Log_Dispatch(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      var level = ArgString(ctx, argv, 0) ?? "info";
      var message = ArgString(ctx, argv, 1) ?? "";
      var stackTrace = argc >= 3 ? ArgString(ctx, argv, 2) ?? "" : "";

      var formatted = FormatJsStackTrace(stackTrace);

      switch (level)
      {
        case "debug":
          Log.Debug("[JS] {0}\n{1}", message, formatted);
          break;
        case "warning":
          Log.Warning("[JS] {0}\n{1}", message, formatted);
          break;
        case "error":
          Log.Error("[JS] {0}\n{1}", message, formatted);
          break;
        case "trace":
          Log.Error("[JS TRACE] {0}\n{1}", message, formatted);
          break;
        default:
          Log.Debug("[JS] {0}\n{1}", message, formatted);
          break;
      }

      SetUndefined(outU, outTag);
    }

    static string FormatJsStackTrace(string stackTrace)
    {
      return stackTrace.Replace("\\n", "\n").Replace("\\t", "\t");
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void Log_Traceback(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      // Stack trace is captured JS-side via new Error().stack
      SetUndefined(outU, outTag);
    }

    static unsafe void RegisterLogFunctions(JSContext ctx)
    {
      // Register _log_internal namespace with dispatch and traceback
      var ns = QJS.JS_NewObject(ctx);

      AddFunction(ctx, ns, "dispatch", Log_Dispatch, 3);
      AddFunction(ctx, ns, "traceback", Log_Traceback, 0);

      var global = QJS.JS_GetGlobalObject(ctx);
      SetNamespace(ctx, global, "_log_internal", ns);

      // Bootstrap JS-side log.* wrappers
      const string logBootstrap =
        @"(function() {
  function formatMsg(msg) { return String(msg); }
  function makeLogger(level) {
    return function(msg) {
      var stack = new Error().stack || '';
      _log_internal.dispatch(level, formatMsg(msg), stack);
    };
  }
  globalThis.log = {
    debug: makeLogger('debug'),
    info: makeLogger('info'),
    warning: makeLogger('warning'),
    error: makeLogger('error'),
    trace: makeLogger('trace'),
  };
})();";

      var result = QJS.EvalGlobal(ctx, logBootstrap, "<log_bootstrap>");
      if (QJS.IsException(result))
        Log.Error(
          "[JsECS] Failed to initialize JS log helpers: {0}",
          QJS.GetExceptionMessage(ctx)
        );

      QJS.JS_FreeValue(ctx, result);

      QJS.JS_FreeValue(ctx, global);
    }
  }
}
