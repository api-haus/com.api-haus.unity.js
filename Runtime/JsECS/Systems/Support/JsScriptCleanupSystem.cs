namespace UnityJS.Entities.Systems.Support
{
  using Components;
  using Runtime;
  using Unity.Collections;
  using Unity.Entities;
  using Unity.Logging;

  [UpdateInGroup(typeof(InitializationSystemGroup))]
  [UpdateAfter(typeof(JsComponentInitSystem))]
  public partial class JsScriptCleanupSystem : SystemBase
  {
    JsRuntimeManager m_Vm;

    protected override void OnStartRunning()
    {
      m_Vm = JsRuntimeManager.Instance;
      if (m_Vm == null || !m_Vm.IsValid)
        return;
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

      var vm = m_Vm;
      var entityManager = EntityManager;

      var query = GetEntityQuery(
        ComponentType.Exclude<JsEntityId>(),
        ComponentType.ReadWrite<JsScript>()
      );

      if (query.IsEmptyIgnoreFilter)
        return;

      var entities = query.ToEntityArray(Allocator.Temp);

      if (entities.Length > 0)
        Log.Verbose("[JsCleanup] Found {0} entities for cleanup", entities.Length);

      for (var i = 0; i < entities.Length; i++)
      {
        var scripts = entityManager.GetBuffer<JsScript>(entities[i]);

        for (var j = 0; j < scripts.Length; j++)
        {
          var script = scripts[j];

          if (script.stateRef < 0 || script.disabled)
            continue;

          var scriptName = script.scriptName.ToString();

          vm.CallFunction(scriptName, "onDestroy", script.stateRef);

          Log.Verbose(
            "[JsCleanup] Releasing state for {0}:{1} ref={2}",
            scriptName,
            script.entityIndex,
            script.stateRef
          );
          vm.ReleaseEntityState(script.stateRef);
        }

        Log.Verbose("[JsCleanup] Destroying entity {0}", entities[i].Index);
        entityManager.DestroyEntity(entities[i]);

        entityManager.RemoveComponent<JsScript>(entities[i]);
      }

      entities.Dispose();
    }
  }
}
