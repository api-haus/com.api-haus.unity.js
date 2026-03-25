import { Component } from 'unity.js/ecs'

const _g = globalThis as Record<string, any>
if (!_g._e2e_order) _g._e2e_order = {}

export default class E2EOrderEarly extends Component {
  update(): void {
    if (!_g._e2e_order[this.entity]) _g._e2e_order[this.entity] = { seq: [] }
    _g._e2e_order[this.entity].seq.push('early')
  }
}
