namespace UnityJS.Entities.Systems
{
  /// <summary>
  /// Inline JavaScript sources for the ECS query builder and component lifecycle glue.
  /// These are evaluated once per VM lifetime to set up the JS-side runtime.
  /// </summary>
  internal static class JsEcsGlueSource
  {
    internal const string QueryBuilder =
      @"
const _nativeQuery = globalThis.ecs.query;
globalThis._nativeQuery = _nativeQuery;
globalThis.ecs.query = function() {
  return new QueryBuilder([], []);
};

function QueryBuilder(all, none) {
  this._all = all;
  this._none = none;
}

QueryBuilder.prototype.withAll = function(...accessors) {
  var mapped = accessors.map(function(a) {
    if (typeof a === 'function' && a.__jsComp) return a;
    return a;
  });
  return new QueryBuilder([...this._all, ...mapped], this._none);
};

QueryBuilder.prototype.withNone = function(...accessors) {
  var mapped = accessors.map(function(a) {
    if (typeof a === 'function' && a.__jsComp) return a;
    return a;
  });
  return new QueryBuilder(this._all, [...this._none, ...mapped]);
};

QueryBuilder.prototype.build = function() {
  const accessors = this._all;
  const allNames = accessors.map(a => a.__name);
  const noneNames = this._none.map(a => a.__name);
  return new BuiltQuery(accessors, allNames, noneNames);
};

function BuiltQuery(accessors, allNames, noneNames) {
  this._accessors = accessors;
  this._allNames = allNames;
  this._noneNames = noneNames;
}

BuiltQuery.prototype[Symbol.iterator] = function() {
  const accessors = this._accessors;
  const entities = this._noneNames.length > 0
    ? _nativeQuery({ all: this._allNames, none: this._noneNames })
    : _nativeQuery(...this._allNames);
  const n = entities.length;
  if (n === 0) return { next() { return { done: true, value: undefined }; } };

  // Force per-entity path — batch setAll writeback doesn't persist
  // for components whose lookup was obtained in a different context.
  let useBatch = false;

  if (!useBatch) {
    // Per-entity fallback
    let i = 0, prevEid = -1, prevTuple = null;
    function flush() {
      if (prevTuple === null) return;
      for (let j = 0; j < accessors.length; j++) {
        if (accessors[j].__jsComp) continue;
        if (accessors[j].set) accessors[j].set(prevEid, prevTuple[j + 1]);
      }
      prevTuple = null;
    }
    return {
      next() {
        flush();
        if (i >= n) return { done: true, value: undefined };
        const eid = entities[i++];
        const tuple = [eid];
        for (let j = 0; j < accessors.length; j++) tuple.push(accessors[j].get(eid));
        prevEid = eid;
        prevTuple = tuple;
        return { done: false, value: tuple };
      },
      return(v) { flush(); return { done: true, value: v }; }
    };
  }

  // Batch path: one FFI call per component instead of N
  const columns = [];
  const strides = [];
  const layouts = [];
  for (let j = 0; j < accessors.length; j++) {
    columns[j] = accessors[j].getAll(entities);
    strides[j] = accessors[j].__stride;
    layouts[j] = accessors[j].__fieldLayout;
  }

  let i = 0;
  const refs = [];

  function unpackEntity(idx) {
    const tuple = [entities[idx]];
    for (let j = 0; j < accessors.length; j++) {
      const s = strides[j], layout = layouts[j], col = columns[j];
      const off = idx * s;
      const obj = {};
      for (let f = 0; f < layout.length; f++) {
        const fl = layout[f];
        if (fl.count === 1) {
          obj[fl.name] = col[off + fl.offset];
        } else {
          const sub = { x: col[off + fl.offset], y: col[off + fl.offset + 1] };
          if (fl.count >= 3) sub.z = col[off + fl.offset + 2];
          if (fl.count >= 4) sub.w = col[off + fl.offset + 3];
          obj[fl.name] = sub;
        }
      }
      tuple.push(obj);
    }
    return tuple;
  }

  function writeback() {
    for (let j = 0; j < accessors.length; j++) {
      if (!accessors[j].setAll) continue;
      const s = strides[j], layout = layouts[j], col = columns[j];
      for (let k = 0; k < refs.length; k++) {
        const obj = refs[k][j + 1];
        const off = refs[k]._idx * s;
        for (let f = 0; f < layout.length; f++) {
          const fl = layout[f];
          if (fl.count === 1) {
            col[off + fl.offset] = obj[fl.name];
          } else {
            const sub = obj[fl.name];
            col[off + fl.offset] = sub.x;
            col[off + fl.offset + 1] = sub.y;
            if (fl.count >= 3) col[off + fl.offset + 2] = sub.z;
            if (fl.count >= 4) col[off + fl.offset + 3] = sub.w;
          }
        }
      }
      accessors[j].setAll(entities, col);
    }
  }

  return {
    next() {
      if (i >= n) {
        writeback();
        return { done: true, value: undefined };
      }
      const tuple = unpackEntity(i);
      tuple._idx = i;
      refs.push(tuple);
      i++;
      return { done: false, value: tuple };
    },
    return(v) {
      writeback();
      return { done: true, value: v };
    }
  };
};
";

