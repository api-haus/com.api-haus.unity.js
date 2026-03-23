// E2E: exercises color conversion bridge with known analytic values.
// Accesses bridges via globalThis at call time to avoid TDZ on module load.

var _e2e_color_ran = false

export function onUpdate(): void {
  if (_e2e_color_ran) return

  const colors = (globalThis as any).colors
  const math = (globalThis as any).math
  const float3 = (globalThis as any).float3
  if (!colors?.hsvToRgb || !math?.sin || !float3) return

  _e2e_color_ran = true

  const red = colors.hsvToRgb(0, 1, 1)
  const green = colors.hsvToRgb(120, 1, 1)
  const blue = colors.hsvToRgb(240, 1, 1)

  const src = float3(0.8, 0.3, 0.5)
  const hsv = colors.rgbToHsv(src)
  const back = colors.hsvToRgb(hsv.h, hsv.s, hsv.v)
  const rtError = math.distance(src, back)

  const oklSrc = float3(0.5, 0.2, 0.7)
  const lab = colors.rgbToOklab(oklSrc)
  const oklBack = colors.oklabToRgb(lab)
  const oklError = math.distance(oklSrc, oklBack)

  const _g = globalThis as Record<string, any>
  _g._e2e_color = {
    red_r: red.x, red_g: red.y, red_b: red.z,
    green_r: green.x, green_g: green.y, green_b: green.z,
    blue_r: blue.x, blue_g: blue.y, blue_b: blue.z,
    rtError,
    oklError,
  }
}
