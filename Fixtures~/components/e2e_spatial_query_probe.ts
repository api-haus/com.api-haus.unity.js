import { Component, get } from 'unity.js/ecs'
import { LocalTransform } from 'unity.js/components'

// E2E: exercises spatial.query() (synchronous, non-trigger).
// Registers self as spatial agent, then queries for nearby entities.

const _g = globalThis as Record<string, any>
if (!_g._e2e_sq) _g._e2e_sq = {}

export default class E2ESpatialQueryProbe extends Component {
  private queryCount = -1

  start(): void {
    const spatial = _g.spatial
    const float3 = _g.float3
    if (!spatial?.add || !spatial?.query || !float3) {
      _g._e2e_sq[this.entity] = { error: 'spatial not available' }
      return
    }

    // Register self as spatial agent
    spatial.add(this.entity, 'e2e_sq_tag', spatial.sphere(1.0))
    _g._e2e_sq[this.entity] = this
  }

  update(): void {
    const spatial = _g.spatial
    if (!spatial?.query) return

    // Query for all agents with our tag within radius 10
    const results = spatial.query('e2e_sq_tag', spatial.sphere(10.0))
    this.queryCount = Array.isArray(results) ? results.length : -2
  }
}
