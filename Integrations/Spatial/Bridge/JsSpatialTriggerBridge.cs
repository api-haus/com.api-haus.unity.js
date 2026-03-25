using MiniSpatial;
using UnityJS.Entities.Core;

namespace UnityJS.Integration.Spatial
{
  using System.Collections.Generic;
  using System.Runtime.InteropServices;
  using System.Text;
  using AOT;
  using Unity.Collections;
  using Unity.Entities;
  using UnityEngine;
  using UnityJS.QJS;
  using UnityJS.Runtime;

  public static class JsSpatialTriggerBridge
  {
    static readonly byte[] s_on = Encoding.UTF8.GetBytes("on\0");
    static readonly byte[] s_destroy = Encoding.UTF8.GetBytes("destroy\0");
    static readonly byte[] s_eid = Encoding.UTF8.GetBytes("_eid\0");
    static readonly byte[] s_dead = Encoding.UTF8.GetBytes("_dead\0");
    static readonly byte[] s_enter = Encoding.UTF8.GetBytes("enter\0");
    static readonly byte[] s_stay = Encoding.UTF8.GetBytes("stay\0");
    static readonly byte[] s_exit = Encoding.UTF8.GetBytes("exit\0");

    struct TriggerCallbacks
    {
      public JSValue enter;
      public JSValue stay;
      public JSValue exit;
      public bool hasEnter;
      public bool hasStay;
      public bool hasExit;
    }

    static readonly Dictionary<int, TriggerCallbacks> s_callbacks = new();
    static readonly HashSet<int> s_pendingRemove = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    internal static void AutoRegister() => JsFunctionRegistry.Register("spatial", RegisterTriggerFunctions);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    internal static void ResetSession()
    {
      s_callbacks.Clear();
      s_pendingRemove.Clear();
    }

    static unsafe void RegisterTriggerFunctions(JSContext ctx, JSValue ns)
    {
      var bytes = Encoding.UTF8.GetBytes("trigger\0");
      fixed (byte* p = bytes)
      {
        var fn = QJSShim.qjs_shim_new_function(ctx, Spatial_Trigger, p, 3);
        QJS.JS_SetPropertyStr(ctx, ns, p, fn);
      }
    }

    #region Helpers

    static unsafe void RetUndefined(long* outU, long* outTag)
    {
      var v = QJS.JS_UNDEFINED;
      *outU = v.u;
      *outTag = v.tag;
    }

    #endregion

    #region Bridge Function: spatial.trigger(eid, tag, shapeObj)

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void Spatial_Trigger(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      int entityId;
      QJS.JS_ToInt32(ctx, &entityId, argv[0]);
      if (entityId <= 0)
      {
        RetUndefined(outU, outTag);
        return;
      }

      var tagHash = ReadTagHash(ctx, argv, 1);
      if (tagHash == 0)
      {
        RetUndefined(outU, outTag);
        return;
      }

      var shape = MarshalShape(ctx, argv[2]);

      if (!JsECSBridge.TryGetBurstContextECB(out var ecb))
      {
        RetUndefined(outU, outTag);
        return;
      }

      var entity = JsECSBridge.GetEntityFromIdBurst(entityId);
      if (entity == Entity.Null)
      {
        RetUndefined(outU, outTag);
        return;
      }

      // If entity already has a trigger, free old callbacks
      if (s_callbacks.TryGetValue(entityId, out var oldCb))
        FreeCallbacks(ctx, ref oldCb);

      // Add ECS components via ECB
      ecb.AddComponent(entity, new SpatialTrigger { shape = shape, targetTag = tagHash });
      ecb.AddBuffer<StatefulSpatialOverlap>(entity);
      ecb.AddBuffer<PreviousSpatialOverlap>(entity);

      // Initialize empty callbacks
      s_callbacks[entityId] = new TriggerCallbacks();

      // Build JS handle object
      var handle = QJS.JS_NewObject(ctx);

      // Store entity ID on handle
      fixed (byte* pEid = s_eid)
        QJS.JS_SetPropertyStr(ctx, handle, pEid, QJS.NewInt32(ctx, entityId));

      // .on(eventName, callback) method
      fixed (byte* pOn = s_on)
      {
        var onFn = QJSShim.qjs_shim_new_function(ctx, Handle_On, pOn, 2);
        QJS.JS_SetPropertyStr(ctx, handle, pOn, onFn);
      }

      // .destroy() method
      fixed (byte* pDestroy = s_destroy)
      {
        var destroyFn = QJSShim.qjs_shim_new_function(ctx, Handle_Destroy, pDestroy, 0);
        QJS.JS_SetPropertyStr(ctx, handle, pDestroy, destroyFn);
      }

      *outU = handle.u;
      *outTag = handle.tag;
    }

