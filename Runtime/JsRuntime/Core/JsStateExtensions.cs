namespace UnityJS.Runtime
{
	using System.Runtime.InteropServices;
	using Unity.Mathematics;
	using UnityJS.QJS;

	/// <summary>
	/// QuickJS value helpers for float3 and quaternion marshalling.
	/// </summary>
	public static unsafe class JsStateExtensions
	{
		static readonly byte[] s_x = { (byte)'x', 0 };
		static readonly byte[] s_y = { (byte)'y', 0 };
		static readonly byte[] s_z = { (byte)'z', 0 };
		static readonly byte[] s_w = { (byte)'w', 0 };

		public static JSValue Float2ToJsObject(JSContext ctx, float2 value)
		{
			var obj = QJS.JS_NewObject(ctx);
			fixed (byte* px = s_x, py = s_y)
			{
				QJS.JS_SetPropertyStr(ctx, obj, px, QJS.NewFloat64(ctx, value.x));
				QJS.JS_SetPropertyStr(ctx, obj, py, QJS.NewFloat64(ctx, value.y));
			}
			return obj;
		}

		public static float2 JsObjectToFloat2(JSContext ctx, JSValue val)
		{
			var result = float2.zero;
			fixed (byte* px = s_x, py = s_y)
			{
				double d;
				var vx = QJS.JS_GetPropertyStr(ctx, val, px);
				if (QJS.IsNumber(vx)) { QJS.JS_ToFloat64(ctx, &d, vx); result.x = (float)d; }
				QJS.JS_FreeValue(ctx, vx);

				var vy = QJS.JS_GetPropertyStr(ctx, val, py);
				if (QJS.IsNumber(vy)) { QJS.JS_ToFloat64(ctx, &d, vy); result.y = (float)d; }
				QJS.JS_FreeValue(ctx, vy);
			}
			return result;
		}

		public static JSValue Float3ToJsObject(JSContext ctx, float3 value)
		{
			var obj = QJS.JS_NewObject(ctx);
			fixed (byte* px = s_x, py = s_y, pz = s_z)
			{
				QJS.JS_SetPropertyStr(ctx, obj, px, QJS.NewFloat64(ctx, value.x));
				QJS.JS_SetPropertyStr(ctx, obj, py, QJS.NewFloat64(ctx, value.y));
				QJS.JS_SetPropertyStr(ctx, obj, pz, QJS.NewFloat64(ctx, value.z));
			}
			return obj;
		}

		public static float3 JsObjectToFloat3(JSContext ctx, JSValue val)
		{
			var result = float3.zero;
			fixed (byte* px = s_x, py = s_y, pz = s_z)
			{
				double d;
				var vx = QJS.JS_GetPropertyStr(ctx, val, px);
				if (QJS.IsNumber(vx)) { QJS.JS_ToFloat64(ctx, &d, vx); result.x = (float)d; }
				QJS.JS_FreeValue(ctx, vx);

				var vy = QJS.JS_GetPropertyStr(ctx, val, py);
				if (QJS.IsNumber(vy)) { QJS.JS_ToFloat64(ctx, &d, vy); result.y = (float)d; }
				QJS.JS_FreeValue(ctx, vy);

				var vz = QJS.JS_GetPropertyStr(ctx, val, pz);
				if (QJS.IsNumber(vz)) { QJS.JS_ToFloat64(ctx, &d, vz); result.z = (float)d; }
				QJS.JS_FreeValue(ctx, vz);
			}
			return result;
		}

		public static JSValue Float4ToJsObject(JSContext ctx, float4 value)
		{
			var obj = QJS.JS_NewObject(ctx);
			fixed (byte* px = s_x, py = s_y, pz = s_z, pw = s_w)
			{
				QJS.JS_SetPropertyStr(ctx, obj, px, QJS.NewFloat64(ctx, value.x));
				QJS.JS_SetPropertyStr(ctx, obj, py, QJS.NewFloat64(ctx, value.y));
				QJS.JS_SetPropertyStr(ctx, obj, pz, QJS.NewFloat64(ctx, value.z));
				QJS.JS_SetPropertyStr(ctx, obj, pw, QJS.NewFloat64(ctx, value.w));
			}
			return obj;
		}

		public static float4 JsObjectToFloat4(JSContext ctx, JSValue val)
		{
			var result = float4.zero;
			fixed (byte* px = s_x, py = s_y, pz = s_z, pw = s_w)
			{
				double d;
				var vx = QJS.JS_GetPropertyStr(ctx, val, px);
				if (QJS.IsNumber(vx)) { QJS.JS_ToFloat64(ctx, &d, vx); result.x = (float)d; }
				QJS.JS_FreeValue(ctx, vx);

				var vy = QJS.JS_GetPropertyStr(ctx, val, py);
				if (QJS.IsNumber(vy)) { QJS.JS_ToFloat64(ctx, &d, vy); result.y = (float)d; }
				QJS.JS_FreeValue(ctx, vy);

				var vz = QJS.JS_GetPropertyStr(ctx, val, pz);
				if (QJS.IsNumber(vz)) { QJS.JS_ToFloat64(ctx, &d, vz); result.z = (float)d; }
				QJS.JS_FreeValue(ctx, vz);

				var vw = QJS.JS_GetPropertyStr(ctx, val, pw);
				if (QJS.IsNumber(vw)) { QJS.JS_ToFloat64(ctx, &d, vw); result.w = (float)d; }
				QJS.JS_FreeValue(ctx, vw);
			}
			return result;
		}

		public static JSValue QuaternionToJsObject(JSContext ctx, quaternion value)
		{
			var obj = QJS.JS_NewObject(ctx);
			fixed (byte* px = s_x, py = s_y, pz = s_z, pw = s_w)
			{
				QJS.JS_SetPropertyStr(ctx, obj, px, QJS.NewFloat64(ctx, value.value.x));
				QJS.JS_SetPropertyStr(ctx, obj, py, QJS.NewFloat64(ctx, value.value.y));
				QJS.JS_SetPropertyStr(ctx, obj, pz, QJS.NewFloat64(ctx, value.value.z));
				QJS.JS_SetPropertyStr(ctx, obj, pw, QJS.NewFloat64(ctx, value.value.w));
			}
			return obj;
		}

		public static quaternion JsObjectToQuaternion(JSContext ctx, JSValue val)
		{
			var result = quaternion.identity;
			fixed (byte* px = s_x, py = s_y, pz = s_z, pw = s_w)
			{
				double d;
				var vx = QJS.JS_GetPropertyStr(ctx, val, px);
				if (QJS.IsNumber(vx)) { QJS.JS_ToFloat64(ctx, &d, vx); result.value.x = (float)d; }
				QJS.JS_FreeValue(ctx, vx);

				var vy = QJS.JS_GetPropertyStr(ctx, val, py);
				if (QJS.IsNumber(vy)) { QJS.JS_ToFloat64(ctx, &d, vy); result.value.y = (float)d; }
				QJS.JS_FreeValue(ctx, vy);

				var vz = QJS.JS_GetPropertyStr(ctx, val, pz);
				if (QJS.IsNumber(vz)) { QJS.JS_ToFloat64(ctx, &d, vz); result.value.z = (float)d; }
				QJS.JS_FreeValue(ctx, vz);

				var vw = QJS.JS_GetPropertyStr(ctx, val, pw);
				if (QJS.IsNumber(vw)) { QJS.JS_ToFloat64(ctx, &d, vw); result.value.w = (float)d; }
				QJS.JS_FreeValue(ctx, vw);
			}
			return result;
		}

		public static float3 QuaternionToEuler(quaternion q)
		{
			float3 euler;

			var sinrCosp = 2 * ((q.value.w * q.value.x) + (q.value.y * q.value.z));
			var cosrCosp = 1 - (2 * ((q.value.x * q.value.x) + (q.value.y * q.value.y)));
			euler.x = math.atan2(sinrCosp, cosrCosp);

			var sinp = 2 * ((q.value.w * q.value.y) - (q.value.z * q.value.x));
			if (math.abs(sinp) >= 1)
				euler.y = math.sign(sinp) * math.PI / 2;
			else
				euler.y = math.asin(sinp);

			var sinyCosp = 2 * ((q.value.w * q.value.z) + (q.value.x * q.value.y));
			var cosyCosp = 1 - (2 * ((q.value.y * q.value.y) + (q.value.z * q.value.z)));
			euler.z = math.atan2(sinyCosp, cosyCosp);

			return math.degrees(euler);
		}
	}
}
