// E2E: verifies builtin module globals are available.
// Tests that ecs.query, math.sin, float3 constructor are functions.
// Uses globalThis access (not ES imports) since system scripts may load
// before synthetic modules are ready — the test verifies the globals exist.

var _e2e_import_ran = false

export function onUpdate(): void {
  if (_e2e_import_ran) return

  const _g = globalThis as any
  const ecs = _g.ecs
  const math = _g.math
  const float3 = _g.float3
  if (!ecs?.query || !math?.sin || !float3) return

  _e2e_import_ran = true

  _g._e2e_import = {
    hasQuery: typeof ecs.query === 'function',
    hasSin: typeof math.sin === 'function',
    hasFloat3: typeof float3 === 'function',
    mathResult: math.sin(0),
  }
}
