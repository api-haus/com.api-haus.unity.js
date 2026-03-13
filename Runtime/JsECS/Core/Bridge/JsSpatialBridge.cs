namespace UnityJS.Entities.Core
{
  using System.Text;
  using AOT;
  using Components;
  using Unity.Collections;
  using Unity.Entities;
  using Unity.Mathematics;
  using Unity.Transforms;
  using UnityJS.QJS;
  using UnityJS.Runtime;

  /// <summary>
  /// Bridge functions for spatial queries.
  /// JS API: spatial.distance(), spatial.queryNear(), spatial.getEntityCount()
  /// </summary>
  static partial class JsECSBridge
  {
    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void Spatial_Distance(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      float3 posA,
        posB;

      if (QJS.IsNumber(argv[0]))
      {
        int idA;
        QJS.JS_ToInt32(ctx, &idA, argv[0]);
        var entityA = GetEntityFromIdBurst(idA);
        if (!TryGetTransformBurst(entityA, out var transformA))
        {
          var neg = QJS.NewFloat64(ctx, -1);
          *outU = neg.u;
          *outTag = neg.tag;
          return;
        }
        posA = transformA.Position;
      }
      else if (QJS.IsObject(argv[0]))
      {
        posA = JsStateExtensions.JsObjectToFloat3(ctx, argv[0]);
      }
      else
      {
        var neg = QJS.NewFloat64(ctx, -1);
        *outU = neg.u;
        *outTag = neg.tag;
        return;
      }

      if (QJS.IsNumber(argv[1]))
      {
        int idB;
        QJS.JS_ToInt32(ctx, &idB, argv[1]);
        var entityB = GetEntityFromIdBurst(idB);
        if (!TryGetTransformBurst(entityB, out var transformB))
        {
          var neg = QJS.NewFloat64(ctx, -1);
          *outU = neg.u;
          *outTag = neg.tag;
          return;
        }
        posB = transformB.Position;
      }
      else if (QJS.IsObject(argv[1]))
      {
        posB = JsStateExtensions.JsObjectToFloat3(ctx, argv[1]);
      }
      else
      {
        var neg = QJS.NewFloat64(ctx, -1);
        *outU = neg.u;
        *outTag = neg.tag;
        return;
      }

      var distance = math.distance(posA, posB);
      var result = QJS.NewFloat64(ctx, distance);
      *outU = result.u;
      *outTag = result.tag;
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void Spatial_QueryNear(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      if (!s_initialized)
      {
        var empty = QJS.JS_NewArray(ctx);
        *outU = empty.u;
        *outTag = empty.tag;
        return;
      }

      float3 center;
      if (QJS.IsObject(argv[0]))
      {
        center = JsStateExtensions.JsObjectToFloat3(ctx, argv[0]);
      }
      else if (QJS.IsNumber(argv[0]))
      {
        int entityIndex;
        QJS.JS_ToInt32(ctx, &entityIndex, argv[0]);
        var entity = GetEntityFromIdBurst(entityIndex);
        if (!TryGetTransformBurst(entity, out var transform))
        {
          var empty = QJS.JS_NewArray(ctx);
          *outU = empty.u;
          *outTag = empty.tag;
          return;
        }
        center = transform.Position;
      }
      else
      {
        var empty = QJS.JS_NewArray(ctx);
        *outU = empty.u;
        *outTag = empty.tag;
        return;
      }

      double dRadius;
      QJS.JS_ToFloat64(ctx, &dRadius, argv[1]);
      var radius = (float)dRadius;
      var radiusSq = radius * radius;

      var query = s_entityManager.CreateEntityQuery(
        ComponentType.ReadOnly<LocalTransform>(),
        ComponentType.ReadOnly<JsScript>()
      );

      var entities = query.ToEntityArray(Allocator.Temp);
      var transforms = query.ToComponentDataArray<LocalTransform>(Allocator.Temp);

      var arr = QJS.JS_NewArray(ctx);
      uint resultIndex = 0;

      for (var i = 0; i < entities.Length; i++)
      {
        var distSq = math.distancesq(center, transforms[i].Position);
        if (distSq <= radiusSq)
        {
          var entityId = JsEntityRegistry.GetIdFromEntity(entities[i]);
          if (entityId > 0)
          {
            QJS.JS_SetPropertyUint32(ctx, arr, resultIndex++, QJS.NewInt32(ctx, entityId));
          }
        }
      }

      entities.Dispose();
      transforms.Dispose();

      *outU = arr.u;
      *outTag = arr.tag;
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void Spatial_GetEntityCount(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      if (!s_initialized)
      {
        var zero = QJS.NewInt32(ctx, 0);
        *outU = zero.u;
        *outTag = zero.tag;
        return;
      }

      var query = s_entityManager.CreateEntityQuery(ComponentType.ReadOnly<JsScript>());
      var count = query.CalculateEntityCount();
      var result = QJS.NewInt32(ctx, count);
      *outU = result.u;
      *outTag = result.tag;
    }

    static unsafe void RegisterSpatialFunctions(JSContext ctx)
    {
      var ns = QJS.JS_NewObject(ctx);

      var pDistanceBytes = Encoding.UTF8.GetBytes("distance\0");
      fixed (byte* pDistance = pDistanceBytes)
      {
        var fn = QJSShim.qjs_shim_new_function(ctx, Spatial_Distance, pDistance, 2);
        QJS.JS_SetPropertyStr(ctx, ns, pDistance, fn);
      }
      var pQueryNearBytes = Encoding.UTF8.GetBytes("queryNear\0");
      fixed (byte* pQueryNear = pQueryNearBytes)
      {
        var fn = QJSShim.qjs_shim_new_function(ctx, Spatial_QueryNear, pQueryNear, 2);
        QJS.JS_SetPropertyStr(ctx, ns, pQueryNear, fn);
      }
      var pGetEntityCountBytes = Encoding.UTF8.GetBytes("getEntityCount\0");
      fixed (byte* pGetEntityCount = pGetEntityCountBytes)
      {
        var fn = QJSShim.qjs_shim_new_function(ctx, Spatial_GetEntityCount, pGetEntityCount, 0);
        QJS.JS_SetPropertyStr(ctx, ns, pGetEntityCount, fn);
      }

      var global = QJS.JS_GetGlobalObject(ctx);
      var pNameBytes = Encoding.UTF8.GetBytes("spatial\0");
      fixed (byte* pName = pNameBytes)
        QJS.JS_SetPropertyStr(ctx, global, pName, ns);
      QJS.JS_FreeValue(ctx, global);
    }
  }
}
