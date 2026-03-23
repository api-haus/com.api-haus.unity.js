import { Component } from 'unity.js/ecs'

// E2E: exercises entities.create() and entities.destroy() in start().

const _g = globalThis as Record<string, any>
if (!_g._e2e_ent) _g._e2e_ent = {}

export default class E2EEntityOps extends Component {
  start(): void {
    const entities = _g.entities
    const float3 = _g.float3
    if (!entities || !float3) { _g._e2e_ent[this.entity] = { error: 'bridges not available' }; return }

    const r: Record<string, any> = {}

    try {
      const id1 = entities.create()
      r.createdId = id1

      const id2 = entities.create(float3(5, 10, 15))
      r.createdWithPosId = id2

      const id3 = entities.create()
      r.destroyTargetId = id3
      r.destroyResult = entities.destroy(id3)
    } catch (e: any) {
      r.error = e?.message ?? String(e)
    }

    _g._e2e_ent[this.entity] = r
  }
}
