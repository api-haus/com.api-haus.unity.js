import { Component } from 'unity.js/ecs'

// E2E: verifies input.readValue() is null-safe when no input device is connected.

const _g = globalThis as Record<string, any>
if (!_g._e2e_input) _g._e2e_input = {}

export default class E2EInputProbe extends Component {
  start(): void {
    const input = _g.input
    const r: Record<string, any> = { inputAvailable: !!input }

    if (input && typeof input.readValue === 'function') {
      try {
        const val = input.readValue('Move')
        r.moveValue = val
        r.moveIsNull = val == null
        r.noThrow = true
      } catch (e: any) {
        r.error = e?.message ?? String(e)
        r.noThrow = false
      }
    } else {
      r.moveIsNull = true
      r.noThrow = true
    }

    _g._e2e_input[this.entity] = r
  }
}
