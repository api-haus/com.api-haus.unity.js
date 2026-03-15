namespace UnityJS.Entities.Core
{
  using System.Runtime.InteropServices;
  using AOT;
  using Drawing;
  using QJS;
  using Runtime;
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
      s_currentDrawColor = new Color((float)dr, (float)dg, (float)db, (float)da);
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
      s_currentDuration = (float)d;
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
      if (s_currentDuration > 0)
        using (Draw.ingame.WithDuration(s_currentDuration))
        {
          Draw.ingame.Line(from, to, s_currentDrawColor);
        }
      else
        Draw.ingame.Line(from, to, s_currentDrawColor);

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
      if (s_currentDuration > 0)
        using (Draw.ingame.WithDuration(s_currentDuration))
        {
          Draw.ingame.Ray(origin, direction, s_currentDrawColor);
        }
      else
        Draw.ingame.Ray(origin, direction, s_currentDrawColor);

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
      if (s_currentDuration > 0)
        using (Draw.ingame.WithDuration(s_currentDuration))
        {
          Draw.ingame.Arrow(from, to, s_currentDrawColor);
        }
      else
        Draw.ingame.Arrow(from, to, s_currentDrawColor);

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
      double radius;
      QJS.JS_ToFloat64(ctx, &radius, argv[1]);
      if (s_currentDuration > 0)
        using (Draw.ingame.WithDuration(s_currentDuration))
        {
          Draw.ingame.WireSphere(center, (float)radius, s_currentDrawColor);
        }
      else
        Draw.ingame.WireSphere(center, (float)radius, s_currentDrawColor);

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
      if (s_currentDuration > 0)
        using (Draw.ingame.WithDuration(s_currentDuration))
        {
          Draw.ingame.WireBox(center, size, s_currentDrawColor);
        }
      else
        Draw.ingame.WireBox(center, size, s_currentDrawColor);

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
      double radius;
      QJS.JS_ToFloat64(ctx, &radius, argv[2]);
      if (s_currentDuration > 0)
        using (Draw.ingame.WithDuration(s_currentDuration))
        {
          Draw.ingame.WireCapsule(start, end, (float)radius, s_currentDrawColor);
        }
      else
        Draw.ingame.WireCapsule(start, end, (float)radius, s_currentDrawColor);

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
      double radius;
      QJS.JS_ToFloat64(ctx, &radius, argv[1]);
      if (s_currentDuration > 0)
        using (Draw.ingame.WithDuration(s_currentDuration))
        {
          Draw.ingame.xz.Circle(center, (float)radius, s_currentDrawColor);
        }
      else
        Draw.ingame.xz.Circle(center, (float)radius, s_currentDrawColor);

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
      if (s_currentDuration > 0)
        using (Draw.ingame.WithDuration(s_currentDuration))
        {
          Draw.ingame.SolidBox(center, size, s_currentDrawColor);
        }
      else
        Draw.ingame.SolidBox(center, size, s_currentDrawColor);

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
      double radius;
      QJS.JS_ToFloat64(ctx, &radius, argv[2]);
      if (s_currentDuration > 0)
        using (Draw.ingame.WithDuration(s_currentDuration))
        {
          Draw.ingame.SolidCircle(center, normal, (float)radius, s_currentDrawColor);
        }
      else
        Draw.ingame.SolidCircle(center, normal, (float)radius, s_currentDrawColor);

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
      var ptr = QJS.JS_ToCString(ctx, argv[1]);
      if (ptr != null)
      {
        var text = Marshal.PtrToStringUTF8((nint)ptr) ?? "";
        QJS.JS_FreeCString(ctx, ptr);
        if (s_currentDuration > 0)
          using (Draw.ingame.WithDuration(s_currentDuration))
          {
            Draw.ingame.Label2D(position, text, s_currentDrawColor);
          }
        else
          Draw.ingame.Label2D(position, text, s_currentDrawColor);
      }

      SetUndefined(outU, outTag);
    }

    static unsafe void RegisterDrawFunctions(JSContext ctx)
    {
      var ns = QJS.JS_NewObject(ctx);

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

      var global = QJS.JS_GetGlobalObject(ctx);
      SetNamespace(ctx, global, "draw", ns);
      QJS.JS_FreeValue(ctx, global);
    }
  }
}
