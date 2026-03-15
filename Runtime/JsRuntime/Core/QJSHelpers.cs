namespace UnityJS.Runtime
{
  using QJS;

  public static unsafe class QJSHelpers
  {
    public static float ArgFloat(JSContext ctx, JSValue* argv, int idx)
    {
      double d;
      QJS.JS_ToFloat64(ctx, &d, argv[idx]);
      return (float)d;
    }

    public static int ArgInt(JSContext ctx, JSValue* argv, int idx)
    {
      int v;
      QJS.JS_ToInt32(ctx, &v, argv[idx]);
      return v;
    }
  }
}