    #endregion

    #region Handle Methods

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void Handle_On(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      // 'this' is the handle object
      var thisVal = new JSValue { u = thisU, tag = thisTag };

      // Read entity ID from handle
      int entityId;
      fixed (byte* pEid = s_eid)
      {
        var eidVal = QJS.JS_GetPropertyStr(ctx, thisVal, pEid);
        QJS.JS_ToInt32(ctx, &entityId, eidVal);
        QJS.JS_FreeValue(ctx, eidVal);
      }

      if (!s_callbacks.TryGetValue(entityId, out var cb))
      {
        // Handle is dead or invalid, return this for chaining
        *outU = thisVal.u;
        *outTag = thisVal.tag;
        // Dup since we're returning it
        QJS.JS_DupValue(ctx, thisVal);
        return;
      }

      // Read event name
      var namePtr = QJS.JS_ToCString(ctx, argv[0]);
      if (namePtr == null)
      {
        QJS.JS_DupValue(ctx, thisVal);
        *outU = thisVal.u;
        *outTag = thisVal.tag;
        return;
      }
      var name = Marshal.PtrToStringUTF8((nint)namePtr);
      QJS.JS_FreeCString(ctx, namePtr);

      var callback = argv[1];
      QJS.JS_DupValue(ctx, callback);

      switch (name)
      {
        case "enter":
          if (cb.hasEnter)
            QJS.JS_FreeValue(ctx, cb.enter);
          cb.enter = callback;
          cb.hasEnter = true;
          break;
        case "stay":
          if (cb.hasStay)
            QJS.JS_FreeValue(ctx, cb.stay);
          cb.stay = callback;
          cb.hasStay = true;
          break;
        case "exit":
          if (cb.hasExit)
            QJS.JS_FreeValue(ctx, cb.exit);
          cb.exit = callback;
          cb.hasExit = true;
          break;
        default:
          QJS.JS_FreeValue(ctx, callback);
          break;
      }

      s_callbacks[entityId] = cb;

      // Return this for chaining
      QJS.JS_DupValue(ctx, thisVal);
      *outU = thisVal.u;
      *outTag = thisVal.tag;
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void Handle_Destroy(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      var thisVal = new JSValue { u = thisU, tag = thisTag };

      int entityId;
      fixed (byte* pEid = s_eid)
      {
        var eidVal = QJS.JS_GetPropertyStr(ctx, thisVal, pEid);
        QJS.JS_ToInt32(ctx, &entityId, eidVal);
        QJS.JS_FreeValue(ctx, eidVal);
      }

      // Mark handle as dead
      fixed (byte* pDead = s_dead)
        QJS.JS_SetPropertyStr(ctx, thisVal, pDead, QJS.NewBool(ctx, true));

      // Free callbacks
      if (s_callbacks.TryGetValue(entityId, out var cb))
      {
        FreeCallbacks(ctx, ref cb);
        s_callbacks.Remove(entityId);
      }

      // Queue for ECS component removal
      s_pendingRemove.Add(entityId);

      RetUndefined(outU, outTag);
    }

    #endregion

    #region Marshal (reuse from JsSpatialBridge)

    static readonly byte[] s_shape = Encoding.UTF8.GetBytes("shape\0");
    static readonly byte[] s_radiusSq = Encoding.UTF8.GetBytes("radiusSq\0");
    static readonly byte[] s_center = Encoding.UTF8.GetBytes("center\0");
    static readonly byte[] s_halfExtents = Encoding.UTF8.GetBytes("halfExtents\0");

    static unsafe SpatialShape MarshalShape(JSContext ctx, JSValue obj)
    {
      int shapeType;
      fixed (byte* pShape = s_shape)
      {
        var v = QJS.JS_GetPropertyStr(ctx, obj, pShape);
        QJS.JS_ToInt32(ctx, &shapeType, v);
        QJS.JS_FreeValue(ctx, v);
      }

      if (shapeType == (int)SpatialShapeType.Sphere)
      {
        var s = JsBridge.Marshal<SpatialSphere>(ctx, obj);
        return SpatialShape.Sphere(s.radiusSq, s.center);
      }

      var b = JsBridge.Marshal<SpatialBox>(ctx, obj);
      return SpatialShape.Box(b.halfExtents, b.center);
    }

    static unsafe int ReadTagHash(JSContext ctx, JSValue* argv, int index)
    {
      var ptr = QJS.JS_ToCString(ctx, argv[index]);
      if (ptr == null)
        return 0;
      var str = Marshal.PtrToStringUTF8((nint)ptr);
      QJS.JS_FreeCString(ctx, ptr);
      if (string.IsNullOrEmpty(str))
        return 0;
      SpatialTag tag = str;
      return tag;
    }

    #endregion

    #region Callback Management

    static void FreeCallbacks(JSContext ctx, ref TriggerCallbacks cb)
    {
      if (cb.hasEnter)
      {
        QJS.JS_FreeValue(ctx, cb.enter);
        cb.hasEnter = false;
      }
      if (cb.hasStay)
      {
        QJS.JS_FreeValue(ctx, cb.stay);
        cb.hasStay = false;
      }
      if (cb.hasExit)
      {
        QJS.JS_FreeValue(ctx, cb.exit);
        cb.hasExit = false;
      }
    }

    public static IReadOnlyCollection<int> PendingRemove => s_pendingRemove;

    public static void ClearPendingRemove() => s_pendingRemove.Clear();

    public static bool HasCallbacks(int entityId) => s_callbacks.ContainsKey(entityId);

    public static int CallbackCount => s_callbacks.Count;

    public static IEnumerable<int> RegisteredEntityIds => s_callbacks.Keys;

    public static bool TryGetCallbacks(
      int entityId,
      out bool hasEnter,
      out JSValue enter,
      out bool hasStay,
      out JSValue stay,
      out bool hasExit,
      out JSValue exit
    )
    {
      if (!s_callbacks.TryGetValue(entityId, out var cb))
      {
        hasEnter = hasStay = hasExit = false;
        enter = stay = exit = default;
        return false;
      }
      hasEnter = cb.hasEnter;
      enter = cb.enter;
      hasStay = cb.hasStay;
      stay = cb.stay;
      hasExit = cb.hasExit;
      exit = cb.exit;
      return true;
    }

    public static void RemoveCallbacks(JSContext ctx, int entityId)
    {
      if (s_callbacks.TryGetValue(entityId, out var cb))
      {
        FreeCallbacks(ctx, ref cb);
        s_callbacks.Remove(entityId);
      }
    }

    public static void DiscardAll()
    {
      s_callbacks.Clear();
      s_pendingRemove.Clear();
    }

    #endregion
  }

