namespace UnityJS.Entities.Core
{
  using System.Runtime.InteropServices;
  using System.Text;
  using System.Threading;
  using AOT;
  using Components;
  using Unity.Collections;
  using Unity.Entities;
  using Unity.Mathematics;
  using Unity.Transforms;
  using UnityJS.QJS;
  using UnityJS.Runtime;

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
      var ptr = QJS.JS_ToCString(ctx, argv[index]);
      if (ptr == null)
        return false;
      var str = Marshal.PtrToStringUTF8((nint)ptr);
      QJS.JS_FreeCString(ctx, ptr);
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
        var n = QJS.JS_NULL;
        *outU = n.u;
        *outTag = n.tag;
        return;
      }

      var entityId = AllocateEntityId();

      var entity = bctx.ecb.CreateEntity();
      bctx.ecb.AddComponent(entity, LocalTransform.FromPosition(position));
      bctx.ecb.AddComponent(entity, new JsEntityId { value = entityId });
      bctx.ecb.AddBuffer<JsScriptRequest>(entity);
      bctx.ecb.AddBuffer<JsEvent>(entity);

      AddPendingEntity(entityId, entity);

      var result = QJS.NewInt32(ctx, entityId);
      *outU = result.u;
      *outTag = result.tag;
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
        var f = QJS.NewBool(ctx, false);
        *outU = f.u;
        *outTag = f.tag;
        return;
      }

      ref var bctx = ref s_burstContext.Data;
      if (!bctx.isValid)
      {
        var f = QJS.NewBool(ctx, false);
        *outU = f.u;
        *outTag = f.tag;
        return;
      }

      var entity = GetEntityFromIdBurst(entityId);
      if (entity == Entity.Null)
      {
        var f = QJS.NewBool(ctx, false);
        *outU = f.u;
        *outTag = f.tag;
        return;
      }

      bctx.ecb.RemoveComponent<JsEntityId>(entity);

      var t = QJS.NewBool(ctx, true);
      *outU = t.u;
      *outTag = t.tag;
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
        var f = QJS.NewBool(ctx, false);
        *outU = f.u;
        *outTag = f.tag;
        return;
      }

      if (!TryReadCStringAsFixed64(ctx, argv, 1, out var scriptName))
      {
        var f = QJS.NewBool(ctx, false);
        *outU = f.u;
        *outTag = f.tag;
        return;
      }

      ref var bctx = ref s_burstContext.Data;
      if (!bctx.isValid)
      {
        var f = QJS.NewBool(ctx, false);
        *outU = f.u;
        *outTag = f.tag;
        return;
      }

      var entity = GetEntityFromIdBurst(entityId);
      if (entity == Entity.Null)
      {
        var f = QJS.NewBool(ctx, false);
        *outU = f.u;
        *outTag = f.tag;
        return;
      }

      var request = new JsScriptRequest
      {
        scriptName = scriptName,
        requestHash = JsScriptPathUtility.HashScriptName(scriptName.ToString()),
        fulfilled = false,
      };
      bctx.ecb.AppendToBuffer(entity, request);

      var t = QJS.NewBool(ctx, true);
      *outU = t.u;
      *outTag = t.tag;
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
        var f = QJS.NewBool(ctx, false);
        *outU = f.u;
        *outTag = f.tag;
        return;
      }

      if (!TryReadCStringAsFixed64(ctx, argv, 1, out var scriptName))
      {
        var f = QJS.NewBool(ctx, false);
        *outU = f.u;
        *outTag = f.tag;
        return;
      }

      var entity = GetEntityFromIdBurst(entityId);
      var hasScript = HasScriptBurst(entity, scriptName);

      var result = QJS.NewBool(ctx, hasScript);
      *outU = result.u;
      *outTag = result.tag;
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
        var f = QJS.NewBool(ctx, false);
        *outU = f.u;
        *outTag = f.tag;
        return;
      }

      var ptr = QJS.JS_ToCString(ctx, argv[1]);
      var componentName = Marshal.PtrToStringUTF8((nint)ptr);
      QJS.JS_FreeCString(ctx, ptr);
      if (string.IsNullOrEmpty(componentName))
      {
        var f = QJS.NewBool(ctx, false);
        *outU = f.u;
        *outTag = f.tag;
        return;
      }

      if (!JsComponentRegistry.TryGetComponentType(componentName, out var componentType))
      {
        var f = QJS.NewBool(ctx, false);
        *outU = f.u;
        *outTag = f.tag;
        return;
      }

      ref var bctx = ref s_burstContext.Data;
      if (!bctx.isValid)
      {
        var f = QJS.NewBool(ctx, false);
        *outU = f.u;
        *outTag = f.tag;
        return;
      }

      var entity = GetEntityFromIdBurst(entityId);
      if (entity == Entity.Null)
      {
        var f = QJS.NewBool(ctx, false);
        *outU = f.u;
        *outTag = f.tag;
        return;
      }

      bctx.ecb.RemoveComponent(entity, componentType);

      var t = QJS.NewBool(ctx, true);
      *outU = t.u;
      *outTag = t.tag;
    }

    static unsafe void RegisterEntitiesFunctions(JSContext ctx)
    {
      var ns = QJS.JS_NewObject(ctx);

      var pCreateBytes = Encoding.UTF8.GetBytes("create\0");
      fixed (byte* pCreate = pCreateBytes)
      {
        var fn = QJSShim.qjs_shim_new_function(ctx, Entities_Create, pCreate, 0);
        QJS.JS_SetPropertyStr(ctx, ns, pCreate, fn);
      }
      var pDestroyBytes = Encoding.UTF8.GetBytes("destroy\0");
      fixed (byte* pDestroy = pDestroyBytes)
      {
        var fn = QJSShim.qjs_shim_new_function(ctx, Entities_Destroy, pDestroy, 1);
        QJS.JS_SetPropertyStr(ctx, ns, pDestroy, fn);
      }
      var pAddScriptBytes = Encoding.UTF8.GetBytes("addScript\0");
      fixed (byte* pAddScript = pAddScriptBytes)
      {
        var fn = QJSShim.qjs_shim_new_function(ctx, Entities_AddScript, pAddScript, 2);
        QJS.JS_SetPropertyStr(ctx, ns, pAddScript, fn);
      }
      var pHasScriptBytes = Encoding.UTF8.GetBytes("hasScript\0");
      fixed (byte* pHasScript = pHasScriptBytes)
      {
        var fn = QJSShim.qjs_shim_new_function(ctx, Entities_HasScript, pHasScript, 2);
        QJS.JS_SetPropertyStr(ctx, ns, pHasScript, fn);
      }
      var pRemoveComponentBytes = Encoding.UTF8.GetBytes("removeComponent\0");
      fixed (byte* pRemoveComponent = pRemoveComponentBytes)
      {
        var fn = QJSShim.qjs_shim_new_function(ctx, Entities_RemoveComponent, pRemoveComponent, 2);
        QJS.JS_SetPropertyStr(ctx, ns, pRemoveComponent, fn);
      }

      var global = QJS.JS_GetGlobalObject(ctx);
      var pNameBytes = Encoding.UTF8.GetBytes("entities\0");
      fixed (byte* pName = pNameBytes)
        QJS.JS_SetPropertyStr(ctx, global, pName, ns);
      QJS.JS_FreeValue(ctx, global);
    }
  }
}
