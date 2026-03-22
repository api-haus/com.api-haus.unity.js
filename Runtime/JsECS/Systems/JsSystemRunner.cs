using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("UnityJS.Entities.PlayModeTests")]
[assembly: InternalsVisibleTo("Project.Tests.PlayMode")]

namespace UnityJS.Entities.Systems
{
  using Core;
  using Runtime;
  using Unity.Collections;
  using Unity.Entities;
  using Unity.Logging;
  using Unity.Transforms;

  public struct JsSystemManifest : IComponentData
  {
    public bool initialized;
  }

  /// <summary>
  /// Mutable state for JsSystemRunner, stored as IComponentData on the system entity.
  /// All collections are Native — zero managed GC allocations.
  /// </summary>
  public struct JsSystemRunnerData : IComponentData
  {
    public NativeList<FixedString64Bytes> SystemNames;
    public NativeHashMap<FixedString64Bytes, int> SystemStateRefs;
    public NativeHashMap<FixedString64Bytes, FixedString64Bytes> SystemScriptIds;
    public bool BridgesRegistered;
    public int LastVmVersion;
  }

  [UpdateInGroup(typeof(SimulationSystemGroup))]
  [UpdateAfter(typeof(JsScriptingSystem))]
  public partial struct JsSystemRunner : ISystem
  {
    internal const string QueryBuilderSourceForTests = QueryBuilderSource;
    internal const string ComponentGlueSourceForTests = ComponentGlueSource;

    ComponentLookup<LocalTransform> m_TransformLookup;
    BufferLookup<Components.JsScript> m_ScriptBufferLookup;
    EntityQuery m_SentinelQuery;

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
  var hasTick = false;
  if (instance.update) { __comp_ticks.update.push({eid: eid, inst: instance}); hasTick = true; }
  if (instance.fixedUpdate) { __comp_ticks.fixedUpdate.push({eid: eid, inst: instance}); hasTick = true; }
  if (instance.lateUpdate) { __comp_ticks.lateUpdate.push({eid: eid, inst: instance}); hasTick = true; }
  if (!hasTick && instance.__needs_start) __comp_ticks.update.push({eid: eid, inst: instance});
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
      if (entry.inst[group]) entry.inst[group](dt);
      else { arr.splice(i--, 1); continue; }
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

    public void OnCreate(ref SystemState state)
    {
      m_TransformLookup = state.GetComponentLookup<LocalTransform>();
      m_ScriptBufferLookup = state.GetBufferLookup<Components.JsScript>(true);
      m_SentinelQuery = new EntityQueryBuilder(Allocator.Temp)
        .WithAllRW<Components.JsEntityId>()
        .Build(ref state);

      state.EntityManager.AddComponentData(state.SystemHandle, new JsSystemRunnerData
      {
        SystemNames = new NativeList<FixedString64Bytes>(16, Allocator.Persistent),
        SystemStateRefs = new NativeHashMap<FixedString64Bytes, int>(16, Allocator.Persistent),
        SystemScriptIds = new NativeHashMap<FixedString64Bytes, FixedString64Bytes>(16, Allocator.Persistent),
        BridgesRegistered = false,
        LastVmVersion = -1,
      });

    }

    public void OnDestroy(ref SystemState state)
    {
      ref var data = ref state.EntityManager
        .GetComponentDataRW<JsSystemRunnerData>(state.SystemHandle).ValueRW;
      if (data.SystemNames.IsCreated) data.SystemNames.Dispose();
      if (data.SystemStateRefs.IsCreated) data.SystemStateRefs.Dispose();
      if (data.SystemScriptIds.IsCreated) data.SystemScriptIds.Dispose();

      JsECSBridge.Shutdown();
    }

    public void OnUpdate(ref SystemState state)
    {
      ref var data = ref state.EntityManager
        .GetComponentDataRW<JsSystemRunnerData>(state.SystemHandle).ValueRW;

      if (!EnsureVmReady(ref state, ref data))
        return;

      var vm = JsRuntimeManager.Instance;

      var worldTime = state.WorldUnmanaged.Time;
      var deltaTime = worldTime.DeltaTime;
      if (deltaTime <= 0f)
        deltaTime = UnityEngine.Time.deltaTime;

      var elapsedTime = worldTime.ElapsedTime;

      RegisterSentinelEntities(ref state);
      PrewarmComponentQueries(ref state);

      state.EntityManager.CompleteDependencyBeforeRW<LocalTransform>();
      m_TransformLookup.Update(ref state);

      JsSystemBridge.UpdateContext(deltaTime, elapsedTime);
      JsComponentRegistry.UpdateAllLookups(ref state);

      if (!SystemAPI.TryGetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>(out var ecbSingleton))
        return;
      var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
      m_ScriptBufferLookup.Update(ref state);
      JsECSBridge.UpdateBurstContext(ecb, deltaTime, m_TransformLookup, m_ScriptBufferLookup);

      for (var i = 0; i < data.SystemNames.Length; i++)
      {
        var systemName = data.SystemNames[i];
        if (!data.SystemStateRefs.TryGetValue(systemName, out var stateRef))
          continue;

        vm.UpdateStateTimings(stateRef, deltaTime, elapsedTime);

        if (data.SystemScriptIds.TryGetValue(systemName, out var scriptId))
        {
          vm.CallFunction(vm.Intern(scriptId), JsRuntimeManager.OnUpdateBytes, stateRef);
          vm.FlushRefRw();
        }
      }

      vm.TickComponents(JsRuntimeManager.GroupUpdateBytes, deltaTime);
    }

