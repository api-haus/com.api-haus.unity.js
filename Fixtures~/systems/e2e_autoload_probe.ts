// Probe script for JsSystemRunnerE2ETests.
// JsSystemRunner auto-discovers this from StreamingAssets/js/systems/.
// Each onUpdate increments a global counter so the test can observe execution.

export function onUpdate(state: UpdateState): void {
  (globalThis as any)._e2eAutoloadCount = ((globalThis as any)._e2eAutoloadCount || 0) + 1;
  (globalThis as any)._e2eAutoloadLastDt = state.deltaTime;
}
