namespace UnityJS.Entities.Systems.Support
{
  using Components;
  using Core;
  using Runtime;
  using Tick;
  using Unity.Collections;
  using Unity.Entities;
  using Unity.Logging;
  using Unity.Transforms;

  [UpdateInGroup(typeof(InitializationSystemGroup))]
  public partial class JsComponentInitSystem : SystemBase
  {
    JsRuntimeManager m_Vm;
    JsRuntimeManager m_LastVm;
    bool m_WasPlaying;
    bool m_LoggedFulfillment;
    EntityQuery m_ScriptQuery;
    ComponentLookup<LocalTransform> m_TransformLookup;
    BufferLookup<JsScript> m_ScriptBufferLookup;

    // Track query version so we can skip OnUpdate when no structural changes
    uint m_LastOrderVersion;

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

    internal static void InitializeVm(JsRuntimeManager vm, World world)
    {
      vm.BridgeState ??= new JsBridgeState();
      JsECSBridge.Initialize(world);
      vm.RegisterBridgeNow(JsECSBridge.RegisterFunctions);
      vm.RegisterBridgeNow(JsQueryBridge.Register);
      vm.RegisterBridgeNow(JsComponentRegistry.RegisterAllBridges);
      vm.RegisterBridgeNow(JsComponentStore.Register);
      vm.LoadScriptFromString("__ecs_query_builder", JsEcsGlueSource.QueryBuilder);
      vm.LoadScriptFromString("__ecs_component_glue", JsEcsGlueSource.ComponentGlue);
    }

    protected override void OnStartRunning()
    {
      var existing = JsRuntimeManager.Instance;
      var hadExisting = existing != null && existing.IsValid;
      if (hadExisting)
        existing.Dispose();

      m_Vm = JsRuntimeManager.GetOrCreate();
      JsScriptSearchPaths.Initialize();
      InitializeVm(m_Vm, World);
      m_LastVm = m_Vm;
      JsTickSystemHelper.ClearActiveTickGroups();

      m_WasPlaying = UnityEngine.Application.isPlaying;

      UnityEngine.Debug.Log(
        $"[JsComponentInit] OnStartRunning — v{JsRuntimeManager.InstanceVersion} " +
        $"(disposedStale={hadExisting})");
    }

    protected override void OnUpdate()
    {
      JsEntityRegistry.BeginFrame(EntityManager);

      // Detect edit → play transition: the VM's QuickJS module cache has
      // stale synthetic module namespaces from edit mode. Dispose and
      // recreate so play mode starts with a clean context.
      var isPlaying = UnityEngine.Application.isPlaying;
      if (isPlaying && !m_WasPlaying)
      {
        m_Vm?.Dispose();
        m_Vm = JsRuntimeManager.GetOrCreate();
        JsScriptSearchPaths.Initialize();
        InitializeVm(m_Vm, World);
        m_LastVm = m_Vm;
        m_LoggedFulfillment = false;
        JsTickSystemHelper.ClearActiveTickGroups();
        InvalidateStaleScripts();
      }
      m_WasPlaying = isPlaying;

      if (m_Vm == null || !m_Vm.IsValid)
      {
        if (JsRuntimeManager.Instance != null && JsRuntimeManager.Instance.IsValid)
          m_Vm = JsRuntimeManager.Instance;
        else
          return;
      }

      if (m_Vm != m_LastVm)
      {
        m_LastVm = m_Vm;
        InitializeVm(m_Vm, World);
        JsTickSystemHelper.ClearActiveTickGroups();
        InvalidateStaleScripts();
      }

      if (m_ScriptQuery.IsEmptyIgnoreFilter)
        return;

      // Fast path: once all scripts are fulfilled, only re-scan if new entities
      // were added (structural change detected via query order version).
      var currentOrderVersion = (uint)m_ScriptQuery.GetCombinedComponentOrderVersion();
      if (m_LoggedFulfillment && currentOrderVersion == m_LastOrderVersion)
        return;
      m_LastOrderVersion = currentOrderVersion;

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
        // Ensure entity is in the main map (not just pending) so BurstContext can find it
        JsEntityRegistry.RegisterImmediate(entity, entityId, EntityManager);

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
            UnityEngine.Debug.LogError($"[JsComponentInit] Script name is empty for entity {entity}");
            entry.disabled = true;
            scripts[i] = entry;
            continue;
          }

          if (!JsScriptSourceRegistry.TryReadScript(scriptName, out var source, out var resolvedId))
          {
            UnityEngine.Debug.LogError($"[JsComponentInit] Script not found in any source: {scriptName}");
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
            UnityEngine.Debug.LogError($"[JsComponentInit] Failed to load script: {scriptName}");
            entry.disabled = true;
            scripts[i] = entry;
            continue;
          }

          // Parse script annotations for tick group
          var annotations = JsScriptAnnotationParser.Parse(source);

          var stateRef = m_Vm.CreateEntityState(scriptName, entityId);
          if (stateRef < 0)
          {
            UnityEngine.Debug.LogError($"[JsComponentInit] CreateEntityState failed for {scriptName} entity={entityId}");
            entry.disabled = true;
            scripts[i] = entry;
            continue;
          }

          entry.scriptName = new FixedString64Bytes(scriptName);
          entry.stateRef = stateRef;
          entry.entityIndex = entityId;
          entry.tickGroup = annotations.tickGroup;
          scripts[i] = entry;

          // Register this tick group as active so the corresponding tick system runs.
          JsTickSystemHelper.SetTickGroupActive(annotations.tickGroup);

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

      // Log fulfillment summary once after first batch of scripts is loaded
      if (!m_LoggedFulfillment && m_Vm != null && m_Vm.IsValid)
      {
        var fulfilled = 0;
        var total = 0;
        var allEntities = m_ScriptQuery.ToEntityArray(Allocator.Temp);
        foreach (var e in allEntities)
        {
          if (!EntityManager.Exists(e)) continue;
          var buf = EntityManager.GetBuffer<JsScript>(e);
          for (var s = 0; s < buf.Length; s++)
          {
            if (buf[s].disabled) continue;
            total++;
            if (buf[s].stateRef >= 0) fulfilled++;
          }
        }
        allEntities.Dispose();

        if (total > 0 && fulfilled == total)
        {
          UnityEngine.Debug.Log($"[JsComponentInit] All scripts fulfilled: {fulfilled}/{total}");
          m_LoggedFulfillment = true;
        }
      }
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
