import { Component, get } from 'unity.js/ecs'
import * as math from 'unity.js/math'
import * as spatial from 'unity.js/spatial'
import { LocalTransform } from 'unity.js/components'
import { float3, sub, add, mul } from 'unity.js/types'

// E2E test fixture: simplified spatial goo that pulls nearby dynamic bodies.
// Attaches a sphere trigger around itself, tracks overlaps, applies velocity.
//
// INPUT:  radius = 3.0, strength = 5
// OUTPUT: _e2e_goo[eid] = { overlapCount, totalEnters, totalExits }

const _g = globalThis as Record<string, any>
if (!_g._e2e_goo) _g._e2e_goo = {}

export default class E2ESpatialGoo extends Component {
  public radius = 3.0
  public strength = 5
  private trigger!: spatial.TriggerHandle
  private overlapping = new Set<entity>()
  private totalEnters = 0
  private totalExits = 0

  start(): void {
    const lt = get(LocalTransform, this.entity)
    if (!lt) return

    this.trigger = spatial.trigger(
      this.entity,
      'dynamic_bodies',
      spatial.sphere(this.radius * lt.Scale)
    )
      .on('enter', (other: entity) => {
        this.overlapping.add(other)
        this.totalEnters++
      })
      .on('exit', (other: entity) => {
        this.overlapping.delete(other)
        this.totalExits++
      })

    _g._e2e_goo[this.entity] = this
  }

  update(dt: number): void {
    const myLt = get(LocalTransform, this.entity)
    if (!myLt) return

    const center = myLt.Position

    for (const eid of this.overlapping) {
      const lt = get(LocalTransform, eid)
      if (!lt) continue

      // Pull body toward center
      const delta = sub(center, lt.Position)
      const dist = math.length(delta)
      if (dist > 0.01) {
        const dir = mul(delta, 1.0 / dist)
        const pull = math.min(this.strength * dt, dist)
        lt.Position = add(lt.Position, mul(dir, pull))
      }
    }
  }

  get overlapCount(): number {
    return this.overlapping.size
  }

  onDestroy(): void {
    if (this.trigger) this.trigger.destroy()
  }
}
