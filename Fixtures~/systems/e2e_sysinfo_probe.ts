// E2E: exercises system.deltaTime, system.time, system.random, system.randomInt.

export function onUpdate(): void {
  const _g = globalThis as Record<string, any>
  const sys = _g.system
  if (!sys || typeof sys.deltaTime !== 'function') return

  if (!_g._e2e_sysinfo) {
    _g._e2e_sysinfo = {
      dt: 0, time: 0, earlyTime: 0, lateTime: 0, frameCount: 0,
      randomMin: 1, randomMax: 0, intMin: 999, intMax: -999,
    }
    for (let i = 0; i < 100; i++) {
      const r = sys.random()
      if (r < _g._e2e_sysinfo.randomMin) _g._e2e_sysinfo.randomMin = r
      if (r > _g._e2e_sysinfo.randomMax) _g._e2e_sysinfo.randomMax = r
      const ri = sys.randomInt(5, 10)
      if (ri < _g._e2e_sysinfo.intMin) _g._e2e_sysinfo.intMin = ri
      if (ri > _g._e2e_sysinfo.intMax) _g._e2e_sysinfo.intMax = ri
    }
  }

  const s = _g._e2e_sysinfo
  s.frameCount++
  s.dt = sys.deltaTime()
  s.time = sys.time()
  if (s.frameCount === 1) s.earlyTime = s.time
  s.lateTime = s.time
}
