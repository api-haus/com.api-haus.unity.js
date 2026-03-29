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
    // Bitmask tracking which tick groups have at least one active script.
    // Set by JsComponentInitSystem when scripts are fulfilled.
    // Bit N = 1 << (int)JsTickGroup.
    static int s_activeTickGroups;

    public static bool IsTickGroupActive(JsTickGroup group) =>
      (s_activeTickGroups & (1 << (int)group)) != 0;

    public static void SetTickGroupActive(JsTickGroup group) =>
      s_activeTickGroups |= 1 << (int)group;

    public static void ClearActiveTickGroups() => s_activeTickGroups = 0;

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
      // Component lifecycle ticks (fixedUpdate, lateUpdate) are independent of
      // entity script tick groups — always run them.
      var hasComponentLifecycle =
        tickGroup == JsTickGroup.Fixed || tickGroup == JsTickGroup.AfterTransform;
      var hasEntityScripts = IsTickGroupActive(tickGroup);

      // Fast path: skip entirely if no entity scripts AND no component lifecycle.
      if (!hasEntityScripts && !hasComponentLifecycle)
        return;

      var vm = JsRuntimeManager.Instance;
      if (vm == null || !vm.IsValid)
        return;

      var worldTime = state.WorldUnmanaged.Time;
      var deltaTime = worldTime.DeltaTime;
      if (deltaTime <= 0f)
        deltaTime = UnityEngine.Time.deltaTime;

      var elapsedTime = worldTime.ElapsedTime;

      if (hasEntityScripts || hasComponentLifecycle)
      {
        state.CompleteDependency();
        tickState.TransformLookup.Update(ref state);
        tickState.ScriptBufferLookup.Update(ref state);
        JsComponentRegistry.UpdateAllLookups(ref state);
        JsECSBridge.UpdateBurstContext(ecb, deltaTime, tickState.TransformLookup, tickState.ScriptBufferLookup);
      }

      if (hasEntityScripts)
      {
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

            if (!state.EntityManager.Exists(entity))
              break;
          }
        }

        entities.Dispose();
      }

      // Tick Component lifecycle methods (fixedUpdate, lateUpdate).
      if (tickGroup == JsTickGroup.Fixed)
        vm.TickComponents(JsRuntimeManager.GroupFixedUpdateBytes, deltaTime);
      else if (tickGroup == JsTickGroup.AfterTransform)
        vm.TickComponents(JsRuntimeManager.GroupLateUpdateBytes, deltaTime);
    }
  }
}
