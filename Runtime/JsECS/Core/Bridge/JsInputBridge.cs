namespace UnityJS.Entities.Core
{
	using System.Text;
	using AOT;
	using UnityJS.QJS;

	/// <summary>
	/// Bridge functions for input operations.
	/// JS API: input.read_value(), input.was_pressed(), input.is_held(), input.was_released()
	/// </summary>
	static partial class JsECSBridge
	{
		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Input_ReadValue(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Input_WasPressed(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Input_IsHeld(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Input_WasReleased(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			SetUndefined(outU, outTag);
		}

		static unsafe void RegisterInputFunctions(JSContext ctx)
		{
			var ns = QJS.JS_NewObject(ctx);

			var pReadValueBytes = Encoding.UTF8.GetBytes("read_value\0");
			fixed (byte* pReadValue = pReadValueBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Input_ReadValue, pReadValue, 1);
				QJS.JS_SetPropertyStr(ctx, ns, pReadValue, fn);
			}
			var pWasPressedBytes = Encoding.UTF8.GetBytes("was_pressed\0");
			fixed (byte* pWasPressed = pWasPressedBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Input_WasPressed, pWasPressed, 1);
				QJS.JS_SetPropertyStr(ctx, ns, pWasPressed, fn);
			}
			var pIsHeldBytes = Encoding.UTF8.GetBytes("is_held\0");
			fixed (byte* pIsHeld = pIsHeldBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Input_IsHeld, pIsHeld, 1);
				QJS.JS_SetPropertyStr(ctx, ns, pIsHeld, fn);
			}
			var pWasReleasedBytes = Encoding.UTF8.GetBytes("was_released\0");
			fixed (byte* pWasReleased = pWasReleasedBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Input_WasReleased, pWasReleased, 1);
				QJS.JS_SetPropertyStr(ctx, ns, pWasReleased, fn);
			}

			var global = QJS.JS_GetGlobalObject(ctx);
			var pNameBytes = Encoding.UTF8.GetBytes("input\0");
			fixed (byte* pName = pNameBytes)
				QJS.JS_SetPropertyStr(ctx, global, pName, ns);
			QJS.JS_FreeValue(ctx, global);
		}
	}
}
