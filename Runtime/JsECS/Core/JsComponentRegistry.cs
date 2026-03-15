namespace UnityJS.Entities.Core
{
  using System;
  using System.Collections.Generic;
  using QJS;
  using Unity.Entities;
  using UnityEngine;

  public delegate void JsLookupUpdater(ref SystemState state);

  public static class JsComponentRegistry
  {
    static readonly Dictionary<string, ComponentType> s_components = new();
    static readonly Dictionary<string, Func<ComponentType>> s_deferredComponents = new();
    static readonly List<Action<JSContext>> s_bridgeRegistrations = new();
    static readonly List<JsLookupUpdater> s_lookupUpdaters = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSession()
    {
      s_components.Clear();
      s_deferredComponents.Clear();
      s_bridgeRegistrations.Clear();
      s_lookupUpdaters.Clear();
    }

    public static void Register(string jsName, ComponentType componentType)
    {
      s_components[jsName] = componentType;
    }

    public static void RegisterBridge(
      string jsName,
      ComponentType componentType,
      Action<JSContext> registerFunc,
      JsLookupUpdater updateLookupFunc
    )
    {
      s_components[jsName] = componentType;
      s_bridgeRegistrations.Add(registerFunc);
      s_lookupUpdaters.Add(updateLookupFunc);
    }

    /// <summary>
    /// Deferred registration — the ComponentType factory is called lazily after TypeManager is initialized.
    /// Use this from [AfterAssembliesLoaded] where TypeManager is not yet ready.
    /// </summary>
    public static void RegisterBridgeDeferred(
      string jsName,
      Func<ComponentType> componentTypeFactory,
      Action<JSContext> registerFunc,
      JsLookupUpdater updateLookupFunc
    )
    {
      s_deferredComponents[jsName] = componentTypeFactory;
      s_bridgeRegistrations.Add(registerFunc);
      s_lookupUpdaters.Add(updateLookupFunc);
    }

    public static void RegisterEnum(Action<JSContext> registerFunc)
    {
      s_bridgeRegistrations.Add(registerFunc);
    }

    public static bool TryGetComponentType(string jsName, out ComponentType componentType)
    {
      if (s_components.TryGetValue(jsName, out componentType))
        return true;

      // Resolve deferred registrations on first access
      if (s_deferredComponents.TryGetValue(jsName, out var factory))
      {
        componentType = factory();
        s_components[jsName] = componentType;
        s_deferredComponents.Remove(jsName);
        return true;
      }

      componentType = default;
      return false;
    }

    public static void RegisterAllBridges(JSContext ctx)
    {
      // Resolve all deferred components now (TypeManager should be ready by this point)
      if (s_deferredComponents.Count > 0)
      {
        foreach (var kvp in s_deferredComponents)
          s_components[kvp.Key] = kvp.Value();
        s_deferredComponents.Clear();
      }

      foreach (var reg in s_bridgeRegistrations)
        reg(ctx);
    }

    public static void UpdateAllLookups(ref SystemState state)
    {
      foreach (var updater in s_lookupUpdaters)
        updater(ref state);
    }
  }
}
