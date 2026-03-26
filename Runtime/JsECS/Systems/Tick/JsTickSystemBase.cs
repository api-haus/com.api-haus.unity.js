namespace UnityJS.Entities.Systems.Tick
{
  using Components;
  using Core;
  using Runtime;
  using Unity.Collections;
  using Unity.Entities;
  using Unity.Logging;
  using Unity.Transforms;

  /// <summary>
  /// Shared logic for all JS tick systems. Each concrete tick system is an
  /// ISystem struct that delegates to these static methods — no abstract class,
  /// no managed allocation from SystemBase virtual dispatch.
  /// </summary>
  public static class JsTickSystemHelper
  {
    public struct State
    {
      public ComponentLookup<LocalTransform> TransformLookup;
      public BufferLookup<JsScript> ScriptBufferLookup;
      public EntityQuery ScriptQuery;
    }

    public static void OnCreate(ref SystemState state, out State tickState)
    {
      tickState = new State
      {
        TransformLookup = state.GetComponentLookup<LocalTransform>(false),
        ScriptBufferLookup = state.GetBufferLookup<JsScript>(true),
        ScriptQuery = new EntityQueryBuilder(Allocator.Temp)
          .WithAll<JsScript, JsEntityId>()
          .Build(ref state),
      };
    }

    /// <summary>
    /// Called from each concrete tick system's OnUpdate. The caller passes the ECB
    /// obtained via SystemAPI.TryGetSingleton (which only works inside ISystem methods).
    /// </summary>
    public static void OnUpdate(
      ref SystemState state,
      ref State tickState,
      JsTickGroup tickGroup,
      EntityCommandBuffer ecb
    )
    {
      var vm = JsRuntimeManager.Instance;
      if (vm == null || !vm.IsValid)
        return;

      state.CompleteDependency();
      tickState.TransformLookup.Update(ref state);
      tickState.ScriptBufferLookup.Update(ref state);
      JsComponentRegistry.UpdateAllLookups(ref state);

      var worldTime = state.WorldUnmanaged.Time;
      var deltaTime = worldTime.DeltaTime;
      if (deltaTime <= 0f)
        deltaTime = UnityEngine.Time.deltaTime;

      var elapsedTime = worldTime.ElapsedTime;

      JsECSBridge.UpdateBurstContext(ecb, deltaTime, tickState.TransformLookup, tickState.ScriptBufferLookup);

      var entities = tickState.ScriptQuery.ToEntityArray(Allocator.Temp);
      for (var e = 0; e < entities.Length; e++)
      {
        var entity = entities[e];
        var scripts = state.EntityManager.GetBuffer<JsScript>(entity, true);

        for (var i = 0; i < scripts.Length; i++)
        {
          var script = scripts[i];
          if (script.stateRef < 0 || script.disabled || script.tickGroup != tickGroup)
            continue;

          if (!vm.ValidateStateRef(script.stateRef))
          {
            Log.Warning(
              "[JsTick:{0}] StateRef mismatch for entity={1} - skipping",
              tickGroup,
              script.entityIndex
            );
            continue;
          }

          vm.CallTick(vm.Intern(script.scriptName), script.stateRef, deltaTime, elapsedTime);

          // Entity may have been destroyed by a JS callback
          if (!state.EntityManager.Exists(entity))
            break;
        }
      }

      entities.Dispose();

      // Tick Component instances for matching tick groups
      if (tickGroup == JsTickGroup.Fixed)
        vm.TickComponents(JsRuntimeManager.GroupFixedUpdateBytes, deltaTime);
      else if (tickGroup == JsTickGroup.AfterTransform)
        vm.TickComponents(JsRuntimeManager.GroupLateUpdateBytes, deltaTime);
    }
  }
}
