import { Component } from 'unity.js/ecs'

// E2E: exercises draw bridge functions through real component lifecycle.
// Calls all draw functions in start() with known parameters.

const _g = globalThis as Record<string, any>
if (!_g._e2e_draw) _g._e2e_draw = {}

export default class E2EDrawProbe extends Component {
  start(): void {
    const draw = _g.draw
    const float3 = _g.float3
    if (!draw || !float3) { _g._e2e_draw[this.entity] = { error: 'bridges not available' }; return }

    const r: Record<string, any> = { callCount: 0 }

    try {
      draw.setColor(1, 0, 0, 1)
      r.callCount++
      draw.line(float3(0, 0, 0), float3(1, 0, 0))
      r.callCount++
      draw.ray(float3(0, 0, 0), float3(0, 1, 0))
      r.callCount++
      draw.wireSphere(float3(0, 0, 0), 1)
      r.callCount++
      draw.wireBox(float3(0, 0, 0), float3(1, 1, 1))
      r.callCount++
      draw.solidBox(float3(0, 0, 0), float3(0.5, 0.5, 0.5))
      r.callCount++
      draw.circleXz(float3(0, 0, 0), 2)
      r.callCount++
      draw.arrow(float3(0, 0, 0), float3(0, 0, 3))
      r.callCount++
      r.success = true
    } catch (e: any) {
      r.error = e?.message ?? String(e)
    }

    _g._e2e_draw[this.entity] = r
  }
}
