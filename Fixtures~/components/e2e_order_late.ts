import { Component } from 'unity.js/ecs'
import E2EOrderEarly from './e2e_order_early'

const _g = globalThis as Record<string, any>
if (!_g._e2e_order) _g._e2e_order = {}

export default class E2EOrderLate extends Component {
  static runsAfter = [E2EOrderEarly]

  update(): void {
    if (!_g._e2e_order[this.entity]) _g._e2e_order[this.entity] = { seq: [] }
    _g._e2e_order[this.entity].seq.push('late')
  }
}
