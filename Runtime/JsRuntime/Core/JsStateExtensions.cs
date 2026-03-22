namespace UnityJS.Runtime
{
  using System.Runtime.InteropServices;
  using QJS;
  using Unity.Mathematics;

  /// <summary>
  /// QuickJS value helpers for float3 and quaternion marshalling.
  /// </summary>
  public static unsafe class JsStateExtensions
  {
    static readonly byte[] s_x = { (byte)'x', 0 };
    static readonly byte[] s_y = { (byte)'y', 0 };
    static readonly byte[] s_z = { (byte)'z', 0 };
    static readonly byte[] s_w = { (byte)'w', 0 };

    static JSValue s_float2Proto;
    static JSValue s_float3Proto;
    static JSValue s_float4Proto;
    static bool s_protosSet;

    public static void SetVectorPrototypes(JSValue f2p, JSValue f3p, JSValue f4p)
    {
      s_float2Proto = f2p;
      s_float3Proto = f3p;
      s_float4Proto = f4p;
      s_protosSet = true;
    }

    public static void ClearVectorPrototypes(JSContext ctx)
    {
      if (!s_protosSet)
        return;
      QJS.JS_FreeValue(ctx, s_float2Proto);
      QJS.JS_FreeValue(ctx, s_float3Proto);
      QJS.JS_FreeValue(ctx, s_float4Proto);
      s_float2Proto = default;
      s_float3Proto = default;
      s_float4Proto = default;
      s_protosSet = false;
    }

    static JSValue NewObjectWithProto(JSContext ctx, JSValue proto)
    {
      return s_protosSet ? QJS.JS_NewObjectProto(ctx, proto) : QJS.JS_NewObject(ctx);
    }

    static float ReadComponent(JSContext ctx, JSValue obj, byte* prop)
    {
      var v = QJS.JS_GetPropertyStr(ctx, obj, prop);
      float result = 0;
      if (QJS.IsNumber(v))
      {
        double d;
        QJS.JS_ToFloat64(ctx, &d, v);
        result = (float)d;
      }
      QJS.JS_FreeValue(ctx, v);
      return result;
    }

    public static JSValue Float2ToJsObject(JSContext ctx, float2 value)
    {
      var obj = NewObjectWithProto(ctx, s_float2Proto);
      fixed (
        byte* px = s_x,
          py = s_y
      )
      {
        QJS.JS_SetPropertyStr(ctx, obj, px, QJS.NewFloat64(ctx, value.x));
        QJS.JS_SetPropertyStr(ctx, obj, py, QJS.NewFloat64(ctx, value.y));
      }

      return obj;
    }

    public static float2 JsObjectToFloat2(JSContext ctx, JSValue val)
    {
      fixed (byte* px = s_x, py = s_y)
        return new float2(ReadComponent(ctx, val, px), ReadComponent(ctx, val, py));
    }

    public static JSValue Float3ToJsObject(JSContext ctx, float3 value)
    {
      var obj = NewObjectWithProto(ctx, s_float3Proto);
      fixed (
        byte* px = s_x,
          py = s_y,
          pz = s_z
      )
      {
        QJS.JS_SetPropertyStr(ctx, obj, px, QJS.NewFloat64(ctx, value.x));
        QJS.JS_SetPropertyStr(ctx, obj, py, QJS.NewFloat64(ctx, value.y));
        QJS.JS_SetPropertyStr(ctx, obj, pz, QJS.NewFloat64(ctx, value.z));
      }

      return obj;
    }

    public static float3 JsObjectToFloat3(JSContext ctx, JSValue val)
    {
      fixed (byte* px = s_x, py = s_y, pz = s_z)
        return new float3(
          ReadComponent(ctx, val, px),
          ReadComponent(ctx, val, py),
          ReadComponent(ctx, val, pz)
        );
    }

    public static JSValue Float4ToJsObject(JSContext ctx, float4 value)
    {
      var obj = NewObjectWithProto(ctx, s_float4Proto);
      fixed (
        byte* px = s_x,
          py = s_y,
          pz = s_z,
          pw = s_w
      )
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
      fixed (byte* px = s_x, py = s_y, pz = s_z, pw = s_w)
        return new float4(
          ReadComponent(ctx, val, px),
          ReadComponent(ctx, val, py),
          ReadComponent(ctx, val, pz),
          ReadComponent(ctx, val, pw)
        );
    }

    public static JSValue QuaternionToJsObject(JSContext ctx, quaternion value)
    {
      var obj = NewObjectWithProto(ctx, s_float4Proto);
      fixed (
        byte* px = s_x,
          py = s_y,
          pz = s_z,
          pw = s_w
      )
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
      fixed (byte* px = s_x, py = s_y, pz = s_z, pw = s_w)
        return new quaternion(
          ReadComponent(ctx, val, px),
          ReadComponent(ctx, val, py),
          ReadComponent(ctx, val, pz),
          ReadComponent(ctx, val, pw)
        );
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
