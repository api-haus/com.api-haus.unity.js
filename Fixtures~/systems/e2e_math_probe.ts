// E2E: exercises math bridge with known analytic values. Runs once.

var _e2e_math_ran = false

export function onUpdate(): void {
  if (_e2e_math_ran) return

  const math = (globalThis as any).math
  const float2 = (globalThis as any).float2
  const float3 = (globalThis as any).float3
  if (!math?.sin || !float3) return

  _e2e_math_ran = true
  const _g = globalThis as Record<string, any>

  _g._e2e_math = {
    dot_ortho: math.dot(float3(1, 0, 0), float3(0, 1, 0)),
    dot_para: math.dot(float3(3, 0, 0), float3(2, 0, 0)),
    len_345: math.length(float3(3, 4, 0)),
    norm_z: math.normalize(float3(0, 0, 5)),
    norm_z_len: math.length(math.normalize(float3(0, 0, 5))),
    lerp_0: math.lerp(0, 10, 0),
    lerp_1: math.lerp(0, 10, 1),
    lerp_half: math.lerp(0, 10, 0.5),
    cross_xy: math.cross(float3(1, 0, 0), float3(0, 1, 0)),
    cross_yx: math.cross(float3(0, 1, 0), float3(1, 0, 0)),
    sin_0: math.sin(0),
    sin_half_pi: math.sin(math.PI / 2),
    cos_0: math.cos(0),
    clamp_over: math.clamp(15, 0, 10),
    clamp_under: math.clamp(-5, 0, 10),
    clamp_in: math.clamp(5, 0, 10),
    dist_345: math.distance(float3(0, 0, 0), float3(3, 4, 0)),
    f2_components: float2(3, 4),
    f2_splat: float2(5),
    f3_swizzle_xz: float3(1, 2, 3).xz,
  }
}
