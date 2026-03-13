namespace UnityJS.Entities.Core
{
	using System.Text;
	using AOT;
	using UnityJS.QJS;
	using UnityJS.Runtime;
	using Unity.Mathematics;

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
			var a = JsStateExtensions.JsObjectToFloat3(ctx, argv[0]);
			var b = JsStateExtensions.JsObjectToFloat3(ctx, argv[1]);
			var result = JsStateExtensions.Float3ToJsObject(ctx, math.cross(a, b));
			*outU = result.u; *outTag = result.tag;
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Math_Dot(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			var a = JsStateExtensions.JsObjectToFloat3(ctx, argv[0]);
			var b = JsStateExtensions.JsObjectToFloat3(ctx, argv[1]);
			var result = QJS.NewFloat64(ctx, math.dot(a, b));
			*outU = result.u; *outTag = result.tag;
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Math_Normalize(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			var v = JsStateExtensions.JsObjectToFloat3(ctx, argv[0]);
			var result = JsStateExtensions.Float3ToJsObject(ctx, math.normalizesafe(v));
			*outU = result.u; *outTag = result.tag;
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Math_Lerp(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			var a = JsStateExtensions.JsObjectToFloat3(ctx, argv[0]);
			var b = JsStateExtensions.JsObjectToFloat3(ctx, argv[1]);
			double t;
			QJS.JS_ToFloat64(ctx, &t, argv[2]);
			var result = JsStateExtensions.Float3ToJsObject(ctx, math.lerp(a, b, (float)t));
			*outU = result.u; *outTag = result.tag;
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Math_RgbToHsv(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			var rgb = JsStateExtensions.JsObjectToFloat3(ctx, argv[0]);
			var cMax = math.max(rgb.x, math.max(rgb.y, rgb.z));
			var cMin = math.min(rgb.x, math.min(rgb.y, rgb.z));
			var delta = cMax - cMin;

			float h = 0f;
			if (delta > 1e-6f)
			{
				if (cMax == rgb.x) h = 60f * (((rgb.y - rgb.z) / delta) % 6f);
				else if (cMax == rgb.y) h = 60f * ((rgb.z - rgb.x) / delta + 2f);
				else h = 60f * ((rgb.x - rgb.y) / delta + 4f);
			}
			if (h < 0f) h += 360f;

			var s = cMax > 1e-6f ? delta / cMax : 0f;

			// Return {h, s, v} object (JS can't multi-return like Lua)
			var obj = QJS.JS_NewObject(ctx);
			var pHBytes = Encoding.UTF8.GetBytes("h\0");
			var pSBytes = Encoding.UTF8.GetBytes("s\0");
			var pVBytes = Encoding.UTF8.GetBytes("v\0");
			fixed (byte* pH = pHBytes, pS = pSBytes, pV = pVBytes)
			{
				QJS.JS_SetPropertyStr(ctx, obj, pH, QJS.NewFloat64(ctx, h));
				QJS.JS_SetPropertyStr(ctx, obj, pS, QJS.NewFloat64(ctx, s));
				QJS.JS_SetPropertyStr(ctx, obj, pV, QJS.NewFloat64(ctx, cMax));
			}
			*outU = obj.u; *outTag = obj.tag;
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Math_HsvToRgb(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			double dh, ds, dv;
			QJS.JS_ToFloat64(ctx, &dh, argv[0]);
			QJS.JS_ToFloat64(ctx, &ds, argv[1]);
			QJS.JS_ToFloat64(ctx, &dv, argv[2]);
			var h = (float)dh;
			var s = (float)ds;
			var v = (float)dv;

			h = ((h % 360f) + 360f) % 360f;
			var c = v * s;
			var x = c * (1f - math.abs((h / 60f) % 2f - 1f));
			var m = v - c;

			float3 rgb;
			if (h < 60f) rgb = new float3(c, x, 0f);
			else if (h < 120f) rgb = new float3(x, c, 0f);
			else if (h < 180f) rgb = new float3(0f, c, x);
			else if (h < 240f) rgb = new float3(0f, x, c);
			else if (h < 300f) rgb = new float3(x, 0f, c);
			else rgb = new float3(c, 0f, x);

			var result = JsStateExtensions.Float3ToJsObject(ctx, rgb + m);
			*outU = result.u; *outTag = result.tag;
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Math_OklabToRgb(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			var lab = JsStateExtensions.JsObjectToFloat3(ctx, argv[0]);

			var lp = lab.x + 0.3963377774f * lab.y + 0.2158037573f * lab.z;
			var mp = lab.x - 0.1055613458f * lab.y - 0.0638541728f * lab.z;
			var sp = lab.x - 0.0894841775f * lab.y - 1.2914855480f * lab.z;

			var ll = lp * lp * lp;
			var mm = mp * mp * mp;
			var ss = sp * sp * sp;

			var r = +4.0767416621f * ll - 3.3077115913f * mm + 0.2309699292f * ss;
			var g = -1.2684380046f * ll + 2.6097574011f * mm - 0.3413193965f * ss;
			var b = -0.0041960863f * ll - 0.7034186147f * mm + 1.7076147010f * ss;

			var result = JsStateExtensions.Float3ToJsObject(ctx, new float3(r, g, b));
			*outU = result.u; *outTag = result.tag;
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Math_RgbToOklab(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			var rgb = JsStateExtensions.JsObjectToFloat3(ctx, argv[0]);

			var ll = 0.4122214708f * rgb.x + 0.5363325363f * rgb.y + 0.0514459929f * rgb.z;
			var mm = 0.2119034982f * rgb.x + 0.6806995451f * rgb.y + 0.1073969566f * rgb.z;
			var ss = 0.0883024619f * rgb.x + 0.2817188376f * rgb.y + 0.6299787005f * rgb.z;

			var lc = math.pow(math.max(ll, 0f), 1f / 3f);
			var mc = math.pow(math.max(mm, 0f), 1f / 3f);
			var sc = math.pow(math.max(ss, 0f), 1f / 3f);

			var L = 0.2104542553f * lc + 0.7936177850f * mc - 0.0040720468f * sc;
			var a = 1.9779984951f * lc - 2.4285922050f * mc + 0.4505937099f * sc;
			var bv = 0.0259040371f * lc + 0.7827717662f * mc - 0.8086757660f * sc;

			var result = JsStateExtensions.Float3ToJsObject(ctx, new float3(L, a, bv));
			*outU = result.u; *outTag = result.tag;
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
				pBytes = Encoding.UTF8.GetBytes("rgbToHsv\0");
				fixed (byte* p = pBytes)
				{
					var fn = QJSShim.qjs_shim_new_function(ctx, Math_RgbToHsv, p, 1);
					QJS.JS_SetPropertyStr(ctx, ns, p, fn);
				}
				pBytes = Encoding.UTF8.GetBytes("hsvToRgb\0");
				fixed (byte* p = pBytes)
				{
					var fn = QJSShim.qjs_shim_new_function(ctx, Math_HsvToRgb, p, 3);
					QJS.JS_SetPropertyStr(ctx, ns, p, fn);
				}
				pBytes = Encoding.UTF8.GetBytes("oklabToRgb\0");
				fixed (byte* p = pBytes)
				{
					var fn = QJSShim.qjs_shim_new_function(ctx, Math_OklabToRgb, p, 1);
					QJS.JS_SetPropertyStr(ctx, ns, p, fn);
				}
				pBytes = Encoding.UTF8.GetBytes("rgbToOklab\0");
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
