namespace UnityJS.Runtime
{
  using System;
  using System.Collections.Generic;

  /// <summary>
  /// Serves synthetic ES module source for <c>unity.js/*</c> specifiers.
  /// Each module re-exports from globalThis registrations set up by the P/Invoke bridges.
  /// </summary>
  public static class JsBuiltinModules
  {
    static readonly Dictionary<string, string> s_cache = new();

    /// <summary>
    /// Set by JsComponentRegistry to provide global names for the unity.js/components module.
    /// </summary>
    public static Func<IReadOnlyCollection<string>> GlobalNamesProvider;

    public static string GetModuleSource(string specifier)
    {
      // unity.js/components is dynamic — never cache
      if (specifier == "unity.js/components")
        return BuildComponentsModule();

      if (s_cache.TryGetValue(specifier, out var cached))
        return cached;

      var source = BuildStaticModule(specifier);
      if (source != null)
        s_cache[specifier] = source;
      return source;
    }

    public static void ClearCache()
    {
      s_cache.Clear();
    }

    static string BuildStaticModule(string specifier)
    {
      switch (specifier)
      {
        case "unity.js/types":
          return @"
const g = globalThis;
export const float2 = g.float2;
export const float3 = g.float3;
export const float4 = g.float4;
export const add = g.add;
export const sub = g.sub;
export const mul = g.mul;
export const div = g.div;
export const eq = g.eq;
";

        case "unity.js/ecs":
          return @"
const e = globalThis.ecs;
export const query = e.query.bind(e);
export const define = e.define.bind(e);
export const add = e.add.bind(e);
export const remove = e.remove.bind(e);
export const has = e.has.bind(e);
export const get = e.get.bind(e);
export const Component = e.Component;
";

        case "unity.js/math":
          return @"
const m = globalThis.math;
export const sin = m.sin.bind(m);
export const cos = m.cos.bind(m);
export const tan = m.tan.bind(m);
export const asin = m.asin.bind(m);
export const acos = m.acos.bind(m);
export const atan = m.atan.bind(m);
export const atan2 = m.atan2.bind(m);
export const sinh = m.sinh.bind(m);
export const cosh = m.cosh.bind(m);
export const tanh = m.tanh.bind(m);
export const floor = m.floor.bind(m);
export const ceil = m.ceil.bind(m);
export const round = m.round.bind(m);
export const trunc = m.trunc.bind(m);
export const frac = m.frac.bind(m);
export const sqrt = m.sqrt.bind(m);
export const rsqrt = m.rsqrt.bind(m);
export const exp = m.exp.bind(m);
export const exp2 = m.exp2.bind(m);
export const log = m.log.bind(m);
export const log2 = m.log2.bind(m);
export const log10 = m.log10.bind(m);
export const abs = m.abs.bind(m);
export const sign = m.sign.bind(m);
export const saturate = m.saturate.bind(m);
export const radians = m.radians.bind(m);
export const degrees = m.degrees.bind(m);
export const min = m.min.bind(m);
export const max = m.max.bind(m);
export const pow = m.pow.bind(m);
export const step = m.step.bind(m);
export const lerp = m.lerp.bind(m);
export const clamp = m.clamp.bind(m);
export const smoothstep = m.smoothstep.bind(m);
export const unlerp = m.unlerp.bind(m);
export const remap = m.remap.bind(m);
export const dot = m.dot.bind(m);
export const length = m.length.bind(m);
export const lengthsq = m.lengthsq.bind(m);
export const distance = m.distance.bind(m);
export const distancesq = m.distancesq.bind(m);
export const normalize = m.normalize.bind(m);
export const cross = m.cross.bind(m);
export const reflect = m.reflect.bind(m);
export const refract = m.refract.bind(m);
export const random = m.random.bind(m);
export const PI = m.PI;
export const E = m.E;
export const EPSILON = m.EPSILON;
export const INFINITY = m.INFINITY;
";

        case "unity.js/input":
          return @"
const i = globalThis.input;
export const readValue = i.readValue.bind(i);
export const wasPressed = i.wasPressed.bind(i);
export const isHeld = i.isHeld.bind(i);
export const wasReleased = i.wasReleased.bind(i);
";

        case "unity.js/entities":
          return @"
const e = globalThis.entities;
export const create = e.create.bind(e);
export const destroy = e.destroy.bind(e);
export const addScript = e.addScript.bind(e);
export const hasScript = e.hasScript.bind(e);
export const removeComponent = e.removeComponent.bind(e);
";

        case "unity.js/log":
          return @"
const l = globalThis.log;
export const debug = l.debug.bind(l);
export const info = l.info.bind(l);
export const warning = l.warning.bind(l);
export const error = l.error.bind(l);
export const trace = l.trace.bind(l);
";

        case "unity.js/colors":
          return @"
const c = globalThis.colors;
export const rgbToHsv = c.rgbToHsv.bind(c);
export const hsvToRgb = c.hsvToRgb.bind(c);
export const oklabToRgb = c.oklabToRgb.bind(c);
export const rgbToOklab = c.rgbToOklab.bind(c);
";

        case "unity.js/draw":
          return @"
const d = globalThis.draw;
export const setColor = d.setColor.bind(d);
export const withDuration = d.withDuration.bind(d);
export const line = d.line.bind(d);
export const ray = d.ray.bind(d);
export const arrow = d.arrow.bind(d);
export const wireSphere = d.wireSphere.bind(d);
export const wireBox = d.wireBox.bind(d);
export const wireCapsule = d.wireCapsule.bind(d);
export const circleXz = d.circleXz.bind(d);
export const solidBox = d.solidBox.bind(d);
export const solidCircle = d.solidCircle.bind(d);
export const label2d = d.label2d.bind(d);
";

        case "unity.js/spatial":
          return @"
const s = globalThis.spatial;
export const add = s.add.bind(s);
export const get = s.get.bind(s);
export const query = s.query.bind(s);
export const sphere = s.sphere.bind(s);
export const box = s.box.bind(s);
";

        case "unity.js/system":
          return @"
const s = globalThis.system;
export const deltaTime = s.deltaTime.bind(s);
export const time = s.time.bind(s);
export const random = s.random.bind(s);
export const randomInt = s.randomInt.bind(s);
";

        default:
          return null;
      }
    }

    static string BuildComponentsModule()
    {
      var names = GlobalNamesProvider?.Invoke();
      if (names == null || names.Count == 0)
        return "// no components registered\n";

      var sb = new System.Text.StringBuilder();
      sb.AppendLine("const g = globalThis;");
      foreach (var name in names)
      {
        sb.Append("export const ");
        sb.Append(name);
        sb.Append(" = g.");
        sb.Append(name);
        sb.AppendLine(";");
      }

      return sb.ToString();
    }
  }
}
