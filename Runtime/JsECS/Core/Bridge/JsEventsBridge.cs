namespace UnityJS.Entities.Core
{
  using System.Text;
  using AOT;
  using Components;
  using Unity.Collections;
  using Unity.Entities;
  using UnityJS.QJS;

  /// <summary>
  /// Bridge functions for cross-entity event operations.
  /// JS API: events.sendAttack()
  /// </summary>
  static partial class JsECSBridge
  {
    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void Events_SendAttack(
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
        SetUndefined(outU, outTag);
        return;
      }

      int sourceId,
        targetId,
        damage;
      QJS.JS_ToInt32(ctx, &sourceId, argv[0]);
      QJS.JS_ToInt32(ctx, &targetId, argv[1]);
      QJS.JS_ToInt32(ctx, &damage, argv[2]);

      var source = GetEntityFromIdBurst(sourceId);
      var target = GetEntityFromIdBurst(targetId);

      if (target == Entity.Null)
      {
        SetUndefined(outU, outTag);
        return;
      }

      ref var bctx = ref s_burstContext.Data;
      if (!bctx.isValid)
      {
        SetUndefined(outU, outTag);
        return;
      }

      FixedString32Bytes eventName = "on_attacked";
      var evt = new JsEvent
      {
        eventName = eventName,
        source = source,
        target = target,
        intParam = damage,
      };
      bctx.ecb.AppendToBuffer(target, evt);

      SetUndefined(outU, outTag);
    }

    static unsafe void RegisterEventsFunctions(JSContext ctx)
    {
      var ns = QJS.JS_NewObject(ctx);

      var pSendAttackBytes = Encoding.UTF8.GetBytes("sendAttack\0");
      fixed (byte* pSendAttack = pSendAttackBytes)
      {
        var fn = QJSShim.qjs_shim_new_function(ctx, Events_SendAttack, pSendAttack, 3);
        QJS.JS_SetPropertyStr(ctx, ns, pSendAttack, fn);
      }

      var global = QJS.JS_GetGlobalObject(ctx);
      var pNameBytes = Encoding.UTF8.GetBytes("events\0");
      fixed (byte* pName = pNameBytes)
        QJS.JS_SetPropertyStr(ctx, global, pName, ns);
      QJS.JS_FreeValue(ctx, global);
    }
  }
}
