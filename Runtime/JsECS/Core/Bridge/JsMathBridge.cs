namespace UnityJS.Entities.Core
{
	using System.Text;
	using AOT;
	using UnityJS.QJS;

	/// <summary>
	/// Bridge functions for math operations.
	/// JS API: extends global math.* with cross, dot, normalize, lerp, etc.
	/// </summary>
	static partial class JsECSBridge
	{
		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Math_Cross(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Math_Dot(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Math_Normalize(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Math_Lerp(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Math_RgbToHsv(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Math_HsvToRgb(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Math_OklabToRgb(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Math_RgbToOklab(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			SetUndefined(outU, outTag);
		}

		static unsafe void RegisterMathFunctions(JSContext ctx)
		{
			// Create or get math namespace
			var global = QJS.JS_GetGlobalObject(ctx);
			var pMathBytes = Encoding.UTF8.GetBytes("math\0");
			fixed (byte* pMath = pMathBytes)
			{
				var existing = QJS.JS_GetPropertyStr(ctx, global, pMath);
				JSValue ns;
				if (QJS.IsUndefined(existing) || QJS.IsNull(existing))
				{
					QJS.JS_FreeValue(ctx, existing);
					ns = QJS.JS_NewObject(ctx);
				}
				else
				{
					ns = existing;
				}

				var pBytes = Encoding.UTF8.GetBytes("cross\0");
				fixed (byte* p = pBytes)
				{
					var fn = QJSShim.qjs_shim_new_function(ctx, Math_Cross, p, 2);
					QJS.JS_SetPropertyStr(ctx, ns, p, fn);
				}
				pBytes = Encoding.UTF8.GetBytes("dot\0");
				fixed (byte* p = pBytes)
				{
					var fn = QJSShim.qjs_shim_new_function(ctx, Math_Dot, p, 2);
					QJS.JS_SetPropertyStr(ctx, ns, p, fn);
				}
				pBytes = Encoding.UTF8.GetBytes("normalize\0");
				fixed (byte* p = pBytes)
				{
					var fn = QJSShim.qjs_shim_new_function(ctx, Math_Normalize, p, 1);
					QJS.JS_SetPropertyStr(ctx, ns, p, fn);
				}
				pBytes = Encoding.UTF8.GetBytes("lerp\0");
				fixed (byte* p = pBytes)
				{
					var fn = QJSShim.qjs_shim_new_function(ctx, Math_Lerp, p, 3);
					QJS.JS_SetPropertyStr(ctx, ns, p, fn);
				}
				pBytes = Encoding.UTF8.GetBytes("rgb_to_hsv\0");
				fixed (byte* p = pBytes)
				{
					var fn = QJSShim.qjs_shim_new_function(ctx, Math_RgbToHsv, p, 1);
					QJS.JS_SetPropertyStr(ctx, ns, p, fn);
				}
				pBytes = Encoding.UTF8.GetBytes("hsv_to_rgb\0");
				fixed (byte* p = pBytes)
				{
					var fn = QJSShim.qjs_shim_new_function(ctx, Math_HsvToRgb, p, 3);
					QJS.JS_SetPropertyStr(ctx, ns, p, fn);
				}
				pBytes = Encoding.UTF8.GetBytes("oklab_to_rgb\0");
				fixed (byte* p = pBytes)
				{
					var fn = QJSShim.qjs_shim_new_function(ctx, Math_OklabToRgb, p, 1);
					QJS.JS_SetPropertyStr(ctx, ns, p, fn);
				}
				pBytes = Encoding.UTF8.GetBytes("rgb_to_oklab\0");
				fixed (byte* p = pBytes)
				{
					var fn = QJSShim.qjs_shim_new_function(ctx, Math_RgbToOklab, p, 1);
					QJS.JS_SetPropertyStr(ctx, ns, p, fn);
				}

				QJS.JS_SetPropertyStr(ctx, global, pMath, ns);
			}
			QJS.JS_FreeValue(ctx, global);
		}
	}
}
