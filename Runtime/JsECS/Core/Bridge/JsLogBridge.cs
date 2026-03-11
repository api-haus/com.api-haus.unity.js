namespace UnityJS.Entities.Core
{
	using System.Text;
	using AOT;
	using UnityJS.QJS;

	/// <summary>
	/// Bridge functions for logging.
	/// JS API: log.debug(), log.info(), log.warning(), log.error(), log.trace()
	/// Implemented via _log_internal.dispatch + JS helper (Stage 4).
	/// </summary>
	static partial class JsECSBridge
	{
		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Log_Dispatch(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Log_Traceback(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
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

			// Also register a stub log namespace (full JS-side helper in Stage 4)
			var logNs = QJS.JS_NewObject(ctx);
			var pLogBytes = Encoding.UTF8.GetBytes("log\0");
			fixed (byte* pLog = pLogBytes)
				QJS.JS_SetPropertyStr(ctx, global, pLog, logNs);

			QJS.JS_FreeValue(ctx, global);
		}
	}
}
