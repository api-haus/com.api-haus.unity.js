using MiniSpatial;
using UnityJS.Entities.Core;

[assembly: JsBridge(typeof(SpatialShapeType))]
[assembly: JsBridge(typeof(SpatialSphere))]
[assembly: JsBridge(typeof(SpatialBox))]

namespace UnityJS.Integration.Spatial
{
  using System.Runtime.InteropServices;
  using System.Text;
  using AOT;
  using Unity.Collections;
  using Unity.Entities;
  using Unity.Mathematics;
  using UnityEngine;
  using UnityJS.QJS;
  using UnityJS.Runtime;

  public static class JsSpatialBridge
  {
    static readonly byte[] s_shape = Encoding.UTF8.GetBytes("shape\0");
    static readonly byte[] s_tag = Encoding.UTF8.GetBytes("tag\0");
    static readonly byte[] s_radiusSq = Encoding.UTF8.GetBytes("radiusSq\0");
    static readonly byte[] s_center = Encoding.UTF8.GetBytes("center\0");
    static readonly byte[] s_halfExtents = Encoding.UTF8.GetBytes("halfExtents\0");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void AutoRegister() => JsFunctionRegistry.Register("spatial", RegisterSpatialFunctions);

    static unsafe void RegisterSpatialFunctions(JSContext ctx, JSValue ns)
    {
      AddFn(ctx, ns, "add", Spatial_Add, 3);
      AddFn(ctx, ns, "get", Spatial_Get, 1);
      AddFn(ctx, ns, "query", Spatial_Query, 2);
      AddFn(ctx, ns, "sphere", Spatial_HelperSphere, 2);
      AddFn(ctx, ns, "box", Spatial_HelperBox, 2);
    }

    #region JS Helpers

    static unsafe void AddFn(JSContext ctx, JSValue ns, string name, QJSShimCallback cb, int argc)
    {
      var bytes = Encoding.UTF8.GetBytes(name + '\0');
      fixed (byte* p = bytes)
      {
        var fn = QJSShim.qjs_shim_new_function(ctx, cb, p, argc);
        QJS.JS_SetPropertyStr(ctx, ns, p, fn);
      }
    }

    static unsafe void RetUndefined(long* outU, long* outTag)
    {
      var v = QJS.JS_UNDEFINED;
      *outU = v.u;
      *outTag = v.tag;
    }

    static unsafe void RetBool(long* outU, long* outTag, JSContext ctx, bool value)
    {
      var v = QJS.NewBool(ctx, value);
      *outU = v.u;
      *outTag = v.tag;
    }

    #endregion

    #region Shape Helpers

    // spatial.sphere(radius, center?)
    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void Spatial_HelperSphere(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      double radius;
      QJS.JS_ToFloat64(ctx, &radius, argv[0]);
      var radiusSq = radius * radius;

      var center =
        argc >= 2 && QJS.IsObject(argv[1])
          ? JsStateExtensions.JsObjectToFloat3(ctx, argv[1])
          : float3.zero;

      var obj = QJS.JS_NewObject(ctx);
      fixed (
        byte* pShape = s_shape,
          pRadiusSq = s_radiusSq,
          pCenter = s_center
      )
      {
        QJS.JS_SetPropertyStr(ctx, obj, pShape, QJS.NewInt32(ctx, (int)SpatialShapeType.Sphere));
        QJS.JS_SetPropertyStr(ctx, obj, pRadiusSq, QJS.NewFloat64(ctx, radiusSq));
        QJS.JS_SetPropertyStr(ctx, obj, pCenter, JsStateExtensions.Float3ToJsObject(ctx, center));
      }

      *outU = obj.u;
      *outTag = obj.tag;
    }

    // spatial.box(halfExtents, center?)
    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void Spatial_HelperBox(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      var halfExtents = JsStateExtensions.JsObjectToFloat3(ctx, argv[0]);
      var center =
        argc >= 2 && QJS.IsObject(argv[1])
          ? JsStateExtensions.JsObjectToFloat3(ctx, argv[1])
          : float3.zero;

      var obj = QJS.JS_NewObject(ctx);
      fixed (
        byte* pShape = s_shape,
          pHalfExtents = s_halfExtents,
          pCenter = s_center
      )
      {
        QJS.JS_SetPropertyStr(ctx, obj, pShape, QJS.NewInt32(ctx, (int)SpatialShapeType.Box));
        QJS.JS_SetPropertyStr(
          ctx,
          obj,
          pHalfExtents,
          JsStateExtensions.Float3ToJsObject(ctx, halfExtents)
        );
        QJS.JS_SetPropertyStr(ctx, obj, pCenter, JsStateExtensions.Float3ToJsObject(ctx, center));
      }

      *outU = obj.u;
      *outTag = obj.tag;
    }

