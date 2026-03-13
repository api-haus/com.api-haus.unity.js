namespace UnityJS.Entities.Core
{
  using System.Text;
  using AOT;
  using Unity.Burst;
  using Unity.Mathematics;
  using UnityJS.QJS;

  public static class JsSystemBridge
  {
    struct DeltaTimeMarker { }

    struct ElapsedTimeMarker { }

    static readonly SharedStatic<float> s_deltaTime =
      SharedStatic<float>.GetOrCreate<DeltaTimeMarker>();

    static readonly SharedStatic<double> s_elapsedTime =
      SharedStatic<double>.GetOrCreate<ElapsedTimeMarker>();

    static Unity.Mathematics.Random s_random;

    public static void UpdateContext(float deltaTime, double elapsedTime)
    {
      s_deltaTime.Data = deltaTime;
      s_elapsedTime.Data = elapsedTime;
    }

    public static unsafe void Register(JSContext ctx)
    {
      s_random = new Unity.Mathematics.Random((uint)System.Environment.TickCount | 1u);

      var ns = QJS.JS_NewObject(ctx);

      var pDeltaTimeBytes = Encoding.UTF8.GetBytes("deltaTime\0");
      fixed (byte* pDeltaTime = pDeltaTimeBytes)
      {
        var fn = QJSShim.qjs_shim_new_function(ctx, System_DeltaTime, pDeltaTime, 0);
        QJS.JS_SetPropertyStr(ctx, ns, pDeltaTime, fn);
      }
      var pTimeBytes = Encoding.UTF8.GetBytes("time\0");
      fixed (byte* pTime = pTimeBytes)
      {
        var fn = QJSShim.qjs_shim_new_function(ctx, System_Time, pTime, 0);
        QJS.JS_SetPropertyStr(ctx, ns, pTime, fn);
      }
      var pRandomBytes = Encoding.UTF8.GetBytes("random\0");
      fixed (byte* pRandom = pRandomBytes)
      {
        var fn = QJSShim.qjs_shim_new_function(ctx, System_Random, pRandom, 2);
        QJS.JS_SetPropertyStr(ctx, ns, pRandom, fn);
      }
      var pRandomIntBytes = Encoding.UTF8.GetBytes("randomInt\0");
      fixed (byte* pRandomInt = pRandomIntBytes)
      {
        var fn = QJSShim.qjs_shim_new_function(ctx, System_RandomInt, pRandomInt, 2);
        QJS.JS_SetPropertyStr(ctx, ns, pRandomInt, fn);
      }

      var global = QJS.JS_GetGlobalObject(ctx);
      var pSystemBytes = Encoding.UTF8.GetBytes("system\0");
      fixed (byte* pSystem = pSystemBytes)
        QJS.JS_SetPropertyStr(ctx, global, pSystem, ns);
      QJS.JS_FreeValue(ctx, global);
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void System_DeltaTime(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      var val = QJS.NewFloat64(ctx, s_deltaTime.Data);
      *outU = val.u;
      *outTag = val.tag;
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void System_Time(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      var val = QJS.NewFloat64(ctx, s_elapsedTime.Data);
      *outU = val.u;
      *outTag = val.tag;
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void System_Random(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      float min = 0,
        max = 1;
      if (argc >= 2)
      {
        double d;
        QJS.JS_ToFloat64(ctx, &d, argv[0]);
        min = (float)d;
        QJS.JS_ToFloat64(ctx, &d, argv[1]);
        max = (float)d;
      }

      var value = s_random.NextFloat(min, max);
      var val = QJS.NewFloat64(ctx, value);
      *outU = val.u;
      *outTag = val.tag;
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void System_RandomInt(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      int min = 0,
        max = 1;
      if (argc >= 2)
      {
        int i;
        QJS.JS_ToInt32(ctx, &i, argv[0]);
        min = i;
        QJS.JS_ToInt32(ctx, &i, argv[1]);
        max = i;
      }

      var value = s_random.NextInt(min, max + 1);
      var val = QJS.NewInt32(ctx, value);
      *outU = val.u;
      *outTag = val.tag;
    }
  }
}
