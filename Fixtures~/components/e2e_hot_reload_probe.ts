import { Component } from 'unity.js/ecs'

// E2E: hot reload probe. Returns a version number.
// Test mutates this file to change VERSION, recompiles, verifies new value loads.

const _g = globalThis as Record<string, any>
if (!_g._e2e_hot) _g._e2e_hot = {}

const VERSION = 1

export default class E2EHotReloadProbe extends Component {
  start(): void {
    _g._e2e_hot[this.entity] = { version: VERSION }
  }
}
