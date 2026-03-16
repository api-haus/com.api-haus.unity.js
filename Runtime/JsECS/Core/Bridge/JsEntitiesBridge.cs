namespace UnityJS.Entities.Core
{
  using AOT;
  using Components;
  using QJS;
  using Runtime;
  using Unity.Collections;
  using Unity.Entities;
  using Unity.Mathematics;
  using Unity.Transforms;
  using static Runtime.QJSHelpers;

  /// <summary>
  /// Bridge functions for entity lifecycle operations.
  /// JS API: entities.create(), entities.destroy(), entities.addScript(), entities.hasScript(), entities.removeComponent()
  /// </summary>
  static partial class JsECSBridge
  {
    /// <summary>
    /// Helper: read a CString from argv[index] into a FixedString64Bytes, then free.
    /// </summary>
    static unsafe bool TryReadCStringAsFixed64(
      JSContext ctx,
      JSValue* argv,
      int index,
      out FixedString64Bytes result
    )
    {
      result = default;
      var str = ArgString(ctx, argv, index);
      if (string.IsNullOrEmpty(str))
        return false;
      if (str.Length > FixedString64Bytes.UTF8MaxLengthInBytes)
        return false;
      result = new FixedString64Bytes(str);
      return true;
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void Entities_Create(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      var position = float3.zero;

      if (argc >= 1)
      {
        if (QJS.IsObject(argv[0]))
        {
          position = JsStateExtensions.JsObjectToFloat3(ctx, argv[0]);
        }
        else if (argc >= 3)
        {
          double dx,
            dy,
            dz;
          QJS.JS_ToFloat64(ctx, &dx, argv[0]);
          QJS.JS_ToFloat64(ctx, &dy, argv[1]);
          QJS.JS_ToFloat64(ctx, &dz, argv[2]);
          position = new float3((float)dx, (float)dy, (float)dz);
        }
      }

      ref var bctx = ref s_burstContext.Data;
      if (!bctx.isValid)
      {
        SetNull(outU, outTag);
        return;
      }

      var entityId = AllocateEntityId();

      var entity = bctx.ecb.CreateEntity();
      bctx.ecb.AddComponent(entity, LocalTransform.FromPosition(position));
      bctx.ecb.AddComponent(entity, new JsEntityId { value = entityId });
      bctx.ecb.AddBuffer<JsScriptRequest>(entity);
      bctx.ecb.AddBuffer<JsEvent>(entity);

      AddPendingEntity(entityId, entity);

      SetInt(outU, outTag, ctx, entityId);
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void Entities_Destroy(
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
        SetBool(outU, outTag, ctx, false);
        return;
      }

      ref var bctx = ref s_burstContext.Data;
      if (!bctx.isValid)
      {
        SetBool(outU, outTag, ctx, false);
        return;
      }

      var entity = GetEntityFromIdBurst(entityId);
      if (entity == Entity.Null)
      {
        SetBool(outU, outTag, ctx, false);
        return;
      }

      bctx.ecb.RemoveComponent<JsEntityId>(entity);
      SetBool(outU, outTag, ctx, true);
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void Entities_AddScript(
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
        SetBool(outU, outTag, ctx, false);
        return;
      }

      if (!TryReadCStringAsFixed64(ctx, argv, 1, out var scriptName))
      {
        SetBool(outU, outTag, ctx, false);
        return;
      }

      ref var bctx = ref s_burstContext.Data;
      if (!bctx.isValid)
      {
        SetBool(outU, outTag, ctx, false);
        return;
      }

      var entity = GetEntityFromIdBurst(entityId);
      if (entity == Entity.Null)
      {
        SetBool(outU, outTag, ctx, false);
        return;
      }

      var request = new JsScriptRequest
      {
        scriptName = scriptName,
        requestHash = JsScriptPathUtility.HashScriptName(scriptName.ToString()),
        fulfilled = false,
      };
      bctx.ecb.AppendToBuffer(entity, request);
      SetBool(outU, outTag, ctx, true);
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void Entities_HasScript(
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
        SetBool(outU, outTag, ctx, false);
        return;
      }

      if (!TryReadCStringAsFixed64(ctx, argv, 1, out var scriptName))
      {
        SetBool(outU, outTag, ctx, false);
        return;
      }

      var entity = GetEntityFromIdBurst(entityId);
      SetBool(outU, outTag, ctx, HasScriptBurst(entity, scriptName));
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void Entities_RemoveComponent(
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
        SetBool(outU, outTag, ctx, false);
        return;
      }

      var componentName = ArgString(ctx, argv, 1);
      if (string.IsNullOrEmpty(componentName))
      {
        SetBool(outU, outTag, ctx, false);
        return;
      }

      if (!JsComponentRegistry.TryGetComponentType(componentName, out var componentType))
      {
        SetBool(outU, outTag, ctx, false);
        return;
      }

      ref var bctx = ref s_burstContext.Data;
      if (!bctx.isValid)
      {
        SetBool(outU, outTag, ctx, false);
        return;
      }

      var entity = GetEntityFromIdBurst(entityId);
      if (entity == Entity.Null)
      {
        SetBool(outU, outTag, ctx, false);
        return;
      }

      bctx.ecb.RemoveComponent(entity, componentType);
      SetBool(outU, outTag, ctx, true);
    }

    static unsafe void RegisterEntitiesFunctions(JSContext ctx)
    {
      var ns = QJS.JS_NewObject(ctx);

      AddFunction(ctx, ns, "create", Entities_Create, 0);
      AddFunction(ctx, ns, "destroy", Entities_Destroy, 1);
      AddFunction(ctx, ns, "addScript", Entities_AddScript, 2);
      AddFunction(ctx, ns, "hasScript", Entities_HasScript, 2);
      AddFunction(ctx, ns, "removeComponent", Entities_RemoveComponent, 2);

      var global = QJS.JS_GetGlobalObject(ctx);
      SetNamespace(ctx, global, "entities", ns);
      QJS.JS_FreeValue(ctx, global);
    }
  }
}