    internal const string ComponentGlue =
      @"
// Component base class
function Component() {}
Object.defineProperty(Component, '__jsComp', { get: function() { return true; } });
Object.defineProperty(Component, '__stride', { get: function() { return 0; } });
Object.defineProperty(Component, '__fieldLayout', { get: function() { return []; } });
Object.defineProperty(Component, '__name', {
  get: function() { return this.name; },
  configurable: true
});
Component.get = function(eid) {
  var s = __js_comp[this.name]; return s ? s[eid] : undefined;
};
Component.set = function() {};
Component.runsAfter = null;
Component.runsBefore = null;
Component.prototype.start = null;
Component.prototype.update = null;
Component.prototype.fixedUpdate = null;
Component.prototype.lateUpdate = null;
Component.prototype.onDestroy = null;
globalThis.ecs.Component = Component;

// Auto-tick registry
var __comp_ticks = { update: [], fixedUpdate: [], lateUpdate: [] };
var __sortDirty = { update: false, fixedUpdate: false, lateUpdate: false };

function __topoSortTickArray(arr) {
  if (arr.length <= 1) return;
  var names = [];
  var nameSet = {};
  for (var i = 0; i < arr.length; i++) {
    var n = arr[i].inst.constructor.name;
    if (!nameSet[n]) { nameSet[n] = true; names.push(n); }
  }
  if (names.length <= 1) return;
  var adj = {};
  var inDeg = {};
  for (var i = 0; i < names.length; i++) { adj[names[i]] = []; inDeg[names[i]] = 0; }
  for (var i = 0; i < arr.length; i++) {
    var ctor = arr[i].inst.constructor;
    var cn = ctor.name;
    var after = ctor.runsAfter;
    if (after) {
      for (var j = 0; j < after.length; j++) {
        var dep = after[j].name;
        if (nameSet[dep] && adj[dep].indexOf(cn) === -1) { adj[dep].push(cn); inDeg[cn]++; }
      }
    }
    var before = ctor.runsBefore;
    if (before) {
      for (var j = 0; j < before.length; j++) {
        var tgt = before[j].name;
        if (nameSet[tgt] && adj[cn].indexOf(tgt) === -1) { adj[cn].push(tgt); inDeg[tgt]++; }
      }
    }
  }
  var queue = [];
  for (var i = 0; i < names.length; i++) { if (inDeg[names[i]] === 0) queue.push(names[i]); }
  var sorted = [];
  while (queue.length > 0) {
    var cur = queue.shift();
    sorted.push(cur);
    var edges = adj[cur];
    for (var j = 0; j < edges.length; j++) { if (--inDeg[edges[j]] === 0) queue.push(edges[j]); }
  }
  if (sorted.length !== names.length) {
    log.error('Cycle detected in component execution ordering');
    return;
  }
  var rank = {};
  for (var i = 0; i < sorted.length; i++) rank[sorted[i]] = i;
  arr.sort(function(a, b) { return rank[a.inst.constructor.name] - rank[b.inst.constructor.name]; });
}

function __registerComponentTick(eid, instance) {
  var hasTick = false;
  if (instance.update) { __comp_ticks.update.push({eid: eid, inst: instance}); __sortDirty.update = true; hasTick = true; }
  if (instance.fixedUpdate) { __comp_ticks.fixedUpdate.push({eid: eid, inst: instance}); __sortDirty.fixedUpdate = true; hasTick = true; }
  if (instance.lateUpdate) { __comp_ticks.lateUpdate.push({eid: eid, inst: instance}); __sortDirty.lateUpdate = true; hasTick = true; }
  if (!hasTick && instance.__needs_start) { __comp_ticks.update.push({eid: eid, inst: instance}); __sortDirty.update = true; }
}

function __unregisterComponentTick(eid) {
  for (var key in __comp_ticks) {
    var arr = __comp_ticks[key];
    for (var i = arr.length - 1; i >= 0; i--) {
      if (arr[i].eid === eid) arr.splice(i, 1);
    }
  }
}

globalThis.__tickComponents = function(group, dt) {
  var arr = __comp_ticks[group];
  if (!arr) return;
  if (__sortDirty[group]) { __sortDirty[group] = false; __topoSortTickArray(arr); }
  for (var i = 0; i < arr.length; i++) {
    var entry = arr[i];
    if (entry.inst.__destroyed) { arr.splice(i--, 1); continue; }
    var compName = entry.inst.constructor ? entry.inst.constructor.name : null;
    try {
      if (entry.inst.__needs_start) {
        entry.inst.__needs_start = false;
        if (entry.inst.start) entry.inst.start();
      }
      if (entry.inst[group]) entry.inst[group](dt);
      else { arr.splice(i--, 1); continue; }
    } catch (e) {
      arr.splice(i--, 1);
      // 'not a function' is a transient artifact of live baking — entity destroyed
      // and recreated, stale instance ticks once before cleanup. Self-heals via splice.
      if (('' + e).indexOf('not a function') < 0) {
        var name = compName || '?';
        log.error('Component ' + name + '.' + group + ' error: ' + e);
      }
    }
  }
  globalThis.__flushRefRw();
};

globalThis.__unregisterComponentTick = __unregisterComponentTick;

globalThis.__cleanupComponentEntity = function(eid) {
  __unregisterComponentTick(eid);
  for (var name in __js_comp) {
    var store = __js_comp[name];
    if (!store) continue;
    var inst = store[eid];
    if (inst && typeof inst === 'object') {
      inst.__destroyed = true;
      if (typeof inst.onDestroy === 'function') inst.onDestroy();
    }
  }
};

// Unified ecs.add — handles both string names and Component classes
var _nativeAdd = globalThis.ecs.add;
globalThis.ecs.add = function(eid, comp, data) {
  if (typeof comp === 'string') return _nativeAdd(eid, comp, data);
  var name = comp.__name || comp.name;
  if (comp.__jsComp) {
    var inst = data !== undefined ? Object.assign(new comp(), data) : new comp();
    inst.entity = eid;
    _nativeAdd(eid, name, inst);
    __registerComponentTick(eid, inst);
    if (inst.start) inst.start();
    return inst;
  }
  return _nativeAdd(eid, name, data);
};

// Component init for fulfillment pipeline
globalThis.__componentInit = function(scriptName, entityId, propsJson) {
  var mod = globalThis.__lastLoadedModule;
  if (!mod || !mod.default) return false;
  var cls = mod.default;
  if (!cls.__jsComp) return false;
  var inst = new cls();
  if (propsJson) {
    var overrides = JSON.parse(propsJson);
    for (var k in overrides) inst[k] = overrides[k];
  }
  inst.entity = entityId;
  _nativeAdd(entityId, cls.name, inst);
  inst.__needs_start = true;
  __registerComponentTick(entityId, inst);
  return true;
};

// ── RefRW auto-flush ──
var __refRwPending = [];

globalThis.__flushRefRw = function() {
  var arr = __refRwPending;
  for (var i = 0; i < arr.length; i++) {
    var e = arr[i];
    e.a.set(e.id, e.d);
  }
  arr.length = 0;
};

// ecs.get(Accessor, entity) — returns auto-flushed ref
// If the same (accessor, eid) was already get()'d this flush window,
// returns the SAME JS object so all in-place modifications accumulate
// on one object and a single set() call flushes the final state.
globalThis.ecs.get = function(accessor, eid) {
  if (accessor.__jsComp) {
    var s = __js_comp[accessor.__name || accessor.name];
    return s ? s[eid] : undefined;
  }
  // Return existing pending object for duplicate get() calls
  for (var i = 0; i < __refRwPending.length; i++) {
    var p = __refRwPending[i];
    if (p.a === accessor && p.id === eid) return p.d;
  }
  var data = accessor.get(eid);
  if (data != null && accessor.set) {
    __refRwPending.push({ a: accessor, id: eid, d: data });
  } else if (data != null && !accessor.set) {
    log.error('[ecs.get] accessor ' + (accessor.__name || '?') + ' has NO .set — auto-flush disabled!');
  }
  return data;
};

globalThis.__verifyModuleExports = function(mod) {
    try {
        var names = Object.getOwnPropertyNames(mod);
        for (var i = 0; i < names.length; i++) void mod[names[i]];
        return null;
    } catch (e) { return e.message; }
};

globalThis.__componentReload = function(scriptName) {
  var mod = globalThis.__lastLoadedModule;
  if (!mod || !mod.default || !mod.default.__jsComp) return;
  var newCls = mod.default;
  var store = __js_comp[newCls.name];
  if (!store) return;
  for (var eid in store) {
    var inst = store[eid];
    if (inst && typeof inst === 'object')
      Object.setPrototypeOf(inst, newCls.prototype);
  }
};
";
  }
}
