import { Component } from 'unity.js/ecs'

// E2E: calls log.info/warning/error in start() with known markers.
// C# test uses LogAssert.Expect to verify they appear in Unity log.

const _g = globalThis as Record<string, any>
const log = _g.log

export default class E2ELogProbe extends Component {
  start(): void {
    if (!log) return
    log.info('E2E_LOG_INFO_MARKER')
    log.warning('E2E_LOG_WARN_MARKER')
    log.error('E2E_LOG_ERROR_MARKER')
  }
}
