namespace UnityJS.Entities.Systems.Support
{
  using Components;
  using Core;
  using Unity.Collections;
  using Unity.Entities;
  using Unity.Logging;
  using UnityJS.Runtime;

  [UpdateInGroup(typeof(InitializationSystemGroup))]
  [UpdateBefore(typeof(JsScriptCleanupSystem))]
  public partial class JsDataCleanupSystem : SystemBase
  {
    JsRuntimeManager m_Vm;
    EntityQuery m_CleanupQuery;

    protected override void OnCreate()
    {
      m_CleanupQuery = GetEntityQuery(
        ComponentType.ReadOnly<JsDataCleanup>(),
        ComponentType.Exclude<JsEntityId>()
      );
    }

    protected override void OnStartRunning()
    {
      m_Vm = JsRuntimeManager.Instance ?? JsRuntimeManager.GetOrCreate();
    }

    protected override void OnUpdate()
    {
      if (m_CleanupQuery.IsEmptyIgnoreFilter)
        return;

      if (m_Vm == null || !m_Vm.IsValid)
      {
        if (JsRuntimeManager.Instance != null && JsRuntimeManager.Instance.IsValid)
          m_Vm = JsRuntimeManager.Instance;
        else
          return;
      }

      var entities = m_CleanupQuery.ToEntityArray(Allocator.Temp);
      var cleanups = m_CleanupQuery.ToComponentDataArray<JsDataCleanup>(Allocator.Temp);
      var ctx = m_Vm.Context;

      for (var i = 0; i < entities.Length; i++)
      {
        var entityId = cleanups[i].entityId;
        var componentNames = JsComponentStore.GetEntityComponents(entityId);

        if (componentNames != null && componentNames.Count > 0)
        {
          JsComponentStore.ScrubJsData(ctx, entityId, componentNames);
          Log.Verbose(
            "[JsDataCleanup] Scrubbed {0} JS components for entity {1}",
            componentNames.Count,
            entityId
          );
        }

        JsComponentStore.CleanupEntity(entityId);
        EntityManager.RemoveComponent<JsDataCleanup>(entities[i]);
      }

      entities.Dispose();
      cleanups.Dispose();
    }
  }
}
