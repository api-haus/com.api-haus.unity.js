import { Component } from 'unity.js/ecs'

const _g = globalThis as Record<string, any>
if (!_g._e2e_multi_b) _g._e2e_multi_b = {}

export default class E2EMultiB extends Component {
  startCount = 0
  updateCount = 0
  destroyCount = 0

  start(): void { this.startCount++; _g._e2e_multi_b[this.entity] = this }
  update(): void { this.updateCount++ }
  onDestroy(): void {
    this.destroyCount++
    _g._e2e_multi_b[this.entity] = {
      startCount: this.startCount, updateCount: this.updateCount, destroyCount: this.destroyCount
    }
  }
}
