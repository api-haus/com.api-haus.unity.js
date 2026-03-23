import { Component } from 'unity.js/ecs'

// E2E: counts update/fixedUpdate/lateUpdate invocations independently.
// Also records per-frame ordinals to verify execution order.

const _g = globalThis as Record<string, any>
if (!_g._e2e_tick) _g._e2e_tick = {}

export default class E2ETickGroupProbe extends Component {
  private updateCount = 0
  private fixedUpdateCount = 0
  private lateUpdateCount = 0
  private frameOrdinal = 0

  start(): void {
    _g._e2e_tick[this.entity] = this
  }

  update(): void {
    this.updateCount++
    this.frameOrdinal = 1 // update runs first
  }

  fixedUpdate(): void {
    this.fixedUpdateCount++
  }

  lateUpdate(): void {
    this.lateUpdateCount++
    // If update ran this frame, ordinal should be > 1
    if (this.frameOrdinal === 1) this.frameOrdinal = 2
  }
}
