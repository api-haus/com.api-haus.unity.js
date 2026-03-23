import { Component, get } from 'unity.js/ecs'
import * as math from 'unity.js/math'
import { LocalTransform } from 'unity.js/components'
import { float3, add, mul } from 'unity.js/types'

// E2E test fixture: character that moves forward at constant speed.
// Uses input.readValue("Move") when available, falls back to constant (0,0,1).
//
// INPUT:  speed = 4, direction = (0, 0, 1) constant
// OUTPUT: _e2e_char[eid] = { frameCount, totalDist, inputWasNull }
//
// Assertions (first principles):
//   speed=4, duration=3s → displacement.z ≈ 12 (± frame timing tolerance)
//   totalDist ≈ 12 ± 2

const _g = globalThis as Record<string, any>
if (!_g._e2e_char) _g._e2e_char = {}

let _hasInput = false
try {
  // Only import input if integration is available
  const inp = (globalThis as any).input
  _hasInput = typeof inp?.readValue === 'function'
} catch { /* no input integration */ }

export default class E2ECharacter extends Component {
  public speed = 4
  private frameCount = 0
  private totalDist = 0
  private inputWasNull = false
  private prevPos: float3 = float3.zero

  start(): void {
    const lt = get(LocalTransform, this.entity)
    if (!lt) return
    this.prevPos = float3(lt.Position.x, lt.Position.y, lt.Position.z)
    _g._e2e_char[this.entity] = this
  }

  update(dt: number): void {
    const lt = get(LocalTransform, this.entity)
    if (!lt) return

    // Try to read input; fall back to constant forward
    let dir = float3(0, 0, 1)
    if (_hasInput) {
      const move = (globalThis as any).input.readValue('Move')
      if (move == null) {
        this.inputWasNull = true
        // Keep constant forward as fallback
      } else {
        dir = float3(move.x ?? move, 0, move.y ?? 0)
      }
    } else {
      this.inputWasNull = true
    }

    // Apply movement
    const len = math.length(dir)
    if (len > 0.01) {
      const normalized = mul(dir, 1.0 / len)
      lt.Position = add(lt.Position, mul(normalized, this.speed * dt))
    }

    // Track metrics
    this.frameCount++
    const moved = math.distance(lt.Position, this.prevPos)
    this.totalDist += moved
    this.prevPos = float3(lt.Position.x, lt.Position.y, lt.Position.z)
  }
}
