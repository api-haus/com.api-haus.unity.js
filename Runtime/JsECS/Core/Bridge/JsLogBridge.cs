namespace UnityJS.Entities.Core
{
	using System.Runtime.InteropServices;
	using System.Text;
	using AOT;
	using UnityJS.QJS;
	using Unity.Logging;

	/// <summary>
	/// Bridge functions for logging.
	/// JS API: log.debug(), log.info(), log.warning(), log.error(), log.trace()
	/// Implemented via _log_internal.dispatch + JS helper.
	/// </summary>
	static partial class JsECSBridge
	{
		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Log_Dispatch(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			var pLevel = QJS.JS_ToCString(ctx, argv[0]);
			var pMessage = QJS.JS_ToCString(ctx, argv[1]);
			var pStack = argc >= 3 ? QJS.JS_ToCString(ctx, argv[2]) : null;

			var level = Marshal.PtrToStringUTF8((nint)pLevel) ?? "info";
			var message = Marshal.PtrToStringUTF8((nint)pMessage) ?? "";
			var stackTrace = pStack != null ? Marshal.PtrToStringUTF8((nint)pStack) ?? "" : "";

			QJS.JS_FreeCString(ctx, pLevel);
			QJS.JS_FreeCString(ctx, pMessage);
			if (pStack != null) QJS.JS_FreeCString(ctx, pStack);

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
					Log.Info("[JS] {0}\n{1}", message, formatted);
					break;
			}

			SetUndefined(outU, outTag);
		}

		static string FormatJsStackTrace(string stackTrace)
		{
			return stackTrace.Replace("\\n", "\n").Replace("\\t", "\t");
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Log_Traceback(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			// Stack trace is captured JS-side via new Error().stack
			SetUndefined(outU, outTag);
		}

		static unsafe void RegisterLogFunctions(JSContext ctx)
		{
			// Register _log_internal namespace with dispatch and traceback
			var ns = QJS.JS_NewObject(ctx);

			var pDispatchBytes = Encoding.UTF8.GetBytes("dispatch\0");
			fixed (byte* pDispatch = pDispatchBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Log_Dispatch, pDispatch, 3);
				QJS.JS_SetPropertyStr(ctx, ns, pDispatch, fn);
			}
			var pTracebackBytes = Encoding.UTF8.GetBytes("traceback\0");
			fixed (byte* pTraceback = pTracebackBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Log_Traceback, pTraceback, 0);
				QJS.JS_SetPropertyStr(ctx, ns, pTraceback, fn);
			}

			var global = QJS.JS_GetGlobalObject(ctx);
			var pNameBytes = Encoding.UTF8.GetBytes("_log_internal\0");
			fixed (byte* pName = pNameBytes)
				QJS.JS_SetPropertyStr(ctx, global, pName, ns);

			// Bootstrap JS-side log.* wrappers
			const string logBootstrap = @"(function() {
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

			var codeBytes = Encoding.UTF8.GetBytes(logBootstrap);
			var filenameBytes = Encoding.UTF8.GetBytes("<log_bootstrap>\0");
			fixed (byte* pCode = codeBytes, pFilename = filenameBytes)
			{
				var result = QJS.JS_Eval(ctx, pCode, codeBytes.Length, pFilename,
					QJS.JS_EVAL_TYPE_GLOBAL);
				if (QJS.IsException(result))
				{
					var ex = QJS.JS_GetException(ctx);
					var pMsg = QJS.JS_ToCString(ctx, ex);
					var msg = Marshal.PtrToStringUTF8((nint)pMsg) ?? "unknown error";
					QJS.JS_FreeCString(ctx, pMsg);
					QJS.JS_FreeValue(ctx, ex);
					Log.Error("[JsECS] Failed to initialize JS log helpers: {0}", msg);
				}
				QJS.JS_FreeValue(ctx, result);
			}

			QJS.JS_FreeValue(ctx, global);
		}
	}
}
