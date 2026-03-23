import { Component, get } from 'unity.js/ecs'
import * as math from 'unity.js/math'
import * as spatial from 'unity.js/spatial'
import { LocalTransform } from 'unity.js/components'
import { float3 } from 'unity.js/types'

// E2E test fixture: tracks cumulative displacement of a dynamic body.
// Also registers itself as a spatial agent with tag "dynamic_bodies".
//
// INPUT:  entity at known position
// OUTPUT: _e2e_bodies[eid] = { totalDist, frameCount }

const _g = globalThis as Record<string, any>
if (!_g._e2e_bodies) _g._e2e_bodies = {}

export default class E2EBodyTracker extends Component {
  private totalDist = 0
  private frameCount = 0
  private prevPos: float3 = float3.zero

  start(): void {
    const lt = get(LocalTransform, this.entity)
    if (!lt) return

    this.prevPos = float3(lt.Position.x, lt.Position.y, lt.Position.z)

    // Register as spatial agent so triggers can find us
    spatial.add(this.entity, 'dynamic_bodies', spatial.sphere(0.5))

    _g._e2e_bodies[this.entity] = this
  }

  update(dt: number): void {
    const lt = get(LocalTransform, this.entity)
    if (!lt) return

    const moved = math.distance(lt.Position, this.prevPos)
    this.totalDist += moved
    this.frameCount++
    this.prevPos = float3(lt.Position.x, lt.Position.y, lt.Position.z)
  }
}
