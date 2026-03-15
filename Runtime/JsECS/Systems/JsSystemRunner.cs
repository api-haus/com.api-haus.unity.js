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
  return new QueryBuilder([...this._all, ...accessors], this._none);
};

QueryBuilder.prototype.withNone = function(...accessors) {
  return new QueryBuilder(this._all, [...this._none, ...accessors]);
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

      m_Vm.RegisterBridgeNow(JsECSBridge.RegisterFunctions);
      m_Vm.RegisterBridgeNow(JsSystemBridge.Register);
      m_Vm.RegisterBridgeNow(JsQueryBridge.Register);
      m_Vm.RegisterBridgeNow(JsComponentRegistry.RegisterAllBridges);
      m_Vm.RegisterBridgeNow(JsComponentStore.Register);
      m_BridgesRegistered = true;
      m_Vm.LoadScriptFromString("__ecs_query_builder", QueryBuilderSource);

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
      JsECSBridge.Shutdown();
      JsQueryBridge.Shutdown();
      JsComponentStore.Shutdown();
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

      // If the VM already has this module (e.g. world was recreated but VM persists),
      // skip re-evaluation — QuickJS caches modules and re-eval returns stale namespace.
      if (!m_Vm.HasScript(scriptId))
        if (!m_Vm.LoadScriptAsModule(scriptId, source, resolvedId))
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

      if (
        JsScriptSourceRegistry.TryReadScript(
          "systems/" + systemName,
          out var source,
          out var resolvedId
        )
      )
        LoadSystem(systemName, source, resolvedId);
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
      }
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
