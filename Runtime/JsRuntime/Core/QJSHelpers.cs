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

    public static string ArgString(JSContext ctx, JSValue* argv, int idx)
    {
      return QJS.ToManagedString(ctx, argv[idx]);
    }

    /// <summary>
    /// Register a JS function on a namespace object via the shim callback table.
    /// </summary>
    public static void AddFunction(
      JSContext ctx,
      JSValue ns,
      string name,
      QJSShimCallback callback,
      int argc
    )
    {
      var bytes = QJS.U8(name);
      fixed (byte* p = bytes)
      {
        var fn = QJSShim.qjs_shim_new_function(ctx, callback, p, argc);
        QJS.JS_SetPropertyStr(ctx, ns, p, fn);
      }
    }

    /// <summary>
    /// Set a namespace object as a property on a global/parent object.
    /// </summary>
    public static void SetNamespace(JSContext ctx, JSValue global, string name, JSValue ns)
    {
      var bytes = QJS.U8(name);
      fixed (byte* p = bytes)
      {
        QJS.JS_SetPropertyStr(ctx, global, p, ns);
      }
    }

    /// <summary>
    /// Copy a JSValue into shim callback output pointers.
    /// </summary>
    public static void SetResult(long* outU, long* outTag, JSValue val)
    {
      *outU = val.u;
      *outTag = val.tag;
    }

    public static void SetUndefined(long* outU, long* outTag)
    {
      SetResult(outU, outTag, QJS.JS_UNDEFINED);
    }

    public static void SetNull(long* outU, long* outTag)
    {
      SetResult(outU, outTag, QJS.JS_NULL);
    }

    public static void SetBool(long* outU, long* outTag, JSContext ctx, bool value)
    {
      SetResult(outU, outTag, QJS.NewBool(ctx, value));
    }

    public static void SetInt(long* outU, long* outTag, JSContext ctx, int value)
    {
      SetResult(outU, outTag, QJS.NewInt32(ctx, value));
    }

    public static void SetFloat(long* outU, long* outTag, JSContext ctx, double value)
    {
      SetResult(outU, outTag, QJS.NewFloat64(ctx, value));
    }
  }
}
