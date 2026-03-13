namespace UnityJS.Entities.Core
{
  using Unity.Mathematics;

  static partial class JsMathCompiled
  {
  // ── Componentwise unary (float/float2/float3/float4) ──

  [JsCompile("math", "sin")]
  static float sin_f(float x) => math.sin(x);

  [JsCompile("math", "sin")]
  static float2 sin_f2(float2 x) => math.sin(x);

  [JsCompile("math", "sin")]
  static float3 sin_f3(float3 x) => math.sin(x);

  [JsCompile("math", "sin")]
  static float4 sin_f4(float4 x) => math.sin(x);

  [JsCompile("math", "cos")]
  static float cos_f(float x) => math.cos(x);

  [JsCompile("math", "cos")]
  static float2 cos_f2(float2 x) => math.cos(x);

  [JsCompile("math", "cos")]
  static float3 cos_f3(float3 x) => math.cos(x);

  [JsCompile("math", "cos")]
  static float4 cos_f4(float4 x) => math.cos(x);

  [JsCompile("math", "tan")]
  static float tan_f(float x) => math.tan(x);

  [JsCompile("math", "tan")]
  static float2 tan_f2(float2 x) => math.tan(x);

  [JsCompile("math", "tan")]
  static float3 tan_f3(float3 x) => math.tan(x);

  [JsCompile("math", "tan")]
  static float4 tan_f4(float4 x) => math.tan(x);

  [JsCompile("math", "asin")]
  static float asin_f(float x) => math.asin(x);

  [JsCompile("math", "asin")]
  static float2 asin_f2(float2 x) => math.asin(x);

  [JsCompile("math", "asin")]
  static float3 asin_f3(float3 x) => math.asin(x);

  [JsCompile("math", "asin")]
  static float4 asin_f4(float4 x) => math.asin(x);

  [JsCompile("math", "acos")]
  static float acos_f(float x) => math.acos(x);

  [JsCompile("math", "acos")]
  static float2 acos_f2(float2 x) => math.acos(x);

  [JsCompile("math", "acos")]
  static float3 acos_f3(float3 x) => math.acos(x);

  [JsCompile("math", "acos")]
  static float4 acos_f4(float4 x) => math.acos(x);

  [JsCompile("math", "atan")]
  static float atan_f(float x) => math.atan(x);

  [JsCompile("math", "atan")]
  static float2 atan_f2(float2 x) => math.atan(x);

  [JsCompile("math", "atan")]
  static float3 atan_f3(float3 x) => math.atan(x);

  [JsCompile("math", "atan")]
  static float4 atan_f4(float4 x) => math.atan(x);

  [JsCompile("math", "sinh")]
  static float sinh_f(float x) => math.sinh(x);

  [JsCompile("math", "sinh")]
  static float2 sinh_f2(float2 x) => math.sinh(x);

  [JsCompile("math", "sinh")]
  static float3 sinh_f3(float3 x) => math.sinh(x);

  [JsCompile("math", "sinh")]
  static float4 sinh_f4(float4 x) => math.sinh(x);

  [JsCompile("math", "cosh")]
  static float cosh_f(float x) => math.cosh(x);

  [JsCompile("math", "cosh")]
  static float2 cosh_f2(float2 x) => math.cosh(x);

  [JsCompile("math", "cosh")]
  static float3 cosh_f3(float3 x) => math.cosh(x);

  [JsCompile("math", "cosh")]
  static float4 cosh_f4(float4 x) => math.cosh(x);

  [JsCompile("math", "tanh")]
  static float tanh_f(float x) => math.tanh(x);

  [JsCompile("math", "tanh")]
  static float2 tanh_f2(float2 x) => math.tanh(x);

  [JsCompile("math", "tanh")]
  static float3 tanh_f3(float3 x) => math.tanh(x);

  [JsCompile("math", "tanh")]
  static float4 tanh_f4(float4 x) => math.tanh(x);

  [JsCompile("math", "floor")]
  static float floor_f(float x) => math.floor(x);

  [JsCompile("math", "floor")]
  static float2 floor_f2(float2 x) => math.floor(x);

  [JsCompile("math", "floor")]
  static float3 floor_f3(float3 x) => math.floor(x);

  [JsCompile("math", "floor")]
  static float4 floor_f4(float4 x) => math.floor(x);

  [JsCompile("math", "ceil")]
  static float ceil_f(float x) => math.ceil(x);

  [JsCompile("math", "ceil")]
  static float2 ceil_f2(float2 x) => math.ceil(x);

  [JsCompile("math", "ceil")]
  static float3 ceil_f3(float3 x) => math.ceil(x);

  [JsCompile("math", "ceil")]
  static float4 ceil_f4(float4 x) => math.ceil(x);

  [JsCompile("math", "round")]
  static float round_f(float x) => math.round(x);

  [JsCompile("math", "round")]
  static float2 round_f2(float2 x) => math.round(x);

  [JsCompile("math", "round")]
  static float3 round_f3(float3 x) => math.round(x);

  [JsCompile("math", "round")]
  static float4 round_f4(float4 x) => math.round(x);

  [JsCompile("math", "trunc")]
  static float trunc_f(float x) => math.trunc(x);

  [JsCompile("math", "trunc")]
  static float2 trunc_f2(float2 x) => math.trunc(x);

  [JsCompile("math", "trunc")]
  static float3 trunc_f3(float3 x) => math.trunc(x);

  [JsCompile("math", "trunc")]
  static float4 trunc_f4(float4 x) => math.trunc(x);

  [JsCompile("math", "frac")]
  static float frac_f(float x) => math.frac(x);

  [JsCompile("math", "frac")]
  static float2 frac_f2(float2 x) => math.frac(x);

  [JsCompile("math", "frac")]
  static float3 frac_f3(float3 x) => math.frac(x);

  [JsCompile("math", "frac")]
  static float4 frac_f4(float4 x) => math.frac(x);

  [JsCompile("math", "sqrt")]
  static float sqrt_f(float x) => math.sqrt(x);

  [JsCompile("math", "sqrt")]
  static float2 sqrt_f2(float2 x) => math.sqrt(x);

  [JsCompile("math", "sqrt")]
  static float3 sqrt_f3(float3 x) => math.sqrt(x);

  [JsCompile("math", "sqrt")]
  static float4 sqrt_f4(float4 x) => math.sqrt(x);

  [JsCompile("math", "rsqrt")]
  static float rsqrt_f(float x) => math.rsqrt(x);

  [JsCompile("math", "rsqrt")]
  static float2 rsqrt_f2(float2 x) => math.rsqrt(x);

  [JsCompile("math", "rsqrt")]
  static float3 rsqrt_f3(float3 x) => math.rsqrt(x);

  [JsCompile("math", "rsqrt")]
  static float4 rsqrt_f4(float4 x) => math.rsqrt(x);

  [JsCompile("math", "exp")]
  static float exp_f(float x) => math.exp(x);

  [JsCompile("math", "exp")]
  static float2 exp_f2(float2 x) => math.exp(x);

  [JsCompile("math", "exp")]
  static float3 exp_f3(float3 x) => math.exp(x);

  [JsCompile("math", "exp")]
  static float4 exp_f4(float4 x) => math.exp(x);

  [JsCompile("math", "exp2")]
  static float exp2_f(float x) => math.exp2(x);

  [JsCompile("math", "exp2")]
  static float2 exp2_f2(float2 x) => math.exp2(x);

  [JsCompile("math", "exp2")]
  static float3 exp2_f3(float3 x) => math.exp2(x);

  [JsCompile("math", "exp2")]
  static float4 exp2_f4(float4 x) => math.exp2(x);

  [JsCompile("math", "log")]
  static float log_f(float x) => math.log(x);

  [JsCompile("math", "log")]
  static float2 log_f2(float2 x) => math.log(x);

  [JsCompile("math", "log")]
  static float3 log_f3(float3 x) => math.log(x);

  [JsCompile("math", "log")]
  static float4 log_f4(float4 x) => math.log(x);

  [JsCompile("math", "log2")]
  static float log2_f(float x) => math.log2(x);

  [JsCompile("math", "log2")]
  static float2 log2_f2(float2 x) => math.log2(x);

  [JsCompile("math", "log2")]
  static float3 log2_f3(float3 x) => math.log2(x);

  [JsCompile("math", "log2")]
  static float4 log2_f4(float4 x) => math.log2(x);

  [JsCompile("math", "log10")]
  static float log10_f(float x) => math.log10(x);

  [JsCompile("math", "log10")]
  static float2 log10_f2(float2 x) => math.log10(x);

  [JsCompile("math", "log10")]
  static float3 log10_f3(float3 x) => math.log10(x);

  [JsCompile("math", "log10")]
  static float4 log10_f4(float4 x) => math.log10(x);

  [JsCompile("math", "abs")]
  static float abs_f(float x) => math.abs(x);

  [JsCompile("math", "abs")]
  static float2 abs_f2(float2 x) => math.abs(x);

  [JsCompile("math", "abs")]
  static float3 abs_f3(float3 x) => math.abs(x);

  [JsCompile("math", "abs")]
  static float4 abs_f4(float4 x) => math.abs(x);

  [JsCompile("math", "sign")]
  static float sign_f(float x) => math.sign(x);

  [JsCompile("math", "sign")]
  static float2 sign_f2(float2 x) => math.sign(x);

  [JsCompile("math", "sign")]
  static float3 sign_f3(float3 x) => math.sign(x);

  [JsCompile("math", "sign")]
  static float4 sign_f4(float4 x) => math.sign(x);

  [JsCompile("math", "saturate")]
  static float saturate_f(float x) => math.saturate(x);

  [JsCompile("math", "saturate")]
  static float2 saturate_f2(float2 x) => math.saturate(x);

  [JsCompile("math", "saturate")]
  static float3 saturate_f3(float3 x) => math.saturate(x);

  [JsCompile("math", "saturate")]
  static float4 saturate_f4(float4 x) => math.saturate(x);

  [JsCompile("math", "radians")]
  static float radians_f(float x) => math.radians(x);

  [JsCompile("math", "radians")]
  static float2 radians_f2(float2 x) => math.radians(x);

  [JsCompile("math", "radians")]
  static float3 radians_f3(float3 x) => math.radians(x);

  [JsCompile("math", "radians")]
  static float4 radians_f4(float4 x) => math.radians(x);

  [JsCompile("math", "degrees")]
  static float degrees_f(float x) => math.degrees(x);

  [JsCompile("math", "degrees")]
  static float2 degrees_f2(float2 x) => math.degrees(x);

  [JsCompile("math", "degrees")]
  static float3 degrees_f3(float3 x) => math.degrees(x);

  [JsCompile("math", "degrees")]
  static float4 degrees_f4(float4 x) => math.degrees(x);

  // ── Componentwise binary (both args same type) ──

  [JsCompile("math", "min")]
  static float min_f(float a, float b) => math.min(a, b);

  [JsCompile("math", "min")]
  static float2 min_f2(float2 a, float2 b) => math.min(a, b);

  [JsCompile("math", "min")]
  static float3 min_f3(float3 a, float3 b) => math.min(a, b);

  [JsCompile("math", "min")]
  static float4 min_f4(float4 a, float4 b) => math.min(a, b);

  [JsCompile("math", "max")]
  static float max_f(float a, float b) => math.max(a, b);

  [JsCompile("math", "max")]
  static float2 max_f2(float2 a, float2 b) => math.max(a, b);

  [JsCompile("math", "max")]
  static float3 max_f3(float3 a, float3 b) => math.max(a, b);

  [JsCompile("math", "max")]
  static float4 max_f4(float4 a, float4 b) => math.max(a, b);

  [JsCompile("math", "pow")]
  static float pow_f(float a, float b) => math.pow(a, b);

  [JsCompile("math", "pow")]
  static float2 pow_f2(float2 a, float2 b) => math.pow(a, b);

  [JsCompile("math", "pow")]
  static float3 pow_f3(float3 a, float3 b) => math.pow(a, b);

  [JsCompile("math", "pow")]
  static float4 pow_f4(float4 a, float4 b) => math.pow(a, b);

  [JsCompile("math", "step")]
  static float step_f(float a, float b) => math.step(a, b);

  [JsCompile("math", "step")]
  static float2 step_f2(float2 a, float2 b) => math.step(a, b);

  [JsCompile("math", "step")]
  static float3 step_f3(float3 a, float3 b) => math.step(a, b);

  [JsCompile("math", "step")]
  static float4 step_f4(float4 a, float4 b) => math.step(a, b);

  // ── Interpolation (a,b match type; t always scalar) ──

  [JsCompile("math", "lerp")]
  static float lerp_f(float a, float b, float t) => math.lerp(a, b, t);

  [JsCompile("math", "lerp")]
  static float2 lerp_f2(float2 a, float2 b, float t) => math.lerp(a, b, t);

  [JsCompile("math", "lerp")]
  static float3 lerp_f3(float3 a, float3 b, float t) => math.lerp(a, b, t);

  [JsCompile("math", "lerp")]
  static float4 lerp_f4(float4 a, float4 b, float t) => math.lerp(a, b, t);

  [JsCompile("math", "clamp")]
  static float clamp_f(float x, float a, float b) => math.clamp(x, a, b);

  [JsCompile("math", "clamp")]
  static float2 clamp_f2(float2 x, float2 a, float2 b) => math.clamp(x, a, b);

  [JsCompile("math", "clamp")]
  static float3 clamp_f3(float3 x, float3 a, float3 b) => math.clamp(x, a, b);

  [JsCompile("math", "clamp")]
  static float4 clamp_f4(float4 x, float4 a, float4 b) => math.clamp(x, a, b);

  [JsCompile("math", "smoothstep")]
  static float smoothstep_f(float a, float b, float x) => math.smoothstep(a, b, x);

  [JsCompile("math", "smoothstep")]
  static float2 smoothstep_f2(float2 a, float2 b, float2 x) => math.smoothstep(a, b, x);

  [JsCompile("math", "smoothstep")]
  static float3 smoothstep_f3(float3 a, float3 b, float3 x) => math.smoothstep(a, b, x);

  [JsCompile("math", "smoothstep")]
  static float4 smoothstep_f4(float4 a, float4 b, float4 x) => math.smoothstep(a, b, x);

  // ── Scalar-only ──

  [JsCompile("math", "atan2")]
  static float atan2_f(float y, float x) => math.atan2(y, x);

  [JsCompile("math", "unlerp")]
  static float unlerp_f(float a, float b, float x) => math.unlerp(a, b, x);

  [JsCompile("math", "remap")]
  static float remap_f(float a, float b, float c, float d, float x) => math.remap(a, b, c, d, x);

  // ── Vector → scalar (float3 only) ──

  [JsCompile("math", "dot")]
  static float dot_f3(float3 a, float3 b) => math.dot(a, b);

  [JsCompile("math", "length")]
  static float length_f3(float3 v) => math.length(v);

  [JsCompile("math", "lengthsq")]
  static float lengthsq_f3(float3 v) => math.lengthsq(v);

  [JsCompile("math", "distance")]
  static float distance_f3(float3 a, float3 b) => math.distance(a, b);

  [JsCompile("math", "distancesq")]
  static float distancesq_f3(float3 a, float3 b) => math.distancesq(a, b);

  // ── Vector → vector (float3 only) ──

  [JsCompile("math", "normalize")]
  static float3 normalize_f3(float3 v) => math.normalizesafe(v);

  [JsCompile("math", "cross")]
  static float3 cross_f3(float3 a, float3 b) => math.cross(a, b);

  [JsCompile("math", "reflect")]
  static float3 reflect_f3(float3 i, float3 n) => math.reflect(i, n);

  [JsCompile("math", "refract")]
  static float3 refract_f3(float3 i, float3 n, float eta) => math.refract(i, n, eta);
  }
}
