import { Component } from 'unity.js/ecs'
import { float3 } from 'unity.js/types'

// E2E test fixture: exercises entities.create() and entities.destroy() in start().
//
// INPUT:  entity spawned by SceneFixture
// OUTPUT: _e2e_ent[eid] = { createdId, createdWithPosId, destroyedId }

const _g = globalThis as Record<string, any>
if (!_g._e2e_ent) _g._e2e_ent = {}

export default class E2EEntityOps extends Component {
  start(): void {
    const entities = _g.entities
    if (!entities) { _g._e2e_ent[this.entity] = { error: 'entities not available' }; return }

    const r: Record<string, any> = {}

    try {
      // Create bare entity
      const id1 = entities.create()
      r.createdId = id1

      // Create with position
      const id2 = entities.create(float3(5, 10, 15))
      r.createdWithPosId = id2

      // Create and immediately destroy
      const id3 = entities.create()
      r.destroyTargetId = id3
      r.destroyResult = entities.destroy(id3)
    } catch (e: any) {
      r.error = e?.message ?? String(e)
    }

    _g._e2e_ent[this.entity] = r
  }
}
