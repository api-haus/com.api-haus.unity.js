import { Component } from 'unity.js/ecs'

// E2E test fixture: counts lifecycle method invocations via globals.
// INPUT: known initial values (all zero). No randomness, no external deps.
// OUTPUT: _g._e2e_lifecycle[entityId] = { startCount, updateCount, destroyCount, lastDt }
//
// Assertions from first principles:
//   startCount == 1 (called exactly once)
//   updateCount == N (called once per frame after start)
//   destroyCount == 1 (called exactly once on destruction)
//   lastDt > 0 (deltaTime is always positive)

const _g = globalThis as Record<string, any>
if (!_g._e2e_lifecycle) {
  _g._e2e_lifecycle = {}
}

export default class LifecycleProbe extends Component {
  startCount = 0
  updateCount = 0
  destroyCount = 0
  lastDt = 0

  start(): void {
    this.startCount++
    _g._e2e_lifecycle[this.entity] = this
  }

  update(dt: number): void {
    this.updateCount++
    this.lastDt = dt
  }

  onDestroy(): void {
    this.destroyCount++
    // Snapshot final state before instance is released
    _g._e2e_lifecycle[this.entity] = {
      startCount: this.startCount,
      updateCount: this.updateCount,
      destroyCount: this.destroyCount,
      lastDt: this.lastDt,
    }
  }
}
