namespace UnityJS.Entities.Systems.Support
{
  using System.Collections.Generic;
  using Components;
  using Core;
  using Runtime;
  using Unity.Collections;
  using Unity.Entities;

  public class JsEventDispatcher
  {
    readonly List<(
      Entity entity,
      int scriptIndex,
      string scriptName,
      int entityIndex,
      int stateRef,
      List<JsEvent> events
    )> m_PendingEvents;

    readonly List<Entity> m_EntitiesToClear;

    JsRuntimeManager m_Vm;
    EntityManager m_EntityManager;
    EntityQuery m_EventQuery;

    public JsEventDispatcher(
      JsRuntimeManager vm,
      EntityManager entityManager,
      EntityQuery eventQuery
    )
    {
      m_Vm = vm;
      m_EntityManager = entityManager;
      m_EventQuery = eventQuery;
      m_PendingEvents = new List<(Entity, int, string, int, int, List<JsEvent>)>(64);
      m_EntitiesToClear = new List<Entity>(64);
    }

    public void SetVm(JsRuntimeManager vm)
    {
      m_Vm = vm;
    }

    public int CollectPendingEvents()
    {
      m_PendingEvents.Clear();
      m_EntitiesToClear.Clear();
      var eventCount = 0;

      var entities = m_EventQuery.ToEntityArray(Allocator.Temp);
      foreach (var entity in entities)
      {
        var events = m_EntityManager.GetBuffer<JsEvent>(entity);
        if (events.Length == 0)
          continue;

        eventCount += events.Length;
        m_EntitiesToClear.Add(entity);

        var eventsCopy = new List<JsEvent>(events.Length);
        for (var i = 0; i < events.Length; i++)
          eventsCopy.Add(events[i]);

        var scripts = m_EntityManager.GetBuffer<JsScript>(entity);
        for (var i = 0; i < scripts.Length; i++)
        {
          var script = scripts[i];
          if (script.stateRef >= 0 && !script.disabled)
            m_PendingEvents.Add(
              (
                entity,
                i,
                script.scriptName.ToString(),
                script.entityIndex,
                script.stateRef,
                eventsCopy
              )
            );
        }
      }

      entities.Dispose();

      return eventCount;
    }

    public void ClearEventBuffers(EntityCommandBuffer ecb)
    {
      foreach (var entity in m_EntitiesToClear)
        ecb.SetBuffer<JsEvent>(entity);
    }

    public void DispatchEvents()
    {
      foreach (
        var (entity, scriptIndex, scriptName, entityIndex, stateRef, events) in m_PendingEvents
      )
      {
        if (!m_EntityManager.Exists(entity))
          continue;

        if (!m_EntityManager.HasComponent<JsEntityId>(entity))
          continue;

        foreach (var evt in events)
        {
          var eventName = evt.eventName.ToString();
          var sourceId = JsEntityRegistry.GetEntityIdFromEntity(evt.source, m_EntityManager);
          var targetId = JsEntityRegistry.GetEntityIdFromEntity(evt.target, m_EntityManager);

          m_Vm.CallEvent(scriptName, stateRef, eventName, sourceId, targetId, evt.intParam);
        }
      }
    }
  }
}
