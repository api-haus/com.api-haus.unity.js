namespace UnityJS.Entities.Core
{
	using System.Runtime.InteropServices;
	using System.Text;
	using AOT;
	using Drawing;
	using UnityJS.QJS;
	using UnityJS.Runtime;
	using UnityEngine;

	/// <summary>
	/// Bridge functions for debug drawing.
	/// JS API: draw.setColor(), draw.withDuration(), draw.line(), draw.ray(), draw.arrow(),
	///         draw.wireSphere(), draw.wireBox(), draw.wireCapsule(), draw.circleXz(),
	///         draw.solidBox(), draw.solidCircle(), draw.label2d()
	/// </summary>
	static partial class JsECSBridge
	{
		static Color s_currentDrawColor = Color.white;
		static float s_currentDuration;

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Draw_SetColor(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			double dr, dg, db, da;
			QJS.JS_ToFloat64(ctx, &dr, argv[0]);
			QJS.JS_ToFloat64(ctx, &dg, argv[1]);
			QJS.JS_ToFloat64(ctx, &db, argv[2]);
			da = 1.0;
			if (argc >= 4) QJS.JS_ToFloat64(ctx, &da, argv[3]);
			s_currentDrawColor = new Color((float)dr, (float)dg, (float)db, (float)da);
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Draw_WithDuration(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			double d;
			QJS.JS_ToFloat64(ctx, &d, argv[0]);
			s_currentDuration = (float)d;
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Draw_Line(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			if (argc < 2) { SetUndefined(outU, outTag); return; }
			var from = JsStateExtensions.JsObjectToFloat3(ctx, argv[0]);
			var to = JsStateExtensions.JsObjectToFloat3(ctx, argv[1]);
			if (s_currentDuration > 0)
				using (Draw.ingame.WithDuration(s_currentDuration))
					Draw.ingame.Line(from, to, s_currentDrawColor);
			else
				Draw.ingame.Line(from, to, s_currentDrawColor);
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Draw_Ray(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			if (argc < 2) { SetUndefined(outU, outTag); return; }
			var origin = JsStateExtensions.JsObjectToFloat3(ctx, argv[0]);
			var direction = JsStateExtensions.JsObjectToFloat3(ctx, argv[1]);
			if (s_currentDuration > 0)
				using (Draw.ingame.WithDuration(s_currentDuration))
					Draw.ingame.Ray(origin, direction, s_currentDrawColor);
			else
				Draw.ingame.Ray(origin, direction, s_currentDrawColor);
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Draw_Arrow(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			if (argc < 2) { SetUndefined(outU, outTag); return; }
			var from = JsStateExtensions.JsObjectToFloat3(ctx, argv[0]);
			var to = JsStateExtensions.JsObjectToFloat3(ctx, argv[1]);
			if (s_currentDuration > 0)
				using (Draw.ingame.WithDuration(s_currentDuration))
					Draw.ingame.Arrow(from, to, s_currentDrawColor);
			else
				Draw.ingame.Arrow(from, to, s_currentDrawColor);
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Draw_WireSphere(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			if (argc < 2) { SetUndefined(outU, outTag); return; }
			var center = JsStateExtensions.JsObjectToFloat3(ctx, argv[0]);
			double radius;
			QJS.JS_ToFloat64(ctx, &radius, argv[1]);
			if (s_currentDuration > 0)
				using (Draw.ingame.WithDuration(s_currentDuration))
					Draw.ingame.WireSphere(center, (float)radius, s_currentDrawColor);
			else
				Draw.ingame.WireSphere(center, (float)radius, s_currentDrawColor);
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Draw_WireBox(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			if (argc < 2) { SetUndefined(outU, outTag); return; }
			var center = JsStateExtensions.JsObjectToFloat3(ctx, argv[0]);
			var size = JsStateExtensions.JsObjectToFloat3(ctx, argv[1]);
			if (s_currentDuration > 0)
				using (Draw.ingame.WithDuration(s_currentDuration))
					Draw.ingame.WireBox(center, size, s_currentDrawColor);
			else
				Draw.ingame.WireBox(center, size, s_currentDrawColor);
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Draw_WireCapsule(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			if (argc < 3) { SetUndefined(outU, outTag); return; }
			var start = JsStateExtensions.JsObjectToFloat3(ctx, argv[0]);
			var end = JsStateExtensions.JsObjectToFloat3(ctx, argv[1]);
			double radius;
			QJS.JS_ToFloat64(ctx, &radius, argv[2]);
			if (s_currentDuration > 0)
				using (Draw.ingame.WithDuration(s_currentDuration))
					Draw.ingame.WireCapsule(start, end, (float)radius, s_currentDrawColor);
			else
				Draw.ingame.WireCapsule(start, end, (float)radius, s_currentDrawColor);
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Draw_CircleXZ(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			if (argc < 2) { SetUndefined(outU, outTag); return; }
			var center = JsStateExtensions.JsObjectToFloat3(ctx, argv[0]);
			double radius;
			QJS.JS_ToFloat64(ctx, &radius, argv[1]);
			if (s_currentDuration > 0)
				using (Draw.ingame.WithDuration(s_currentDuration))
					Draw.ingame.xz.Circle(center, (float)radius, s_currentDrawColor);
			else
				Draw.ingame.xz.Circle(center, (float)radius, s_currentDrawColor);
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Draw_SolidBox(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			if (argc < 2) { SetUndefined(outU, outTag); return; }
			var center = JsStateExtensions.JsObjectToFloat3(ctx, argv[0]);
			var size = JsStateExtensions.JsObjectToFloat3(ctx, argv[1]);
			if (s_currentDuration > 0)
				using (Draw.ingame.WithDuration(s_currentDuration))
					Draw.ingame.SolidBox(center, size, s_currentDrawColor);
			else
				Draw.ingame.SolidBox(center, size, s_currentDrawColor);
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Draw_SolidCircle(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			if (argc < 3) { SetUndefined(outU, outTag); return; }
			var center = JsStateExtensions.JsObjectToFloat3(ctx, argv[0]);
			var normal = JsStateExtensions.JsObjectToFloat3(ctx, argv[1]);
			double radius;
			QJS.JS_ToFloat64(ctx, &radius, argv[2]);
			if (s_currentDuration > 0)
				using (Draw.ingame.WithDuration(s_currentDuration))
					Draw.ingame.SolidCircle(center, normal, (float)radius, s_currentDrawColor);
			else
				Draw.ingame.SolidCircle(center, normal, (float)radius, s_currentDrawColor);
			SetUndefined(outU, outTag);
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Draw_Label2D(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			if (argc < 2) { SetUndefined(outU, outTag); return; }
			var position = JsStateExtensions.JsObjectToFloat3(ctx, argv[0]);
			var ptr = QJS.JS_ToCString(ctx, argv[1]);
			if (ptr != null)
			{
				var text = Marshal.PtrToStringUTF8((nint)ptr) ?? "";
				QJS.JS_FreeCString(ctx, ptr);
				if (s_currentDuration > 0)
					using (Draw.ingame.WithDuration(s_currentDuration))
						Draw.ingame.Label2D(position, text, s_currentDrawColor);
				else
					Draw.ingame.Label2D(position, text, s_currentDrawColor);
			}
			SetUndefined(outU, outTag);
		}

		static unsafe void RegisterDrawFunctions(JSContext ctx)
		{
			var ns = QJS.JS_NewObject(ctx);

			var pBytes = Encoding.UTF8.GetBytes("setColor\0");
			fixed (byte* p = pBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Draw_SetColor, p, 4);
				QJS.JS_SetPropertyStr(ctx, ns, p, fn);
			}
			pBytes = Encoding.UTF8.GetBytes("withDuration\0");
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
			pBytes = Encoding.UTF8.GetBytes("wireSphere\0");
			fixed (byte* p = pBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Draw_WireSphere, p, 2);
				QJS.JS_SetPropertyStr(ctx, ns, p, fn);
			}
			pBytes = Encoding.UTF8.GetBytes("wireBox\0");
			fixed (byte* p = pBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Draw_WireBox, p, 2);
				QJS.JS_SetPropertyStr(ctx, ns, p, fn);
			}
			pBytes = Encoding.UTF8.GetBytes("wireCapsule\0");
			fixed (byte* p = pBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Draw_WireCapsule, p, 3);
				QJS.JS_SetPropertyStr(ctx, ns, p, fn);
			}
			pBytes = Encoding.UTF8.GetBytes("circleXz\0");
			fixed (byte* p = pBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Draw_CircleXZ, p, 2);
				QJS.JS_SetPropertyStr(ctx, ns, p, fn);
			}
			pBytes = Encoding.UTF8.GetBytes("solidBox\0");
			fixed (byte* p = pBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Draw_SolidBox, p, 2);
				QJS.JS_SetPropertyStr(ctx, ns, p, fn);
			}
			pBytes = Encoding.UTF8.GetBytes("solidCircle\0");
			fixed (byte* p = pBytes)
			{
				var fn = QJSShim.qjs_shim_new_function(ctx, Draw_SolidCircle, p, 3);
				QJS.JS_SetPropertyStr(ctx, ns, p, fn);
			}
			pBytes = Encoding.UTF8.GetBytes("label2d\0");
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
