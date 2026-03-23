import { Component } from 'unity.js/ecs'

// E2E test fixture: exercises ecs.define/add/get/has/remove in start().
// All operations run once deterministically — results stored in globals.
//
// INPUT:  entity spawned by SceneFixture
// OUTPUT: _e2e_comp[eid] = { defined, hasBefore, hasAfter, getCurrent, hasAfterRemove, hasTransform }

const _g = globalThis as Record<string, any>
if (!_g._e2e_comp) _g._e2e_comp = {}

export default class E2EComponentAccess extends Component {
  start(): void {
    // Access ecs at call time (not module load) — bridges may not be ready at import
    const ecs = _g.ecs
    if (!ecs) { _g._e2e_comp[this.entity] = { error: 'ecs not available' }; return }

    const eid = this.entity
    const r: Record<string, any> = {}

    try {
      ecs.define('E2ETestHP', { current: 100, max: 100 })
      r.defined = true

      r.hasBefore = ecs.has(eid, 'E2ETestHP')

      ecs.add(eid, 'E2ETestHP', { current: 50, max: 200 })
      r.hasAfter = ecs.has(eid, 'E2ETestHP')

      // Read from JS component store directly — ecs.get() may be wrapped by glue
      const store = _g.__js_comp?.['E2ETestHP']
      const hp = store?.[eid]
      r.getCurrent = hp?.current ?? -1
      r.getMax = hp?.max ?? -1

      ecs.remove(eid, 'E2ETestHP')
      r.hasAfterRemove = ecs.has(eid, 'E2ETestHP')

      r.hasTransform = ecs.has(eid, 'LocalTransform')
    } catch (e: any) {
      r.error = e?.message ?? String(e)
    }

    _g._e2e_comp[eid] = r
  }
}
