namespace UnityJS.Entities.Core
{
  using System.Text;
  using AOT;
  using Unity.Mathematics;
  using UnityJS.QJS;
  using UnityJS.Runtime;

  static partial class JsColorsBridge
  {
  [JsCompile("colors", "hsvToRgb")]
  static float3 hsvToRgb(float h, float s, float v)
  {
    h = ((h % 360f) + 360f) % 360f;
    var c = v * s;
    var x = c * (1f - math.abs((h / 60f) % 2f - 1f));
    var m = v - c;

    float3 rgb;
    if (h < 60f)
      rgb = new float3(c, x, 0f);
    else if (h < 120f)
      rgb = new float3(x, c, 0f);
    else if (h < 180f)
      rgb = new float3(0f, c, x);
    else if (h < 240f)
      rgb = new float3(0f, x, c);
    else if (h < 300f)
      rgb = new float3(x, 0f, c);
    else
      rgb = new float3(c, 0f, x);

    return rgb + m;
  }

  [JsCompile("colors", "oklabToRgb")]
  static float3 oklabToRgb(float3 lab)
  {
    var lp = lab.x + 0.3963377774f * lab.y + 0.2158037573f * lab.z;
    var mp = lab.x - 0.1055613458f * lab.y - 0.0638541728f * lab.z;
    var sp = lab.x - 0.0894841775f * lab.y - 1.2914855480f * lab.z;

    var ll = lp * lp * lp;
    var mm = mp * mp * mp;
    var ss = sp * sp * sp;

    var r = +4.0767416621f * ll - 3.3077115913f * mm + 0.2309699292f * ss;
    var g = -1.2684380046f * ll + 2.6097574011f * mm - 0.3413193965f * ss;
    var b = -0.0041960863f * ll - 0.7034186147f * mm + 1.7076147010f * ss;

    return new float3(r, g, b);
  }

  [JsCompile("colors", "rgbToOklab")]
  static float3 rgbToOklab(float3 rgb)
  {
    var ll = 0.4122214708f * rgb.x + 0.5363325363f * rgb.y + 0.0514459929f * rgb.z;
    var mm = 0.2119034982f * rgb.x + 0.6806995451f * rgb.y + 0.1073969566f * rgb.z;
    var ss = 0.0883024619f * rgb.x + 0.2817188376f * rgb.y + 0.6299787005f * rgb.z;

    var lc = math.pow(math.max(ll, 0f), 1f / 3f);
    var mc = math.pow(math.max(mm, 0f), 1f / 3f);
    var sc = math.pow(math.max(ss, 0f), 1f / 3f);

    var L = 0.2104542553f * lc + 0.7936177850f * mc - 0.0040720468f * sc;
    var a = 1.9779984951f * lc - 2.4285922050f * mc + 0.4505937099f * sc;
    var bv = 0.0259040371f * lc + 0.7827717662f * mc - 0.8086757660f * sc;

    return new float3(L, a, bv);
  }

  // Custom return shape — Signature mode (manual bridge)
  [JsCompile("colors", "rgbToHsv", Signature = "(rgb: float3): {h: number, s: number, v: number}")]
  [MonoPInvokeCallback(typeof(QJSShimCallback))]
  static unsafe void rgbToHsv(
    JSContext ctx,
    long thisU,
    long thisTag,
    int argc,
    JSValue* argv,
    long* outU,
    long* outTag
  )
  {
    var rgb = JsStateExtensions.JsObjectToFloat3(ctx, argv[0]);
    var cMax = math.max(rgb.x, math.max(rgb.y, rgb.z));
    var cMin = math.min(rgb.x, math.min(rgb.y, rgb.z));
    var delta = cMax - cMin;

    float h = 0f;
    if (delta > 1e-6f)
    {
      if (cMax == rgb.x)
        h = 60f * (((rgb.y - rgb.z) / delta) % 6f);
      else if (cMax == rgb.y)
        h = 60f * ((rgb.z - rgb.x) / delta + 2f);
      else
        h = 60f * ((rgb.x - rgb.y) / delta + 4f);
    }
    if (h < 0f)
      h += 360f;

    var s = cMax > 1e-6f ? delta / cMax : 0f;

    var obj = QJS.JS_NewObject(ctx);
    var pHBytes = Encoding.UTF8.GetBytes("h\0");
    var pSBytes = Encoding.UTF8.GetBytes("s\0");
    var pVBytes = Encoding.UTF8.GetBytes("v\0");
    fixed (
      byte* pH = pHBytes,
        pS = pSBytes,
        pV = pVBytes
    )
    {
      QJS.JS_SetPropertyStr(ctx, obj, pH, QJS.NewFloat64(ctx, h));
      QJS.JS_SetPropertyStr(ctx, obj, pS, QJS.NewFloat64(ctx, s));
      QJS.JS_SetPropertyStr(ctx, obj, pV, QJS.NewFloat64(ctx, cMax));
    }
    *outU = obj.u;
    *outTag = obj.tag;
    }
  }
}
