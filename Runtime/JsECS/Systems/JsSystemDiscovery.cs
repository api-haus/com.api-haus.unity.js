namespace UnityJS.Entities.Systems
{
  using Core;
  using Runtime;
  using Unity.Collections;
  using Unity.Entities;
  using Unity.Logging;

  /// <summary>
  /// Discovers, loads, and reloads JS system scripts from registered sources.
  /// Extracted from JsSystemRunner to isolate discovery/loading concerns.
  /// </summary>
  public static class JsSystemDiscovery
  {
    public static void DiscoverAndLoad(
      ref JsSystemRunnerData data,
      ref JsSystemManifest manifest,
      JsRuntimeManager vm
    )
    {
      JsScriptSearchPaths.Initialize();
      var systems = JsScriptSourceRegistry.DiscoverAllSystems();
      if (systems.Count == 0)
      {
        Log.Warning("[JsSystemDiscovery] No system scripts found in any registered source");
        manifest.initialized = true;
        return;
      }

      foreach (var (systemName, source) in systems)
      {
        var fsName = new FixedString64Bytes(systemName);
        if (data.SystemStateRefs.ContainsKey(fsName))
          continue;

        if (!source.TryReadScript("systems/" + systemName, out var code, out var resolvedId))
          continue;

        LoadSystem(ref data, vm, systemName, fsName, code, resolvedId);
      }

      manifest.initialized = true;
    }

    public static void LoadSystem(
      ref JsSystemRunnerData data,
      JsRuntimeManager vm,
      string systemName,
      FixedString64Bytes fsName,
      string source,
      string resolvedId
    )
    {
      var scriptId = "system:" + systemName;

      if (!vm.ReloadScript(scriptId, source, resolvedId))
      {
        Log.Error("[JsSystemDiscovery] Failed to load system '{0}'", systemName);
        return;
      }

      var stateRef = vm.CreateEntityState(scriptId, -1);
      if (stateRef < 0)
      {
        Log.Error("[JsSystemDiscovery] Failed to create state for system '{0}'", systemName);
        return;
      }

      var fsScriptId = new FixedString64Bytes(scriptId);
      data.SystemStateRefs[fsName] = stateRef;
      data.SystemScriptIds[fsName] = fsScriptId;
      data.SystemNames.Add(fsName);
    }

    /// <summary>
    /// Forces system re-discovery on the next OnUpdate. Used by test harnesses
    /// after changing search paths to load fixture systems.
    /// </summary>
    public static void ForceRediscovery(ref SystemState state)
    {
      ref var data = ref state.EntityManager
        .GetComponentDataRW<JsSystemRunnerData>(state.SystemHandle).ValueRW;
      data.LastVmVersion = -1;
    }

    public static void ReloadSystem(ref SystemState state, string systemName)
    {
      var vm = JsRuntimeManager.Instance;
      if (vm == null || !vm.IsValid)
        return;

      ref var data = ref state.EntityManager
        .GetComponentDataRW<JsSystemRunnerData>(state.SystemHandle).ValueRW;

      var fsName = new FixedString64Bytes(systemName);
      if (data.SystemStateRefs.TryGetValue(fsName, out var oldRef))
      {
        vm.ReleaseEntityState(oldRef);
        data.SystemStateRefs.Remove(fsName);
        data.SystemScriptIds.Remove(fsName);
        for (var i = 0; i < data.SystemNames.Length; i++)
        {
          if (data.SystemNames[i] == fsName)
          {
            data.SystemNames.RemoveAtSwapBack(i);
            break;
          }
        }
      }

      if (!JsScriptSourceRegistry.TryReadScript(
            "systems/" + systemName, out var source, out var resolvedId))
        return;

      LoadSystem(ref data, vm, systemName, fsName, source, resolvedId);
    }
  }
}