    bool EnsureVmReady(ref SystemState state, ref JsSystemRunnerData data)
    {
      var vm = JsRuntimeManager.Instance;
      if (vm == null || !vm.IsValid)
      {
        Log.Debug("[JsSystemRunner] EnsureVmReady: VM not ready (null={0})", vm == null);
        return false;
      }

      var currentVersion = JsRuntimeManager.InstanceVersion;
      if (data.LastVmVersion == currentVersion && data.BridgesRegistered)
        return true;

      data.SystemNames.Clear();
      data.SystemStateRefs.Clear();
      data.SystemScriptIds.Clear();
      data.BridgesRegistered = false;
      data.LastVmVersion = currentVersion;

      vm.BridgeState ??= new JsBridgeState();

      if (!vm.HasScript("__ecs_component_glue"))
      {
        vm.RegisterBridgeNow(JsECSBridge.RegisterFunctions);
        vm.RegisterBridgeNow(JsQueryBridge.Register);
        vm.RegisterBridgeNow(JsComponentRegistry.RegisterAllBridges);
        vm.RegisterBridgeNow(JsComponentStore.Register);
        vm.LoadScriptFromString("__ecs_query_builder", QueryBuilderSource);
        vm.LoadScriptFromString("__ecs_component_glue", ComponentGlueSource);
      }

      vm.RegisterBridgeNow(JsSystemBridge.Register);
      data.BridgesRegistered = true;

      JsECSBridge.Initialize(state.World);
      JsEntityRegistry.Initialize();
      JsQueryBridge.Initialize(state.EntityManager);

      if (!SystemAPI.HasSingleton<JsSystemManifest>())
      {
        var entity = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponentData(entity, new JsSystemManifest { initialized = false });
      }

      DiscoverAndLoadSystems(ref state, ref data, vm);
      return true;
    }

    void DiscoverAndLoadSystems(
      ref SystemState state,
      ref JsSystemRunnerData data,
      JsRuntimeManager vm
    )
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
        var fsName = new FixedString64Bytes(systemName);
        if (data.SystemStateRefs.ContainsKey(fsName))
          continue;

        if (!source.TryReadScript("systems/" + systemName, out var code, out var resolvedId))
          continue;

        LoadSystem(ref data, vm, systemName, fsName, code, resolvedId);
      }

      ref var m = ref SystemAPI.GetSingletonRW<JsSystemManifest>().ValueRW;
      m.initialized = true;
    }

    static void LoadSystem(
      ref JsSystemRunnerData data,
      JsRuntimeManager vm,
      string systemName,
      FixedString64Bytes fsName,
      string source,
      string resolvedId
    )
    {
      var scriptId = "system:" + systemName;

      if (!vm.ReloadScript(scriptId, source, resolvedId))
      {
        Log.Error("[JsSystemRunner] Failed to load system '{0}'", systemName);
        return;
      }

      var stateRef = vm.CreateEntityState(scriptId, -1);
      if (stateRef < 0)
      {
        Log.Error("[JsSystemRunner] Failed to create state for system '{0}'", systemName);
        return;
      }

      var fsScriptId = new FixedString64Bytes(scriptId);
      data.SystemStateRefs[fsName] = stateRef;
      data.SystemScriptIds[fsName] = fsScriptId;
      data.SystemNames.Add(fsName);
    }

    public static void ReloadSystem(ref SystemState state, string systemName)
    {
      var vm = JsRuntimeManager.Instance;
      if (vm == null || !vm.IsValid)
        return;

      ref var data = ref state.EntityManager
        .GetComponentDataRW<JsSystemRunnerData>(state.SystemHandle).ValueRW;

      var fsName = new FixedString64Bytes(systemName);
      if (data.SystemStateRefs.TryGetValue(fsName, out var oldRef))
      {
        vm.ReleaseEntityState(oldRef);
        data.SystemStateRefs.Remove(fsName);
        data.SystemScriptIds.Remove(fsName);
        for (var i = 0; i < data.SystemNames.Length; i++)
        {
          if (data.SystemNames[i] == fsName)
          {
            data.SystemNames.RemoveAtSwapBack(i);
            break;
          }
        }
      }

      if (!JsScriptSourceRegistry.TryReadScript(
            "systems/" + systemName, out var source, out var resolvedId))
        return;

      LoadSystem(ref data, vm, systemName, fsName, source, resolvedId);
    }

    void PrewarmComponentQueries(ref SystemState state)
    {
      JsQueryBridge.FlushPendingQueries(state.EntityManager);
      JsQueryBridge.PrecomputeQueryResults(state.EntityManager);
    }

    void RegisterSentinelEntities(ref SystemState state)
    {
      if (m_SentinelQuery.IsEmptyIgnoreFilter)
        return;

      var entities = m_SentinelQuery.ToEntityArray(Allocator.Temp);
      var ids = m_SentinelQuery.ToComponentDataArray<Components.JsEntityId>(Allocator.Temp);

      for (var i = 0; i < entities.Length; i++)
      {
        if (ids[i].value != 0)
          continue;

        var newId = JsEntityRegistry.AllocateId();
        if (newId <= 0)
          continue;

        state.EntityManager.SetComponentData(entities[i], new Components.JsEntityId { value = newId });
        JsEntityRegistry.RegisterImmediate(entities[i], newId, state.EntityManager);
      }

      entities.Dispose();
      ids.Dispose();
    }
  }
}
