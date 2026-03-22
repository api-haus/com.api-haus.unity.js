namespace UnityJS.Integration.ALINE
{
  using AOT;
  using Drawing;
  using UnityEngine;
  using UnityJS.QJS;
  using UnityJS.Runtime;
  using static UnityJS.Runtime.QJSHelpers;

  /// <summary>
  /// Bridge functions for debug drawing via ALINE.
  /// JS API: draw.setColor(), draw.withDuration(), draw.line(), draw.ray(), draw.arrow(),
  ///         draw.wireSphere(), draw.wireBox(), draw.wireCapsule(), draw.circleXz(),
  ///         draw.solidBox(), draw.solidCircle(), draw.label2d()
  /// </summary>
  public static class JsDrawBridge
  {
    static Color s_drawColor = Color.white;
    static float s_drawDuration;

    static void WithDraw(System.Action draw)
    {
      if (s_drawDuration > 0)
        using (Draw.ingame.WithDuration(s_drawDuration))
          draw();
      else
        draw();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSession()
    {
      s_drawColor = Color.white;
      s_drawDuration = 0;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void AutoRegister() =>
      UnityJS.Entities.Core.JsFunctionRegistry.Register("draw", RegisterDrawFunctions);

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void Draw_SetColor(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      double dr,
        dg,
        db,
        da;
      QJS.JS_ToFloat64(ctx, &dr, argv[0]);
      QJS.JS_ToFloat64(ctx, &dg, argv[1]);
      QJS.JS_ToFloat64(ctx, &db, argv[2]);
      da = 1.0;
      if (argc >= 4)
        QJS.JS_ToFloat64(ctx, &da, argv[3]);
      s_drawColor = new Color((float)dr, (float)dg, (float)db, (float)da);
      SetUndefined(outU, outTag);
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void Draw_WithDuration(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      double d;
      QJS.JS_ToFloat64(ctx, &d, argv[0]);
      s_drawDuration = (float)d;
      SetUndefined(outU, outTag);
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void Draw_Line(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      if (argc < 2)
      {
        SetUndefined(outU, outTag);
        return;
      }

      var from = JsStateExtensions.JsObjectToFloat3(ctx, argv[0]);
      var to = JsStateExtensions.JsObjectToFloat3(ctx, argv[1]);
      WithDraw(() => Draw.ingame.Line(from, to, s_drawColor));

      SetUndefined(outU, outTag);
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void Draw_Ray(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      if (argc < 2)
      {
        SetUndefined(outU, outTag);
        return;
      }

      var origin = JsStateExtensions.JsObjectToFloat3(ctx, argv[0]);
      var direction = JsStateExtensions.JsObjectToFloat3(ctx, argv[1]);
      WithDraw(() => Draw.ingame.Ray(origin, direction, s_drawColor));

      SetUndefined(outU, outTag);
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void Draw_Arrow(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      if (argc < 2)
      {
        SetUndefined(outU, outTag);
        return;
      }

      var from = JsStateExtensions.JsObjectToFloat3(ctx, argv[0]);
      var to = JsStateExtensions.JsObjectToFloat3(ctx, argv[1]);
      WithDraw(() => Draw.ingame.Arrow(from, to, s_drawColor));

      SetUndefined(outU, outTag);
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void Draw_WireSphere(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      if (argc < 2)
      {
        SetUndefined(outU, outTag);
        return;
      }

      var center = JsStateExtensions.JsObjectToFloat3(ctx, argv[0]);
      double rd;
      QJS.JS_ToFloat64(ctx, &rd, argv[1]);
      var radius = (float)rd;
      WithDraw(() => Draw.ingame.WireSphere(center, radius, s_drawColor));

      SetUndefined(outU, outTag);
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void Draw_WireBox(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      if (argc < 2)
      {
        SetUndefined(outU, outTag);
        return;
      }

      var center = JsStateExtensions.JsObjectToFloat3(ctx, argv[0]);
      var size = JsStateExtensions.JsObjectToFloat3(ctx, argv[1]);
      WithDraw(() => Draw.ingame.WireBox(center, size, s_drawColor));

      SetUndefined(outU, outTag);
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void Draw_WireCapsule(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      if (argc < 3)
      {
        SetUndefined(outU, outTag);
        return;
      }

      var start = JsStateExtensions.JsObjectToFloat3(ctx, argv[0]);
      var end = JsStateExtensions.JsObjectToFloat3(ctx, argv[1]);
      double rd;
      QJS.JS_ToFloat64(ctx, &rd, argv[2]);
      var radius = (float)rd;
      WithDraw(() => Draw.ingame.WireCapsule(start, end, radius, s_drawColor));

      SetUndefined(outU, outTag);
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void Draw_CircleXZ(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      if (argc < 2)
      {
        SetUndefined(outU, outTag);
        return;
      }

      var center = JsStateExtensions.JsObjectToFloat3(ctx, argv[0]);
      double rd;
      QJS.JS_ToFloat64(ctx, &rd, argv[1]);
      var radius = (float)rd;
      WithDraw(() => Draw.ingame.xz.Circle(center, radius, s_drawColor));

      SetUndefined(outU, outTag);
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void Draw_SolidBox(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      if (argc < 2)
      {
        SetUndefined(outU, outTag);
        return;
      }

      var center = JsStateExtensions.JsObjectToFloat3(ctx, argv[0]);
      var size = JsStateExtensions.JsObjectToFloat3(ctx, argv[1]);
      WithDraw(() => Draw.ingame.SolidBox(center, size, s_drawColor));

      SetUndefined(outU, outTag);
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void Draw_SolidCircle(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      if (argc < 3)
      {
        SetUndefined(outU, outTag);
        return;
      }

      var center = JsStateExtensions.JsObjectToFloat3(ctx, argv[0]);
      var normal = JsStateExtensions.JsObjectToFloat3(ctx, argv[1]);
      double rd;
      QJS.JS_ToFloat64(ctx, &rd, argv[2]);
      var radius = (float)rd;
      WithDraw(() => Draw.ingame.SolidCircle(center, normal, radius, s_drawColor));

      SetUndefined(outU, outTag);
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void Draw_Label2D(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      if (argc < 2)
      {
        SetUndefined(outU, outTag);
        return;
      }

      var position = JsStateExtensions.JsObjectToFloat3(ctx, argv[0]);
      var text = QJS.ToManagedString(ctx, argv[1]);
      if (text != null)
        WithDraw(() => Draw.ingame.Label2D(position, text, s_drawColor));

      SetUndefined(outU, outTag);
    }

    static unsafe void RegisterDrawFunctions(JSContext ctx, JSValue ns)
    {
      AddFunction(ctx, ns, "setColor", Draw_SetColor, 4);
      AddFunction(ctx, ns, "withDuration", Draw_WithDuration, 1);
      AddFunction(ctx, ns, "line", Draw_Line, 2);
      AddFunction(ctx, ns, "ray", Draw_Ray, 2);
      AddFunction(ctx, ns, "arrow", Draw_Arrow, 2);
      AddFunction(ctx, ns, "wireSphere", Draw_WireSphere, 2);
      AddFunction(ctx, ns, "wireBox", Draw_WireBox, 2);
      AddFunction(ctx, ns, "wireCapsule", Draw_WireCapsule, 3);
      AddFunction(ctx, ns, "circleXz", Draw_CircleXZ, 2);
      AddFunction(ctx, ns, "solidBox", Draw_SolidBox, 2);
      AddFunction(ctx, ns, "solidCircle", Draw_SolidCircle, 3);
      AddFunction(ctx, ns, "label2d", Draw_Label2D, 2);
    }
  }
}