    #endregion

    #region Marshal

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

    #region Bridge Functions

    // spatial.add(eid, tag, shapeObj)
    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void Spatial_Add(
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
        RetBool(outU, outTag, ctx, false);
        return;
      }

      var tagHash = ReadTagHash(ctx, argv, 1);
      if (tagHash == 0)
      {
        RetBool(outU, outTag, ctx, false);
        return;
      }

      var shape = MarshalShape(ctx, argv[2]);

      if (!JsECSBridge.TryGetBurstContextECB(out var ecb))
      {
        RetBool(outU, outTag, ctx, false);
        return;
      }

      var entity = JsECSBridge.GetEntityFromIdBurst(entityId);
      if (entity == Entity.Null)
      {
        RetBool(outU, outTag, ctx, false);
        return;
      }

      ecb.AddComponent(entity, new SpatialAgent { shape = shape, tag = tagHash });
      RetBool(outU, outTag, ctx, true);
    }

    // spatial.get(eid)
    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void Spatial_Get(
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

      var entity = JsECSBridge.GetEntityFromIdBurst(entityId);
      if (entity == Entity.Null)
      {
        RetUndefined(outU, outTag);
        return;
      }

      var world = World.DefaultGameObjectInjectionWorld;
      if (world == null || !world.IsCreated)
      {
        RetUndefined(outU, outTag);
        return;
      }

      var em = world.EntityManager;
      if (!em.HasComponent<SpatialAgent>(entity))
      {
        RetUndefined(outU, outTag);
        return;
      }

      var agent = em.GetComponentData<SpatialAgent>(entity);

      var obj = QJS.JS_NewObject(ctx);
      fixed (
        byte* pTag = s_tag,
          pShape = s_shape
      )
      {
        QJS.JS_SetPropertyStr(ctx, obj, pTag, QJS.NewInt32(ctx, agent.tag));
        QJS.JS_SetPropertyStr(ctx, obj, pShape, QJS.NewInt32(ctx, (int)agent.shape.type));
      }

      if (agent.shape.type == SpatialShapeType.Sphere)
      {
        fixed (
          byte* pRadiusSq = s_radiusSq,
            pCenter = s_center
        )
        {
          QJS.JS_SetPropertyStr(
            ctx,
            obj,
            pRadiusSq,
            QJS.NewFloat64(ctx, agent.shape.sphere.radiusSq)
          );
          QJS.JS_SetPropertyStr(
            ctx,
            obj,
            pCenter,
            JsStateExtensions.Float3ToJsObject(ctx, agent.shape.sphere.center)
          );
        }
      }
      else
      {
        fixed (
          byte* pHalfExtents = s_halfExtents,
            pCenter = s_center
        )
        {
          QJS.JS_SetPropertyStr(
            ctx,
            obj,
            pHalfExtents,
            JsStateExtensions.Float3ToJsObject(ctx, agent.shape.box.halfExtents)
          );
          QJS.JS_SetPropertyStr(
            ctx,
            obj,
            pCenter,
            JsStateExtensions.Float3ToJsObject(ctx, agent.shape.box.center)
          );
        }
      }

      *outU = obj.u;
      *outTag = obj.tag;
    }

    // spatial.query(tag, shapeObj)
    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void Spatial_Query(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      var tagHash = ReadTagHash(ctx, argv, 0);
      if (tagHash == 0)
      {
        var empty = QJS.JS_NewArray(ctx);
        *outU = empty.u;
        *outTag = empty.tag;
        return;
      }

      var shape = MarshalShape(ctx, argv[1]);

      var results = new NativeList<Entity>(16, Allocator.Temp);
      var query = new ShapeQuery { shape = shape, results = results };
      SpatialQuery.Range(tagHash, ref query);

      var arr = QJS.JS_NewArray(ctx);
      for (var i = 0; i < results.Length; i++)
      {
        var eid = JsEntityRegistry.GetIdFromEntity(results[i]);
        if (eid >= 0)
          QJS.JS_SetPropertyUint32(ctx, arr, (uint)i, QJS.NewInt32(ctx, eid));
      }

      results.Dispose();

      *outU = arr.u;
      *outTag = arr.tag;
    }

    #endregion
  }
}
