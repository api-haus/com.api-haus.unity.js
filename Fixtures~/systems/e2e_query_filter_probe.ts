// E2E: exercises ecs.query().withNone() filter.
// Only runs when _e2e_qf_active is set. Lazy-initializes queries on first activation.

var _e2e_qf_queries: { all: any; filtered: any } | null = null

export function onUpdate(): void {
  const _g = globalThis as any
  if (!_g._e2e_qf_active) return

  const ecs = _g.ecs
  const LocalTransform = _g.LocalTransform
  if (!ecs || !LocalTransform) return

  // Lazy init queries on first activation
  if (!_e2e_qf_queries) {
    ecs.define('E2EDisabled', {})
    const tag = { __name: 'E2EDisabled', __jsComp: true }
    _e2e_qf_queries = {
      all: ecs.query().withAll(LocalTransform).build(),
      filtered: ecs.query().withAll(LocalTransform).withNone(tag).build(),
    }
  }

  if (!_g._e2e_qf) _g._e2e_qf = { allCount: 0, filteredCount: 0 }

  const toTag = _g._e2e_qf_tag as number[] | undefined
  if (toTag && toTag.length > 0) {
    for (const eid of toTag) ecs.add(eid, 'E2EDisabled')
    _g._e2e_qf_tag = []
  }

  let a = 0; for (const [eid] of _e2e_qf_queries.all) a++
  let f = 0; for (const [eid] of _e2e_qf_queries.filtered) f++
  _g._e2e_qf.allCount = a
  _g._e2e_qf.filteredCount = f
}