  [UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
  [UpdateAfter(typeof(SpatialTriggerSystem))]
  public partial class SpatialTriggerDispatchSystem : SystemBase
  {
    protected override void OnUpdate()
    {
      // Handle pending removes first
      var pendingRemove = JsSpatialTriggerBridge.PendingRemove;
      if (pendingRemove.Count > 0)
      {
        var ecb = new EntityCommandBuffer(Allocator.Temp);
        foreach (var eid in pendingRemove)
        {
          var entity = JsEntityRegistry.GetEntityFromId(eid);
          if (entity != Entity.Null && EntityManager.Exists(entity))
          {
            if (EntityManager.HasComponent<SpatialTrigger>(entity))
              ecb.RemoveComponent<SpatialTrigger>(entity);
            if (EntityManager.HasBuffer<StatefulSpatialOverlap>(entity))
              ecb.RemoveComponent<StatefulSpatialOverlap>(entity);
            if (EntityManager.HasBuffer<PreviousSpatialOverlap>(entity))
              ecb.RemoveComponent<PreviousSpatialOverlap>(entity);
          }
        }
        ecb.Playback(EntityManager);
        ecb.Dispose();
        JsSpatialTriggerBridge.ClearPendingRemove();
      }

      // Clean up orphaned callbacks (entity destroyed without .destroy())
      var instance = JsRuntimeManager.Instance;
      if (instance != null && instance.IsValid && JsSpatialTriggerBridge.CallbackCount > 0)
      {
        var ctx = instance.Context;
        List<int> orphans = null;
        foreach (var registeredEid in JsSpatialTriggerBridge.RegisteredEntityIds)
        {
          var entity = JsEntityRegistry.GetEntityFromId(registeredEid);
          if (entity == Entity.Null || !EntityManager.Exists(entity))
          {
            orphans ??= new List<int>();
            orphans.Add(registeredEid);
          }
        }
        if (orphans != null)
        {
          foreach (var eid in orphans)
            JsSpatialTriggerBridge.RemoveCallbacks(ctx, eid);
        }
      }

      // Dispatch callbacks
      var inst2 = JsRuntimeManager.Instance;
      if (inst2 == null || !inst2.IsValid)
        return;
      var ctx2 = inst2.Context;

      foreach (
        var (overlapBuf, entity) in SystemAPI
          .Query<DynamicBuffer<StatefulSpatialOverlap>>()
          .WithEntityAccess()
      )
      {
        if (overlapBuf.Length == 0)
          continue;

        var eid = JsEntityRegistry.GetIdFromEntity(entity);
        if (eid < 0)
          continue;

        if (
          !JsSpatialTriggerBridge.TryGetCallbacks(
            eid,
            out var hasEnter,
            out var enterCb,
            out var hasStay,
            out var stayCb,
            out var hasExit,
            out var exitCb
          )
        )
          continue;

        for (int i = 0; i < overlapBuf.Length; i++)
        {
          var overlap = overlapBuf[i];
          var otherEid = JsEntityRegistry.GetIdFromEntity(overlap.other);
          if (otherEid < 0)
          {
#if DEBUG
            UnityEngine.Debug.LogWarning(
              $"[SpatialTrigger] overlap.other entity {overlap.other} has no JsEntityId — callback skipped"
            );
#endif
            continue;
          }

          JSValue cb;
          bool hasCb;
          switch (overlap.state)
          {
            case SpatialEventState.Enter:
              cb = enterCb;
              hasCb = hasEnter;
              break;
            case SpatialEventState.Stay:
              cb = stayCb;
              hasCb = hasStay;
              break;
            case SpatialEventState.Exit:
              cb = exitCb;
              hasCb = hasExit;
              break;
            default:
              continue;
          }

          if (!hasCb)
            continue;

          InvokeCallback(ctx2, cb, otherEid);
        }
      }
    }

    static unsafe void InvokeCallback(JSContext ctx, JSValue cb, int otherEid)
    {
      var arg = QJS.NewInt32(ctx, otherEid);
      var result = QJS.JS_Call(ctx, cb, QJS.JS_UNDEFINED, 1, &arg);
      if (QJS.IsException(result))
      {
        var exc = QJS.JS_GetException(ctx);
        var eptr = QJS.JS_ToCString(ctx, exc);
        if (eptr != null)
        {
          var msg = Marshal.PtrToStringUTF8((nint)eptr) ?? "unknown error";
          UnityEngine.Debug.LogError($"[SpatialTrigger] JS callback error: {msg}");
          QJS.JS_FreeCString(ctx, eptr);
        }
        QJS.JS_FreeValue(ctx, exc);
      }
      QJS.JS_FreeValue(ctx, result);
    }
  }
}
