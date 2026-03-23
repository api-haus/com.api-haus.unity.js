// E2E: system that counts onUpdate calls and records deltaTime.

export function onUpdate(state: { deltaTime: number; elapsedTime: number }): void {
  const _g = globalThis as Record<string, any>
  if (!_g._e2e_sys) _g._e2e_sys = { updateCount: 0, lastDt: 0, lastElapsed: 0, earlyElapsed: 0 }
  const s = _g._e2e_sys
  s.updateCount++
  s.lastDt = state.deltaTime
  s.lastElapsed = state.elapsedTime
  if (s.updateCount === 1) s.earlyElapsed = state.elapsedTime
}
