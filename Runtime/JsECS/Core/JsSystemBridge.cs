namespace UnityJS.Entities.Core
{
  using AOT;
  using QJS;
  using Unity.Burst;
  using Unity.Mathematics;
  using static Runtime.QJSHelpers;

  public static class JsSystemBridge
  {
    struct DeltaTimeMarker { }

    struct ElapsedTimeMarker { }

    static readonly SharedStatic<float> s_deltaTime =
      SharedStatic<float>.GetOrCreate<DeltaTimeMarker>();

    static readonly SharedStatic<double> s_elapsedTime =
      SharedStatic<double>.GetOrCreate<ElapsedTimeMarker>();

    static Random s_random;

    public static void UpdateContext(float deltaTime, double elapsedTime)
    {
      s_deltaTime.Data = deltaTime;
      s_elapsedTime.Data = elapsedTime;
    }

    public static unsafe void Register(JSContext ctx)
    {
      s_random = new Random((uint)System.Environment.TickCount | 1u);

      var ns = QJS.JS_NewObject(ctx);

      AddFunction(ctx, ns, "deltaTime", System_DeltaTime, 0);
      AddFunction(ctx, ns, "time", System_Time, 0);
      AddFunction(ctx, ns, "random", System_Random, 2);
      AddFunction(ctx, ns, "randomInt", System_RandomInt, 2);

      var global = QJS.JS_GetGlobalObject(ctx);
      SetNamespace(ctx, global, "system", ns);
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
      SetFloat(outU, outTag, ctx, s_deltaTime.Data);
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
      SetFloat(outU, outTag, ctx, s_elapsedTime.Data);
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
      SetFloat(outU, outTag, ctx, value);
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
      SetInt(outU, outTag, ctx, value);
    }
  }
}
