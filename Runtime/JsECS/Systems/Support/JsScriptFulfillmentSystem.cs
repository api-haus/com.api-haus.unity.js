namespace UnityJS.Entities.Systems.Support
{
  using Components;
  using Core;
  using Runtime;
  using Unity.Collections;
  using Unity.Entities;
  using Unity.Logging;
  using Unity.Transforms;

  [UpdateInGroup(typeof(InitializationSystemGroup))]
  public partial class JsScriptFulfillmentSystem : SystemBase
  {
    JsRuntimeManager m_Vm;
    JsRuntimeManager m_LastVm;
    EntityQuery m_RequestQuery;
    EntityQuery m_ScriptQuery;
    ComponentLookup<LocalTransform> m_TransformLookup;
    BufferLookup<JsScript> m_ScriptBufferLookup;

    protected override void OnCreate()
    {
      JsEntityRegistry.Initialize();
      m_RequestQuery = GetEntityQuery(ComponentType.ReadWrite<JsScriptRequest>());
      m_ScriptQuery = GetEntityQuery(ComponentType.ReadWrite<JsScript>());
      m_TransformLookup = GetComponentLookup<LocalTransform>();
      m_ScriptBufferLookup = GetBufferLookup<JsScript>(true);
    }

    protected override void OnDestroy()
    {
      JsEntityRegistry.Dispose();
    }

    protected override void OnStartRunning()
    {
      m_Vm = JsRuntimeManager.Instance ?? JsRuntimeManager.GetOrCreate();

      // Create bridge state if not yet set (first system to touch the VM)
      m_Vm.BridgeState ??= new JsBridgeState();

      JsScriptSearchPaths.Initialize();

      // Ensure ECS bridge is initialized before we prime ECB context.
      // Without this, UpdateBurstContext silently fails because s_initialized
      // is false until JsSystemRunner (SimulationSystemGroup) calls it — too late.
      JsECSBridge.Initialize(World);

      // Register ALL bridges before any script evaluation — module imports
      // (unity.js/ecs, unity.js/components) bind globals at eval time.
      m_Vm.RegisterBridgeNow(JsECSBridge.RegisterFunctions);
      m_Vm.RegisterBridgeNow(JsQueryBridge.Register);
      m_Vm.RegisterBridgeNow(JsComponentRegistry.RegisterAllBridges);
      m_Vm.RegisterBridgeNow(JsComponentStore.Register);

      m_Vm.LoadScriptFromString("__ecs_query_builder", JsSystemRunner.QueryBuilderSourceForTests);
      m_Vm.LoadScriptFromString("__ecs_component_glue", JsSystemRunner.ComponentGlueSourceForTests);

      m_LastVm = m_Vm;
    }

    protected override void OnUpdate()
    {
      JsEntityRegistry.BeginFrame(EntityManager);

      if (m_Vm == null || !m_Vm.IsValid)
      {
        if (JsRuntimeManager.Instance != null && JsRuntimeManager.Instance.IsValid)
          m_Vm = JsRuntimeManager.Instance;
        else
          return;
      }

      // VM changed (domain reload destroyed the old one).
      // Re-register bridges + glue on the new VM — OnStartRunning only ran
      // on the old one. Scripts must import the glue-wrapped ecs.get (with
      // auto-flush) not the raw bridge function.
      if (m_Vm != m_LastVm)
      {
        m_LastVm = m_Vm;

        m_Vm.BridgeState ??= new JsBridgeState();
        JsECSBridge.Initialize(World);
        m_Vm.RegisterBridgeNow(JsECSBridge.RegisterFunctions);
        m_Vm.RegisterBridgeNow(JsQueryBridge.Register);
        m_Vm.RegisterBridgeNow(JsComponentRegistry.RegisterAllBridges);
        m_Vm.RegisterBridgeNow(JsComponentStore.Register);
        m_Vm.LoadScriptFromString("__ecs_query_builder", JsSystemRunner.QueryBuilderSourceForTests);
        m_Vm.LoadScriptFromString("__ecs_component_glue", JsSystemRunner.ComponentGlueSourceForTests);

        InvalidateStaleScripts();
      }

      if (m_RequestQuery.IsEmptyIgnoreFilter)
        return;

      using var ecb = new EntityCommandBuffer(Allocator.Temp);
      var entities = m_RequestQuery.ToEntityArray(Allocator.Temp);

      foreach (var entity in entities)
      {
        if (!EntityManager.Exists(entity))
          continue;

        if (!EntityManager.HasComponent<JsEntityId>(entity))
          continue;

        var requests = EntityManager.GetBuffer<JsScriptRequest>(entity);
        if (requests.Length == 0)
          continue;

        var hasUnfulfilled = false;
        for (var i = 0; i < requests.Length; i++)
          if (!requests[i].fulfilled)
          {
            hasUnfulfilled = true;
            break;
          }

        if (!hasUnfulfilled)
          continue;

        var entityId = JsEntityRegistry.GetOrAssignEntityId(entity, ecb, EntityManager);

        if (!EntityManager.HasBuffer<JsScript>(entity))
        {
          EntityManager.AddBuffer<JsScript>(entity);
          requests = EntityManager.GetBuffer<JsScriptRequest>(entity);
        }

        var scripts = EntityManager.GetBuffer<JsScript>(entity);

        for (var i = 0; i < requests.Length; i++)
        {
          var request = requests[i];
          if (request.fulfilled)
            continue;

          if (HasScriptWithHash(scripts, request.requestHash))
          {
            request.fulfilled = true;
            requests[i] = request;
            Log.Verbose("[JsFulfillment] Skipping duplicate request hash for entity {0}", entity);
            continue;
          }

          var scriptName = JsScriptPathUtility.NormalizeScriptId(request.scriptName.ToString());

          if (string.IsNullOrEmpty(scriptName))
          {
            Log.Error("[JsFulfillment] Script name is empty for entity {0}", entity);
            request.fulfilled = true;
            requests[i] = request;
            continue;
          }

          if (!JsScriptSourceRegistry.TryReadScript(scriptName, out var source, out var resolvedId))
          {
            Log.Error("[JsFulfillment] Script not found in any source: {0}", scriptName);
            request.fulfilled = true;
            requests[i] = request;
            continue;
          }

          if (!m_Vm.LoadScriptAsModule(scriptName, source, resolvedId))
          {
            Log.Error("[JsFulfillment] Failed to load script: {0}", scriptName);
            request.fulfilled = true;
            requests[i] = request;
            continue;
          }

          // Parse script annotations for tick group
          var annotations = JsScriptAnnotationParser.Parse(source);

          var stateRef = m_Vm.CreateEntityState(scriptName, entityId);
          if (stateRef < 0)
          {
            Log.Error(
              "[JsFulfillment] CreateEntityState failed for {0} entity={1}",
              scriptName,
              entityId
            );
            request.fulfilled = true;
            requests[i] = request;
            continue;
          }

          scripts = EntityManager.GetBuffer<JsScript>(entity);
          scripts.Add(
            new JsScript
            {
              scriptName = new FixedString64Bytes(scriptName),
              stateRef = stateRef,
              entityIndex = entityId,
              requestHash = request.requestHash,
              disabled = false,
              tickGroup = annotations.tickGroup,
            }
          );

          Log.Verbose(
            "[JsFulfillment] Fulfilled script '{0}' on entity {1}, stateRef={2}",
            scriptName,
            entityId,
            stateRef
          );

          // Prime BurstContext so _nativeAdd in __componentInit can use the ECB.
          // start() is deferred to the first TickComponents call in JsSystemRunner
          // where generated bridge lookups are guaranteed fresh.
          m_TransformLookup.Update(this);
          m_ScriptBufferLookup.Update(this);
          JsECSBridge.UpdateBurstContext(ecb, 0f, m_TransformLookup, m_ScriptBufferLookup);

          var propsJson = request.propertiesJson.IsEmpty
            ? null
            : request.propertiesJson.ToString();
          if (!m_Vm.TryComponentInit(scriptName, entityId, propsJson))
            m_Vm.CallInit(scriptName, stateRef);

          JsECSBridge.ClearBurstContext();

          requests = EntityManager.GetBuffer<JsScriptRequest>(entity);
          request.fulfilled = true;
          requests[i] = request;
        }
      }

      entities.Dispose();
      ecb.Playback(EntityManager);
      JsEntityRegistry.CommitPendingCreations(EntityManager);
    }

    void InvalidateStaleScripts()
    {
      var entities = m_ScriptQuery.ToEntityArray(Allocator.Temp);
      foreach (var entity in entities)
      {
        if (!EntityManager.Exists(entity))
          continue;

        // Clear stale JsScript entries (stateRefs point to dead VM)
        var scripts = EntityManager.GetBuffer<JsScript>(entity);
        scripts.Clear();

        // Unfulfill all requests so they get re-processed
        if (EntityManager.HasBuffer<JsScriptRequest>(entity))
        {
          var requests = EntityManager.GetBuffer<JsScriptRequest>(entity);
          for (var i = 0; i < requests.Length; i++)
          {
            var r = requests[i];
            r.fulfilled = false;
            requests[i] = r;
          }
        }
      }

      entities.Dispose();
      Log.Debug("[JsFulfillment] VM changed — invalidated stale scripts for re-fulfillment");
    }

    static bool HasScriptWithHash(DynamicBuffer<JsScript> scripts, Hash128 hash)
    {
      for (var i = 0; i < scripts.Length; i++)
        if (scripts[i].requestHash == hash)
          return true;
      return false;
    }

    public bool DisableScript(Entity entity, string scriptName)
    {
      if (!EntityManager.HasBuffer<JsScript>(entity))
        return false;

      var scripts = EntityManager.GetBuffer<JsScript>(entity);
      for (var i = 0; i < scripts.Length; i++)
      {
        var script = scripts[i];
        if (script.scriptName.ToString() == scriptName && !script.disabled && script.stateRef >= 0)
        {
          DisableScriptAtIndex(entity, scripts, i);
          return true;
        }
      }

      return false;
    }

    public bool DisableScriptByHash(Entity entity, Hash128 hash)
    {
      if (!EntityManager.HasBuffer<JsScript>(entity))
        return false;

      var scripts = EntityManager.GetBuffer<JsScript>(entity);
      for (var i = 0; i < scripts.Length; i++)
      {
        var script = scripts[i];
        if (script.requestHash == hash && !script.disabled && script.stateRef >= 0)
        {
          DisableScriptAtIndex(entity, scripts, i);
          return true;
        }
      }

      return false;
    }

    void DisableScriptAtIndex(Entity entity, DynamicBuffer<JsScript> scripts, int index)
    {
      var script = scripts[index];
      var scriptName = script.scriptName.ToString();

      if (m_Vm != null && m_Vm.IsValid)
      {
        m_Vm.CallFunction(scriptName, "onDestroy", script.stateRef);
        m_Vm.ReleaseEntityState(script.stateRef);
      }

      Log.Verbose(
        "[JsFulfillment] Disabled script '{0}' on entity {1}",
        scriptName,
        script.entityIndex
      );

      script.stateRef = -1;
      script.disabled = true;
      scripts[index] = script;
    }

    public int GetEntityIdFromEntity(Entity entity)
    {
      return JsEntityRegistry.GetEntityIdFromEntity(entity, EntityManager);
    }

    public Entity GetEntityFromId(int entityId)
    {
      return JsEntityRegistry.GetEntityFromId(entityId);
    }
  }
}
