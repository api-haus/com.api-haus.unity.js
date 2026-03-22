namespace UnityJS.Entities.Core
{
  using System;
  using System.Collections.Generic;
  using Unity.Entities;
  using Unity.Mathematics;
  using UnityEngine;

  /// <summary>
  /// Instance-scoped container for all mutable bridge state.
  /// Owned by JsRuntimeManager — disposed atomically when the VM shuts down.
  /// Eliminates scattered static state that previously leaked across reloads.
  /// </summary>
  public class JsBridgeState : IDisposable
  {
    // ── Query (from JsQueryBridge) ──
    public readonly Dictionary<int, EntityQuery> QueryCache = new();
    public readonly Dictionary<int, (ComponentType[] all, ComponentType[] none)> PendingQueries =
      new();
    public readonly Dictionary<int, (int[] ids, int count)> PrecomputedIds = new();
    public EntityManager QueryEntityManager;
    public bool QueryInitialized;

    // ── Component store (from JsComponentStore) ──
    public const int MaxSlots = 64;
    public readonly Dictionary<string, int> NameToSlot = new();
    public readonly string[] SlotToName = new string[MaxSlots];
    public readonly Dictionary<string, Dictionary<string, string>> Schemas = new();
    public readonly Dictionary<int, HashSet<string>> EntityComponents = new();
    public readonly HashSet<int> EntitiesWithCleanup = new();
    public int NextSlot;

    // ── System bridge (from JsSystemBridge) ──
    public Unity.Mathematics.Random SystemRandom;

    public void Dispose()
    {
      // Query — don't Dispose() EntityQuery handles, they're dead with the world
      QueryCache.Clear();
      PendingQueries.Clear();
      PrecomputedIds.Clear();
      QueryEntityManager = default;
      QueryInitialized = false;

      // Component store
      NameToSlot.Clear();
      Schemas.Clear();
      EntityComponents.Clear();
      EntitiesWithCleanup.Clear();
      NextSlot = 0;
      Array.Clear(SlotToName, 0, SlotToName.Length);

      // System bridge
      SystemRandom = default;

      // Search paths + source registry reset via [SubsystemRegistration] on their own classes
    }
  }
}
