import { Component, get } from 'unity.js/ecs'
import * as math from 'unity.js/math'
import { LocalTransform } from 'unity.js/components'
import { float3, sub, add, mul } from 'unity.js/types'

// E2E test fixture: deterministic wanderer with no randomness.
// Cycles through 5 hardcoded waypoints at constant speed.
//
// INPUT:  speed = 3, waypoints = [(2,0,0), (0,0,2), (-2,0,0), (0,0,-2), (2,0,0)]
// OUTPUT: _e2e_wander[eid] = { waypointIndex, totalDist }
//
// Assertions (first principles):
//   speed=3, waypoint distance=2√2≈2.83 → time per leg ≈ 0.94s
//   In 5 seconds: ~5.3 legs → waypointIndex >= 4
//   Every entity must have moved > 1.0 unit from origin

const _g = globalThis as Record<string, any>
if (!_g._e2e_wander) _g._e2e_wander = {}

const WAYPOINTS: float3[] = [
  float3(2, 0, 0),
  float3(0, 0, 2),
  float3(-2, 0, 0),
  float3(0, 0, -2),
  float3(2, 0, 0),
]

export default class E2EWanderer extends Component {
  public speed = 3
  private waypointIndex = 0
  private totalDist = 0
  private prevPos: float3 = float3.zero

  start(): void {
    const lt = get(LocalTransform, this.entity)
    if (!lt) return
    this.prevPos = float3(lt.Position.x, lt.Position.y, lt.Position.z)
    _g._e2e_wander[this.entity] = this
  }

  update(dt: number): void {
    const lt = get(LocalTransform, this.entity)
    if (!lt) return

    const pos = lt.Position
    const target = WAYPOINTS[this.waypointIndex % WAYPOINTS.length]
    const delta = sub(target, pos)
    const dist = math.length(delta)

    if (dist < 0.1) {
      // Reached waypoint — advance immediately (no pause)
      this.waypointIndex++
    } else {
      // Move toward target
      const step = math.min(this.speed * dt, dist)
      lt.Position = add(pos, mul(delta, step / dist))
    }

    // Track cumulative displacement
    const moved = math.distance(lt.Position, this.prevPos)
    this.totalDist += moved
    this.prevPos = float3(lt.Position.x, lt.Position.y, lt.Position.z)
  }
}
