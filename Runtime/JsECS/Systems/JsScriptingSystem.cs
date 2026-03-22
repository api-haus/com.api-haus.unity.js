namespace UnityJS.Entities.Systems
{
  using System.Collections.Generic;
  using Components;
  using Core;
  using Runtime;
  using Support;
  using Unity.Collections;
  using Unity.Entities;
  using Unity.Logging;
  using Unity.Mathematics;
  using Unity.Transforms;

  public struct JsScriptingSystemSingleton : IComponentData { }

  [UpdateInGroup(typeof(SimulationSystemGroup))]
  [UpdateBefore(typeof(EndSimulationEntityCommandBufferSystem))]
  public partial class JsScriptingSystem : SystemBase
  {
    JsRuntimeManager m_Vm;
    JsComponentInitSystem m_FulfillmentSystem;

    EntityCommandBuffer m_CurrentECB;
    bool m_ECBValid;

    EntityCommandBuffer m_PrimedECB;
    bool m_PrimedECBValid;

    ComponentLookup<LocalTransform> m_TransformLookup;
    BufferLookup<JsScript> m_ScriptBufferLookup;

    List<(
      Entity entity,
      int scriptIndex,
      string scriptName,
      int entityIndex,
      int stateRef,
      JsTickGroup tickGroup
    )> m_PendingUpdates;

    EntityQuery m_EventQuery;

    static int s_frameCount;

    protected override void OnCreate()
    {
      m_FulfillmentSystem = World.GetOrCreateSystemManaged<JsComponentInitSystem>();

      m_EventQuery = GetEntityQuery(
        ComponentType.ReadWrite<JsScript>(),
        ComponentType.ReadWrite<JsEvent>(),
        ComponentType.ReadOnly<JsEntityId>()
      );

      m_PendingUpdates = new List<(Entity, int, string, int, int, JsTickGroup)>(256);
      m_TransformLookup = GetComponentLookup<LocalTransform>();
      m_ScriptBufferLookup = GetBufferLookup<JsScript>(true);
    }

    protected override void OnStartRunning()
    {
      if (m_Vm == null)
        m_Vm = JsRuntimeManager.GetOrCreate();

      EntityManager.CreateSingleton<JsScriptingSystemSingleton>();
    }

    protected override void OnDestroy()
    {
      m_Vm?.Dispose();
      m_Vm = null;
    }

    public void PrimeBurstContextForOnInit()
    {
      m_PrimedECB = new EntityCommandBuffer(Allocator.TempJob);
      m_PrimedECBValid = true;

      m_TransformLookup.Update(this);
      m_ScriptBufferLookup.Update(this);

      JsECSBridge.UpdateBurstContext(m_PrimedECB, 0f, m_TransformLookup, m_ScriptBufferLookup);
    }

    public void PlaybackPrimedECB()
    {
      if (m_PrimedECBValid && m_PrimedECB.IsCreated)
      {
        m_PrimedECB.Playback(EntityManager);
        m_PrimedECB.Dispose();
        m_PrimedECBValid = false;
      }
    }

    protected override void OnUpdate()
    {
      if (m_Vm == null || !m_Vm.IsValid)
      {
        if (JsRuntimeManager.Instance != null && JsRuntimeManager.Instance.IsValid)
          m_Vm = JsRuntimeManager.Instance;
        else
          return;
      }

      s_frameCount++;

      var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
      m_CurrentECB = ecbSingleton.CreateCommandBuffer(World.Unmanaged);
      m_ECBValid = true;

      var deltaTime = SystemAPI.Time.DeltaTime;
      if (deltaTime <= 0f)
        deltaTime = UnityEngine.Time.deltaTime;

      EntityManager.CompleteDependencyBeforeRW<LocalTransform>();

      m_TransformLookup.Update(this);
      m_ScriptBufferLookup.Update(this);

      JsECSBridge.UpdateBurstContext(
        m_CurrentECB,
        deltaTime,
        m_TransformLookup,
        m_ScriptBufferLookup
      );
      UpdateScriptedEntities(deltaTime);
      // TickComponents moved to JsSystemRunner (which owns the generated bridge lookups).
      DispatchEvents();

      m_ECBValid = false;
    }

    void UpdateScriptedEntities(float deltaTime)
    {
      var elapsedTime = SystemAPI.Time.ElapsedTime;
      m_PendingUpdates.Clear();

      foreach (
        var (scripts, entity) in SystemAPI
          .Query<DynamicBuffer<JsScript>>()
          .WithAll<JsEntityId>()
          .WithEntityAccess()
      )
        for (var i = 0; i < scripts.Length; i++)
        {
          var script = scripts[i];
          if (script.stateRef >= 0 && !script.disabled && script.tickGroup == JsTickGroup.Variable)
            m_PendingUpdates.Add(
              (
                entity,
                i,
                m_Vm.Intern(script.scriptName),
                script.entityIndex,
                script.stateRef,
                script.tickGroup
              )
            );
        }

      foreach (
        var (entity, scriptIndex, scriptName, entityIndex, stateRef, tickGroup) in m_PendingUpdates
      )
      {
        if (!EntityManager.Exists(entity))
        {
          Log.Warning("[JsScripting] Entity no longer exists for script {0}", scriptName);
          continue;
        }

        if (!EntityManager.HasComponent<JsEntityId>(entity))
          continue;

        if (stateRef < 0)
        {
          Log.Error(
            "[JsScripting] Invalid stateRef={0} for {1} entity={2}",
            stateRef,
            scriptName,
            entityIndex
          );
          continue;
        }

        if (!m_Vm.ValidateStateRef(stateRef))
        {
          Log.Warning(
            "[JsScripting] StateRef mismatch for {0} entity={1} stateRef={2} - skipping update",
            scriptName,
            entityIndex,
            stateRef
          );
          continue;
        }

        m_Vm.CallTick(scriptName, stateRef, deltaTime, elapsedTime);
      }
    }

    void DispatchEvents()
    {
      CollectPendingEvents();
      ClearEventBuffers();
      DispatchCollectedEvents();
    }

    void CollectPendingEvents()
    {
      JsECSBridge.ClearEventContext();
      ref var ctx = ref JsECSBridge.EventContext;
      if (!ctx.isValid)
        return;

      var entities = m_EventQuery.ToEntityArray(Allocator.Temp);
      foreach (var entity in entities)
      {
        var events = EntityManager.GetBuffer<JsEvent>(entity);
        if (events.Length == 0)
          continue;

        JsECSBridge.AddEntityToClear(entity);

        var eventStartIndex = ctx.eventBuffer.Length;
        for (var i = 0; i < events.Length; i++)
          JsECSBridge.AddEvent(events[i]);
        var eventCount = events.Length;

        var scripts = EntityManager.GetBuffer<JsScript>(entity);
        for (var i = 0; i < scripts.Length; i++)
        {
          var script = scripts[i];
          if (script.stateRef >= 0 && !script.disabled)
            JsECSBridge.AddEventDispatch(
              entity,
              i,
              script.scriptName,
              script.entityIndex,
              script.stateRef,
              eventStartIndex,
              eventCount
            );
        }
      }

      entities.Dispose();
    }

    void ClearEventBuffers()
    {
      ref var ctx = ref JsECSBridge.EventContext;
      if (!ctx.isValid)
        return;

      for (var i = 0; i < ctx.entitiesToClear.Length; i++)
        m_CurrentECB.SetBuffer<JsEvent>(ctx.entitiesToClear[i]);
    }

    void DispatchCollectedEvents()
    {
      ref var ctx = ref JsECSBridge.EventContext;
      if (!ctx.isValid)
        return;

      for (var i = 0; i < ctx.pendingEvents.Length; i++)
      {
        var dispatch = ctx.pendingEvents[i];

        if (!EntityManager.Exists(dispatch.entity))
          continue;

        if (!EntityManager.HasComponent<JsEntityId>(dispatch.entity))
          continue;

        var scriptName = m_Vm.Intern(dispatch.scriptName);

        for (var j = 0; j < dispatch.eventCount; j++)
        {
          var evt = JsECSBridge.GetEvent(dispatch.eventStartIndex + j);
          var eventName = m_Vm.Intern(evt.eventName);
          var sourceId = JsEntityRegistry.GetEntityIdFromEntity(evt.source, EntityManager);
          var targetId = JsEntityRegistry.GetEntityIdFromEntity(evt.target, EntityManager);

          m_Vm.CallEvent(
            scriptName,
            dispatch.stateRef,
            eventName,
            sourceId,
            targetId,
            evt.intParam
          );
        }
      }
    }

    #region Public API

    public int CreateEntityDeferred(float3 position)
    {
      if (!m_ECBValid)
      {
        Log.Error("[JsScripting] CreateEntityDeferred called outside of update");
        return -1;
      }

      return JsEntityRegistry.Create(position, m_CurrentECB);
    }

    public bool AddScriptDeferred(int entityId, string scriptName)
    {
      if (!m_ECBValid)
      {
        Log.Error("[JsScripting] AddScriptDeferred called outside of update");
        return false;
      }

      return JsEntityRegistry.AddScriptDeferred(entityId, scriptName, m_CurrentECB, EntityManager);
    }

    public void SetPositionDeferred(int entityId, float3 position)
    {
      if (!m_ECBValid)
      {
        Log.Error("[JsScripting] SetPositionDeferred called outside of update");
        return;
      }

      JsEntityRegistry.SetPositionDeferred(entityId, position, m_CurrentECB);
    }

    public void DestroyEntityDeferred(int entityId)
    {
      if (!m_ECBValid)
      {
        Log.Error("[JsScripting] DestroyEntityDeferred called outside of update");
        return;
      }

      JsEntityRegistry.DestroyEntityDeferred(entityId, m_CurrentECB);
    }

    public int GetEntityIdFromEntity(Entity entity)
    {
      return JsEntityRegistry.GetEntityIdFromEntity(entity, EntityManager);
    }

    public Entity GetEntityFromId(int entityId)
    {
      return JsEntityRegistry.GetEntityFromId(entityId);
    }

    public bool IsDeferred(int entityId)
    {
      return JsEntityRegistry.IsPending(entityId);
    }

    public void SendCommand(Entity entity, string command)
    {
      if (!EntityManager.HasBuffer<JsScript>(entity))
        return;

      var scripts = EntityManager.GetBuffer<JsScript>(entity);
      for (var i = 0; i < scripts.Length; i++)
      {
        var script = scripts[i];
        if (script.stateRef < 0 || script.disabled)
          continue;

        m_Vm.CallCommand(m_Vm.Intern(script.scriptName), script.stateRef, command);
      }
    }

    #endregion
  }
}
