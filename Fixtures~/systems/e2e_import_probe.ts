// E2E: verifies builtin module globals are available AND callable.

var _e2e_import_ran = false

export function onUpdate(): void {
  if (_e2e_import_ran) return

  const _g = globalThis as any
  const ecs = _g.ecs
  const math = _g.math
  const float3 = _g.float3
  if (!ecs?.query || !math?.sin || !float3) return

  _e2e_import_ran = true

  // Actually CALL the functions, not just typeof check
  const v = float3(1, 2, 3)
  const queryBuilder = ecs.query()

  _g._e2e_import = {
    hasQuery: typeof ecs.query === 'function',
    hasSin: typeof math.sin === 'function',
    hasFloat3: typeof float3 === 'function',
    mathResult: math.sin(0),
    // Prove float3 constructor works: (1,2,3).x should be 1
    float3X: v.x,
    float3Y: v.y,
    float3Z: v.z,
    // Prove query builder returns an object with withAll
    queryHasWithAll: typeof queryBuilder.withAll === 'function',
  }
}
