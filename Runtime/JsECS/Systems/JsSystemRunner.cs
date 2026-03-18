using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("UnityJS.Entities.PlayModeTests")]
[assembly: InternalsVisibleTo("Project.Tests.PlayMode")]

namespace UnityJS.Entities.Systems
{
  using System.Collections.Generic;
  using System.IO;
  using Core;
  using Runtime;
  using Unity.Collections;
  using Unity.Entities;
  using Unity.Logging;
  using Unity.Transforms;
  using UnityEngine;

  public struct JsSystemManifest : IComponentData
  {
    public bool initialized;
  }

  [UpdateInGroup(typeof(SimulationSystemGroup))]
  [UpdateAfter(typeof(JsScriptingSystem))]
  public partial class JsSystemRunner : SystemBase
  {
    internal const string QueryBuilderSourceForTests = QueryBuilderSource;
    internal const string ComponentGlueSourceForTests = ComponentGlueSource;

    const string QueryBuilderSource =
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

    const string ComponentGlueSource =
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
Component.prototype.start = null;
Component.prototype.update = null;
Component.prototype.fixedUpdate = null;
Component.prototype.lateUpdate = null;
Component.prototype.onDestroy = null;
globalThis.ecs.Component = Component;

// Auto-tick registry
var __comp_ticks = { update: [], fixedUpdate: [], lateUpdate: [] };

function __registerComponentTick(eid, instance) {
  if (instance.update) __comp_ticks.update.push({eid: eid, inst: instance});
  if (instance.fixedUpdate) __comp_ticks.fixedUpdate.push({eid: eid, inst: instance});
  if (instance.lateUpdate) __comp_ticks.lateUpdate.push({eid: eid, inst: instance});
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
  for (var i = 0; i < arr.length; i++) {
    var entry = arr[i];
    if (entry.inst.__destroyed) { arr.splice(i--, 1); continue; }
    try {
      if (entry.inst.__needs_start) {
        entry.inst.__needs_start = false;
        if (entry.inst.start) entry.inst.start();
      }
      entry.inst[group](dt);
    } catch (e) {
      log.error('Component ' + (entry.inst.constructor ? entry.inst.constructor.name : '?') + '.' + group + ' error: ' + e);
      arr.splice(i--, 1);
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
  __registerComponentTick(entityId, inst);
  inst.__needs_start = true;
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
";

    JsRuntimeManager m_Vm;
    readonly List<string> m_SystemNames = new();
    readonly Dictionary<string, int> m_SystemStateRefs = new();
    bool m_BridgesRegistered;

    ComponentLookup<LocalTransform> m_TransformLookup;
    EntityQuery m_SentinelQuery;

    protected override void OnCreate()
    {
      m_TransformLookup = GetComponentLookup<LocalTransform>();
      m_SentinelQuery = GetEntityQuery(ComponentType.ReadWrite<Components.JsEntityId>());
    }

    protected override void OnStartRunning()
    {
      EnsureVmReady();
    }

    /// <summary>
    /// Checks whether the VM is alive and belongs to this runner.
    /// If the VM was disposed (e.g. JsScriptingSystem.OnDestroy during
    /// subscene loading or play-mode re-entry) and a new one exists,
    /// resets all cached state and re-discovers system scripts.
    /// Returns true if the VM is ready for use.
    /// </summary>
    bool EnsureVmReady()
    {
      var vm = JsRuntimeManager.Instance;
      if (vm == null || !vm.IsValid)
        return false;

      if (vm == m_Vm && m_BridgesRegistered)
        return true;

      // VM changed — old script refs and state refs point to a dead context.
      m_SystemNames.Clear();
      m_SystemStateRefs.Clear();
      m_BridgesRegistered = false;
      m_Vm = vm;

      // Ensure bridge state exists on new VM
      m_Vm.BridgeState ??= new JsBridgeState();

      // If fulfillment already loaded bridges + glue, skip re-registration.
      // QueryBridge.Register and ComponentStore.Register overwrite ecs.query / ecs.add
      // which the glue scripts have already wrapped — re-registering undoes the wrapping.
      if (!m_Vm.HasScript("__ecs_component_glue"))
      {
        m_Vm.RegisterBridgeNow(JsECSBridge.RegisterFunctions);
        m_Vm.RegisterBridgeNow(JsQueryBridge.Register);
        m_Vm.RegisterBridgeNow(JsComponentRegistry.RegisterAllBridges);
        m_Vm.RegisterBridgeNow(JsComponentStore.Register);
        m_Vm.LoadScriptFromString("__ecs_query_builder", QueryBuilderSource);
        m_Vm.LoadScriptFromString("__ecs_component_glue", ComponentGlueSource);
      }

      m_Vm.RegisterBridgeNow(JsSystemBridge.Register);
      m_BridgesRegistered = true;

      var scriptingSystem = World.GetOrCreateSystemManaged<JsScriptingSystem>();
      JsECSBridge.Initialize(World);
      JsEntityRegistry.Initialize();
      JsQueryBridge.Initialize(EntityManager);

      if (!SystemAPI.HasSingleton<JsSystemManifest>())
      {
        var entity = EntityManager.CreateEntity();
        EntityManager.AddComponentData(entity, new JsSystemManifest { initialized = false });
      }

      DiscoverAndLoadSystems();
      return true;
    }

    protected override void OnDestroy()
    {
      // SharedStatic Persistent allocations — must be disposed exactly once
      JsECSBridge.Shutdown();
      // QueryBridge + ComponentStore state is cleared by JsBridgeState.Dispose()
    }

    void DiscoverAndLoadSystems()
    {
      JsScriptSearchPaths.Initialize();
      var systems = JsScriptSourceRegistry.DiscoverAllSystems();
      if (systems.Count == 0)
      {
        Log.Warning("[JsSystemRunner] No system scripts found in any registered source");
        ref var manifest = ref SystemAPI.GetSingletonRW<JsSystemManifest>().ValueRW;
        manifest.initialized = true;
        return;
      }

      foreach (var (systemName, source) in systems)
      {
        if (m_SystemStateRefs.ContainsKey(systemName))
          continue;

        if (!source.TryReadScript("systems/" + systemName, out var code, out var resolvedId))
          continue;

        LoadSystem(systemName, code, resolvedId);
      }

      ref var m = ref SystemAPI.GetSingletonRW<JsSystemManifest>().ValueRW;
      m.initialized = true;
    }

    void LoadSystem(string systemName, string source, string resolvedId)
    {
      var scriptId = "system:" + systemName;

      // Always force fresh evaluation — ReloadScript frees any stale namespace
      // and appends ?v=N to bypass QuickJS module cache.
      if (!m_Vm.ReloadScript(scriptId, source, resolvedId))
      {
        Log.Error("[JsSystemRunner] Failed to load system '{0}'", systemName);
        return;
      }

      // System scripts have no entity (entityId = -1)
      var stateRef = m_Vm.CreateEntityState(scriptId, -1);
      if (stateRef < 0)
      {
        Log.Error("[JsSystemRunner] Failed to create state for system '{0}'", systemName);
        return;
      }

      m_SystemStateRefs[systemName] = stateRef;
      m_SystemNames.Add(systemName);
    }

    public void ReloadSystem(string systemName)
    {
      if (m_SystemStateRefs.TryGetValue(systemName, out var oldRef))
      {
        m_Vm.ReleaseEntityState(oldRef);
        m_SystemStateRefs.Remove(systemName);
        m_SystemNames.Remove(systemName);
      }

      if (!JsScriptSourceRegistry.TryReadScript(
            "systems/" + systemName, out var source, out var resolvedId))
        return;

      var scriptId = "system:" + systemName;
      if (!m_Vm.ReloadScript(scriptId, source, resolvedId))
      {
        Log.Error("[JsSystemRunner] Failed to reload system '{0}'", systemName);
        return;
      }

      var stateRef = m_Vm.CreateEntityState(scriptId, -1);
      if (stateRef < 0)
      {
        Log.Error("[JsSystemRunner] Failed to create state for system '{0}'", systemName);
        return;
      }

      m_SystemStateRefs[systemName] = stateRef;
      m_SystemNames.Add(systemName);
    }

    protected override void OnUpdate()
    {
      if (!EnsureVmReady())
        return;

      var deltaTime = SystemAPI.Time.DeltaTime;
      if (deltaTime <= 0f)
        deltaTime = UnityEngine.Time.deltaTime;

      var elapsedTime = SystemAPI.Time.ElapsedTime;

      RegisterSentinelEntities();

      PrewarmComponentQueries();

      EntityManager.CompleteDependencyBeforeRW<LocalTransform>();
      m_TransformLookup.Update(this);

      JsSystemBridge.UpdateContext(deltaTime, elapsedTime);

      ref var sysState = ref CheckedStateRef;
      JsComponentRegistry.UpdateAllLookups(ref sysState);

      var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
      var ecb = ecbSingleton.CreateCommandBuffer(World.Unmanaged);
      var scriptBufferLookup = GetBufferLookup<Components.JsScript>(true);
      JsECSBridge.UpdateBurstContext(ecb, deltaTime, m_TransformLookup, scriptBufferLookup);

      foreach (var systemName in m_SystemNames)
      {
        if (!m_SystemStateRefs.TryGetValue(systemName, out var stateRef))
          continue;

        m_Vm.UpdateStateTimings(stateRef, deltaTime, elapsedTime);

        var scriptId = "system:" + systemName;
        m_Vm.CallFunction(scriptId, "onUpdate", stateRef);
        m_Vm.FlushRefRw();
      }

      // Tick Component-class instances (update lifecycle) — must happen here because
      // generated bridge lookups are refreshed by UpdateAllLookups above.
      m_Vm.TickComponents("update", deltaTime);
    }

    void PrewarmComponentQueries()
    {
      JsQueryBridge.FlushPendingQueries(EntityManager);
      JsQueryBridge.PrecomputeQueryResults(EntityManager);
    }

    void RegisterSentinelEntities()
    {
      var entities = m_SentinelQuery.ToEntityArray(Allocator.Temp);
      var ids = m_SentinelQuery.ToComponentDataArray<Components.JsEntityId>(Allocator.Temp);

      for (var i = 0; i < entities.Length; i++)
      {
        if (ids[i].value != 0)
          continue;

        var newId = JsEntityRegistry.AllocateId();
        if (newId <= 0)
          continue;

        EntityManager.SetComponentData(entities[i], new Components.JsEntityId { value = newId });
        JsEntityRegistry.RegisterImmediate(entities[i], newId, EntityManager);
      }

      entities.Dispose();
      ids.Dispose();
    }
  }
}
