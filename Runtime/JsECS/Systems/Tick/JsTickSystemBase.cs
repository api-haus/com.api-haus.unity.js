namespace UnityJS.Entities.Systems.Tick
{
  using System.Collections.Generic;
  using Components;
  using Core;
  using Runtime;
  using Unity.Entities;
  using Unity.Logging;
  using Unity.Transforms;

  public abstract partial class JsTickSystemBase : SystemBase
  {
    protected JsRuntimeManager m_Vm;
    ComponentLookup<LocalTransform> m_TransformLookup;
    BufferLookup<JsScript> m_ScriptBufferLookup;

    readonly List<(
      Entity entity,
      string scriptName,
      int entityIndex,
      int stateRef
    )> m_PendingTicks = new(64);

    protected abstract JsTickGroup GetTickGroup();

    protected override void OnCreate()
    {
      base.OnCreate();
      m_TransformLookup = GetComponentLookup<LocalTransform>(false);
      m_ScriptBufferLookup = GetBufferLookup<JsScript>(true);
    }

    protected override void OnStartRunning()
    {
      m_Vm = JsRuntimeManager.Instance ?? JsRuntimeManager.GetOrCreate();
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

      EntityManager.CompleteDependencyBeforeRW<LocalTransform>();
      m_TransformLookup.Update(this);
      m_ScriptBufferLookup.Update(this);

      ref var sysState = ref CheckedStateRef;
      JsComponentRegistry.UpdateAllLookups(ref sysState);
      CompleteDependency();

      var tickGroup = GetTickGroup();

      var deltaTime = SystemAPI.Time.DeltaTime;
      if (deltaTime <= 0f)
        deltaTime = UnityEngine.Time.deltaTime;

      var elapsedTime = SystemAPI.Time.ElapsedTime;

      var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
      var ecb = ecbSingleton.CreateCommandBuffer(World.Unmanaged);
      JsECSBridge.UpdateBurstContext(ecb, deltaTime, m_TransformLookup, m_ScriptBufferLookup);

      m_PendingTicks.Clear();

      foreach (
        var (scripts, entity) in SystemAPI
          .Query<DynamicBuffer<JsScript>>()
          .WithAll<JsEntityId>()
          .WithEntityAccess()
      )
        for (var i = 0; i < scripts.Length; i++)
        {
          var script = scripts[i];
          if (script.stateRef >= 0 && !script.disabled && script.tickGroup == tickGroup)
            m_PendingTicks.Add(
              (entity, script.scriptName.ToString(), script.entityIndex, script.stateRef)
            );
        }

      foreach (var (entity, scriptName, entityIndex, stateRef) in m_PendingTicks)
      {
        if (!EntityManager.Exists(entity))
          continue;

        if (!EntityManager.HasComponent<JsEntityId>(entity))
          continue;

        if (!m_Vm.ValidateStateRef(stateRef))
        {
          Log.Warning(
            "[JsTick:{0}] StateRef mismatch for {1} entity={2} - skipping",
            tickGroup,
            scriptName,
            entityIndex
          );
          continue;
        }

        m_Vm.CallTick(scriptName, stateRef, deltaTime, elapsedTime);
      }

      // Tick Component instances for matching tick groups
      if (tickGroup == JsTickGroup.Fixed)
        m_Vm.TickComponents("fixedUpdate", deltaTime);
      else if (tickGroup == JsTickGroup.AfterTransform)
        m_Vm.TickComponents("lateUpdate", deltaTime);
    }
  }
}
