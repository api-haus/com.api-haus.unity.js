namespace UnityJS.Entities.Core
{
	using System.Text;
	using AOT;
	using UnityJS.QJS;

	/// <summary>
	/// Bridge functions for debug drawing.
	/// JS API: draw.set_color(), draw.with_duration(), draw.line(), draw.ray(), draw.arrow(),
	///         draw.wire_sphere(), draw.wire_box(), draw.wire_capsule(), draw.circle_xz(),
	///         draw.solid_box(), draw.solid_circle(), draw.label_2d()
	/// </summary>
	static partial class JsECSBridge
	{
		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Draw_SetColor(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Draw_WithDuration(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Draw_Line(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Draw_Ray(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Draw_Arrow(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Draw_WireSphere(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Draw_WireBox(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Draw_WireCapsule(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Draw_CircleXZ(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Draw_SolidBox(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Draw_SolidCircle(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Draw_Label2D(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			SetUndefined(outU, outTag);
		}

		static unsafe void RegisterDrawFunctions(JSContext ctx)
		{
			var ns = QJS.JS_NewObject(ctx);

			var pBytes = Encoding.UTF8.GetBytes("set_color\0");
			fixed (byte* p = pBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Draw_SetColor, p, 4);
				QJS.JS_SetPropertyStr(ctx, ns, p, fn);
			}
			pBytes = Encoding.UTF8.GetBytes("with_duration\0");
			fixed (byte* p = pBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Draw_WithDuration, p, 1);
				QJS.JS_SetPropertyStr(ctx, ns, p, fn);
			}
			pBytes = Encoding.UTF8.GetBytes("line\0");
			fixed (byte* p = pBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Draw_Line, p, 2);
				QJS.JS_SetPropertyStr(ctx, ns, p, fn);
			}
			pBytes = Encoding.UTF8.GetBytes("ray\0");
			fixed (byte* p = pBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Draw_Ray, p, 2);
				QJS.JS_SetPropertyStr(ctx, ns, p, fn);
			}
			pBytes = Encoding.UTF8.GetBytes("arrow\0");
			fixed (byte* p = pBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Draw_Arrow, p, 2);
				QJS.JS_SetPropertyStr(ctx, ns, p, fn);
			}
			pBytes = Encoding.UTF8.GetBytes("wire_sphere\0");
			fixed (byte* p = pBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Draw_WireSphere, p, 2);
				QJS.JS_SetPropertyStr(ctx, ns, p, fn);
			}
			pBytes = Encoding.UTF8.GetBytes("wire_box\0");
			fixed (byte* p = pBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Draw_WireBox, p, 2);
				QJS.JS_SetPropertyStr(ctx, ns, p, fn);
			}
			pBytes = Encoding.UTF8.GetBytes("wire_capsule\0");
			fixed (byte* p = pBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Draw_WireCapsule, p, 3);
				QJS.JS_SetPropertyStr(ctx, ns, p, fn);
			}
			pBytes = Encoding.UTF8.GetBytes("circle_xz\0");
			fixed (byte* p = pBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Draw_CircleXZ, p, 2);
				QJS.JS_SetPropertyStr(ctx, ns, p, fn);
			}
			pBytes = Encoding.UTF8.GetBytes("solid_box\0");
			fixed (byte* p = pBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Draw_SolidBox, p, 2);
				QJS.JS_SetPropertyStr(ctx, ns, p, fn);
			}
			pBytes = Encoding.UTF8.GetBytes("solid_circle\0");
			fixed (byte* p = pBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Draw_SolidCircle, p, 3);
				QJS.JS_SetPropertyStr(ctx, ns, p, fn);
			}
			pBytes = Encoding.UTF8.GetBytes("label_2d\0");
			fixed (byte* p = pBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Draw_Label2D, p, 2);
				QJS.JS_SetPropertyStr(ctx, ns, p, fn);
			}

			var global = QJS.JS_GetGlobalObject(ctx);
			var pNameBytes = Encoding.UTF8.GetBytes("draw\0");
			fixed (byte* pName = pNameBytes)
				QJS.JS_SetPropertyStr(ctx, global, pName, ns);
			QJS.JS_FreeValue(ctx, global);
		}
	}
}
