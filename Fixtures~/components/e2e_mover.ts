import { Component, get } from 'unity.js/ecs'
import { LocalTransform } from 'unity.js/components'
import { float3, add, mul } from 'unity.js/types'

// E2E test fixture: moves entity by (1,0,0)*dt each frame via ecs.get() write-back.
// Proves query accessor write-back persists to ECS LocalTransform.

const _g = globalThis as Record<string, any>
if (!_g._e2e_mover) _g._e2e_mover = {}

export default class E2EMover extends Component {
  update(dt: number): void {
    const lt = get(LocalTransform, this.entity)
    if (!lt) return
    lt.Position = add(lt.Position, mul(float3(1, 0, 0), dt))
    _g._e2e_mover[this.entity] = true
  }
}
