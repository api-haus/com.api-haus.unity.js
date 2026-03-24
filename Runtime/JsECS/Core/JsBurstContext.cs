namespace UnityJS.Entities.Core
{
  using System.Threading;
  using Components;
  using Unity.Burst;
  using Unity.Collections;
  using Unity.Collections.LowLevel.Unsafe;
  using Unity.Entities;
  using Unity.Transforms;

  /// <summary>
  /// Owns the Burst-compatible SharedStatic context, pending entity tracking,
  /// and entity ID allocation. Updated each frame before script execution.
  /// Extracted from JsECSBridge.
  /// </summary>
  public static class JsBurstContext
  {
    struct BurstContextMarker { }
    struct NextEntityIdMarker { }
    struct PendingEntitiesMarker { }

    static readonly SharedStatic<BurstBridgeContext> s_burstContext =
      SharedStatic<BurstBridgeContext>.GetOrCreate<BurstContextMarker, BurstBridgeContext>();

    static readonly SharedStatic<UnsafeHashMap<int, Entity>> s_pendingEntities = SharedStatic<
      UnsafeHashMap<int, Entity>
    >.GetOrCreate<PendingEntitiesMarker, UnsafeHashMap<int, Entity>>();

    static readonly SharedStatic<int> s_nextEntityId =
      SharedStatic<int>.GetOrCreate<NextEntityIdMarker>();

    /// <summary>
    /// Direct ref access for hot-path P/Invoke bridge callbacks.
    /// </summary>
    internal static ref BurstBridgeContext Context => ref s_burstContext.Data;

    public static bool IsValid => s_burstContext.Data.isValid;

    internal static void Initialize()
    {
      s_nextEntityId.Data = 1;
      s_pendingEntities.Data = new UnsafeHashMap<int, Entity>(32, Allocator.Persistent);
    }

    internal static void Shutdown()
    {
      s_burstContext.Data = default;

      if (s_pendingEntities.Data.IsCreated)
        s_pendingEntities.Data.Dispose();
    }

    public static void Update(
      EntityCommandBuffer ecb,
      float deltaTime,
      ComponentLookup<LocalTransform> transformLookup,
      BufferLookup<JsScript> scriptBufferLookup
    )
    {
      if (!JsEntityRegistry.IsCreated)
      {
        s_burstContext.Data = default;
        return;
      }

      if (s_pendingEntities.Data.IsCreated)
        s_pendingEntities.Data.Clear();

      s_burstContext.Data = new BurstBridgeContext
      {
        ecb = ecb,
        deltaTime = deltaTime,
        entityIdMap = JsEntityRegistry.EntityIdMap,
        transformLookup = transformLookup,
        scriptBufferLookup = scriptBufferLookup,
        isValid = true,
      };
    }

    public static void Clear()
    {
      s_burstContext.Data = default;
    }

    public static bool TryGetECB(out EntityCommandBuffer ecb)
    {
      ref var ctx = ref s_burstContext.Data;
      if (ctx.isValid)
      {
        ecb = ctx.ecb;
        return true;
      }

      ecb = default;
      return false;
    }

    // ── Entity ID allocation ──

    internal static int AllocateEntityId()
    {
      return Interlocked.Increment(ref s_nextEntityId.Data);
    }

    public static void SyncNextEntityId(int nextId)
    {
      var current = s_nextEntityId.Data;
      while (current < nextId)
      {
        var prev = Interlocked.CompareExchange(ref s_nextEntityId.Data, nextId, current);
        if (prev == current)
          break;
        current = prev;
      }
    }

    // ── Pending entity tracking ──

    internal static void AddPendingEntity(int entityId, Entity entity)
    {
      if (s_pendingEntities.Data.IsCreated)
        s_pendingEntities.Data.TryAdd(entityId, entity);
    }

    internal static Entity GetPendingEntity(int entityId)
    {
      if (
        s_pendingEntities.Data.IsCreated
        && s_pendingEntities.Data.TryGetValue(entityId, out var entity)
      )
        return entity;
      return Entity.Null;
    }

    public static bool IsPendingEntity(int entityId)
    {
      return s_pendingEntities.Data.IsCreated && s_pendingEntities.Data.ContainsKey(entityId);
    }

    // ── Burst-compatible lookups ──

    public static Entity GetEntityFromId(int entityId)
    {
      ref var ctx = ref s_burstContext.Data;
      if (!ctx.isValid || entityId <= 0)
        return Entity.Null;

      if (ctx.entityIdMap.TryGetValue(entityId, out var entity) && entity != Entity.Null)
        return entity;

      return GetPendingEntity(entityId);
    }

    internal static bool TryGetTransform(Entity entity, out LocalTransform transform)
    {
      ref var ctx = ref s_burstContext.Data;
      transform = default;

      if (!ValidateEntity(entity, ref ctx))
        return false;

      if (!ctx.transformLookup.HasComponent(entity))
        return false;

      transform = ctx.transformLookup[entity];
      return true;
    }

    internal static bool TrySetTransform(Entity entity, LocalTransform transform)
    {
      ref var ctx = ref s_burstContext.Data;

      if (!ValidateEntity(entity, ref ctx))
        return false;

      if (!ctx.transformLookup.HasComponent(entity))
        return false;

      ctx.transformLookup[entity] = transform;
      return true;
    }

    internal static bool HasScript(Entity entity, FixedString64Bytes scriptName)
    {
      ref var ctx = ref s_burstContext.Data;
      if (!ValidateEntity(entity, ref ctx))
        return false;

      if (!ctx.scriptBufferLookup.HasBuffer(entity))
        return false;

      var scripts = ctx.scriptBufferLookup[entity];
      for (var i = 0; i < scripts.Length; i++)
        if (scripts[i].scriptName == scriptName)
          return true;

      return false;
    }

    static bool ValidateEntity(Entity entity, ref BurstBridgeContext ctx)
    {
      return ctx.isValid && entity != Entity.Null && entity.Index >= 0;
    }
  }
}
