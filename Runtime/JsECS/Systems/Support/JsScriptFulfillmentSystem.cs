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
  public partial class JsComponentInitSystem : SystemBase
  {
    JsRuntimeManager m_Vm;
    JsRuntimeManager m_LastVm;
    EntityQuery m_ScriptQuery;
    ComponentLookup<LocalTransform> m_TransformLookup;
    BufferLookup<JsScript> m_ScriptBufferLookup;

    protected override void OnCreate()
    {
      JsEntityRegistry.Initialize();
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
        m_Vm.LoadScriptFromString(
          "__ecs_component_glue",
          JsSystemRunner.ComponentGlueSourceForTests
        );

        InvalidateStaleScripts();
      }

      if (m_ScriptQuery.IsEmptyIgnoreFilter)
        return;

      using var ecb = new EntityCommandBuffer(Allocator.Temp);
      var entities = m_ScriptQuery.ToEntityArray(Allocator.Temp);

      foreach (var entity in entities)
      {
        if (!EntityManager.Exists(entity))
          continue;

        if (!EntityManager.HasComponent<JsEntityId>(entity))
          continue;

        var scripts = EntityManager.GetBuffer<JsScript>(entity);
        if (scripts.Length == 0)
          continue;

        var hasUnfulfilled = false;
        for (var i = 0; i < scripts.Length; i++)
          if (scripts[i].stateRef == -1 && !scripts[i].disabled)
          {
            hasUnfulfilled = true;
            break;
          }

        if (!hasUnfulfilled)
          continue;

        var entityId = JsEntityRegistry.GetOrAssignEntityId(entity, ecb, EntityManager);

        for (var i = 0; i < scripts.Length; i++)
        {
          var entry = scripts[i];
          if (entry.stateRef >= 0 || entry.disabled)
            continue;

          if (HasFulfilledScriptWithHash(scripts, entry.requestHash))
          {
            entry.disabled = true;
            scripts[i] = entry;
            Log.Verbose("[JsComponentInit] Skipping duplicate request hash for entity {0}", entity);
            continue;
          }

          var scriptName = JsScriptPathUtility.NormalizeScriptId(entry.scriptName.ToString());

          if (string.IsNullOrEmpty(scriptName))
          {
            Log.Error("[JsComponentInit] Script name is empty for entity {0}", entity);
            entry.disabled = true;
            scripts[i] = entry;
            continue;
          }

          if (!JsScriptSourceRegistry.TryReadScript(scriptName, out var source, out var resolvedId))
          {
            Log.Error("[JsComponentInit] Script not found in any source: {0}", scriptName);
            entry.disabled = true;
            scripts[i] = entry;
            continue;
          }

          if (m_Vm.HasScript(scriptName))
          {
            m_Vm.SetLastLoadedModule(scriptName);
          }
          else if (!m_Vm.LoadScriptAsModule(scriptName, source, resolvedId))
          {
            Log.Error("[JsComponentInit] Failed to load script: {0}", scriptName);
            entry.disabled = true;
            scripts[i] = entry;
            continue;
          }

          // Parse script annotations for tick group
          var annotations = JsScriptAnnotationParser.Parse(source);

          var stateRef = m_Vm.CreateEntityState(scriptName, entityId);
          if (stateRef < 0)
          {
            Log.Error(
              "[JsComponentInit] CreateEntityState failed for {0} entity={1}",
              scriptName,
              entityId
            );
            entry.disabled = true;
            scripts[i] = entry;
            continue;
          }

          entry.scriptName = new FixedString64Bytes(scriptName);
          entry.stateRef = stateRef;
          entry.entityIndex = entityId;
          entry.tickGroup = annotations.tickGroup;
          scripts[i] = entry;

          Log.Verbose(
            "[JsComponentInit] Initialized script '{0}' on entity {1}, stateRef={2}",
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

          var propsJson = entry.propertiesJson.IsEmpty ? null : entry.propertiesJson.ToString();
          if (!m_Vm.TryComponentInit(scriptName, entityId, propsJson))
            m_Vm.CallInit(scriptName, stateRef);

          JsECSBridge.ClearBurstContext();

          // Re-fetch buffer in case __componentInit modified it
          scripts = EntityManager.GetBuffer<JsScript>(entity);
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

        // Reset stale JsScript entries (stateRefs point to dead VM)
        // Set stateRef back to -1 so they get re-initialized
        var scripts = EntityManager.GetBuffer<JsScript>(entity);
        for (var i = 0; i < scripts.Length; i++)
        {
          var s = scripts[i];
          s.stateRef = -1;
          s.disabled = false;
          scripts[i] = s;
        }
      }

      entities.Dispose();
      Log.Debug("[JsComponentInit] VM changed — invalidated stale scripts for re-initialization");
    }

    static bool HasFulfilledScriptWithHash(DynamicBuffer<JsScript> scripts, Hash128 hash)
    {
      for (var i = 0; i < scripts.Length; i++)
        if (scripts[i].requestHash == hash && scripts[i].stateRef >= 0)
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
        if (script.scriptName == new Unity.Collections.FixedString64Bytes(scriptName) && !script.disabled && script.stateRef >= 0)
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

      if (m_Vm != null && m_Vm.IsValid)
      {
        var scriptName = m_Vm.Intern(script.scriptName);
        m_Vm.CallFunction(scriptName, JsRuntimeManager.OnDestroyBytes, script.stateRef);
        m_Vm.ReleaseEntityState(script.stateRef);
      }

      Log.Verbose(
        "[JsComponentInit] Disabled script on entity {0}",
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
