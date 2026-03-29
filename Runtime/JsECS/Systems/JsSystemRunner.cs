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
    ComponentLookup<LocalTransform> m_TransformLookup;
    BufferLookup<Components.JsScript> m_ScriptBufferLookup;
    EntityQuery m_SentinelQuery;

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

      // LocalTransform deps already completed by JsScriptingSystem (runs before us).
      // JS does not schedule ECS jobs, so no new deps can appear between the two systems.
      m_TransformLookup.Update(ref state);

      JsSystemBridge.UpdateContext(deltaTime, elapsedTime);

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

      // Refresh bridge ComponentLookups before TickComponents invokes bridge callbacks.
      // CompleteDependency ensures any jobs touching bridged components are done.
      JsComponentRegistry.UpdateAllLookups(ref state);
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

      if (!vm.HasScript("__ecs_component_glue"))
        Support.JsComponentInitSystem.InitializeVm(vm, state.World);

      vm.RegisterBridgeNow(JsSystemBridge.Register);
      data.BridgesRegistered = true;

      JsEntityRegistry.Initialize();
      JsQueryBridge.Initialize(state.EntityManager);

      if (!SystemAPI.HasSingleton<JsSystemManifest>())
      {
        var entity = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponentData(entity, new JsSystemManifest { initialized = false });
      }

      ref var manifest = ref SystemAPI.GetSingletonRW<JsSystemManifest>().ValueRW;
      JsSystemDiscovery.DiscoverAndLoad(ref data, ref manifest, vm);
      return true;
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
