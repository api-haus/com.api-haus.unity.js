namespace UnityJS.Entities.Core
{
  using System.Collections.Generic;
  using AOT;
  using Components;
  using QJS;
  using Runtime;
  using Unity.Entities;
  using Unity.Logging;
  using static Runtime.QJSHelpers;

  /// <summary>
  /// Manages JS-defined component types.
  /// Structural presence tracked via ECS tag pool (JsDynTag0..63).
  /// Field data stored in JS objects (__js_comp[name][eid]).
  /// </summary>
  public static class JsComponentStore
  {
    const int MaxSlots = JsBridgeState.MaxSlots;
    static readonly byte[] s_jsCompKey = QJS.U8("__js_comp");

    static JsBridgeState B => JsRuntimeManager.Instance?.BridgeState as JsBridgeState;

    public static unsafe void Register(JSContext ctx)
    {
      var global = QJS.JS_GetGlobalObject(ctx);
      var pEcsBytes = QJS.U8("ecs");
      fixed (byte* pEcs = pEcsBytes)
      {
        var existing = QJS.JS_GetPropertyStr(ctx, global, pEcs);
        JSValue ns;
        if (QJS.IsUndefined(existing) || QJS.IsNull(existing))
        {
          QJS.JS_FreeValue(ctx, existing);
          ns = QJS.JS_NewObject(ctx);
        }
        else
        {
          ns = existing;
        }

        AddFunction(ctx, ns, "define", EcsDefine, 2);
        AddFunction(ctx, ns, "add", EcsAdd, 3);
        AddFunction(ctx, ns, "remove", EcsRemove, 2);
        AddFunction(ctx, ns, "has", EcsHas, 2);
        AddFunction(ctx, ns, "get", EcsGet, 2);

        QJS.JS_SetPropertyStr(ctx, global, pEcs, ns);
      }

      // Bootstrap JS-side data store: __js_comp = {}
      var pJsCompBytes = s_jsCompKey;
      fixed (byte* pJsComp = pJsCompBytes)
      {
        var existing = QJS.JS_GetPropertyStr(ctx, global, pJsComp);
        if (QJS.IsUndefined(existing) || QJS.IsNull(existing))
        {
          QJS.JS_FreeValue(ctx, existing);
          QJS.JS_SetPropertyStr(ctx, global, pJsComp, QJS.JS_NewObject(ctx));
        }
        else
        {
          QJS.JS_FreeValue(ctx, existing);
        }
      }

      QJS.JS_FreeValue(ctx, global);
    }

    public static void Shutdown()
    {
      // No-op — state is cleared by JsBridgeState.Dispose()
    }

    public static HashSet<string> GetEntityComponents(int entityId)
    {
      var b = B;
      if (b == null)
        return null;
      return b.EntityComponents.TryGetValue(entityId, out var set) ? set : null;
    }

    public static void CleanupEntity(int entityId)
    {
      var b = B;
      if (b == null)
        return;
      b.EntityComponents.Remove(entityId);
      b.EntitiesWithCleanup.Remove(entityId);
    }

    public static string GetSlotName(int slot)
    {
      var b = B;
      if (b == null)
        return null;
      return slot >= 0 && slot < MaxSlots ? b.SlotToName[slot] : null;
    }

    public static bool IsDefined(string name)
    {
      var b = B;
      return b != null && b.NameToSlot.ContainsKey(name);
    }

    public static int GetSlotForName(string name)
    {
      var b = B;
      if (b == null)
        return -1;
      return b.NameToSlot.TryGetValue(name, out var slot) ? slot : -1;
    }

    /// <summary>
    /// Scrubs JS-side data for an entity during cleanup.
    /// </summary>
    static readonly byte[] s_cleanupFuncKey = QJS.U8("__cleanupComponentEntity");

    public static unsafe void ScrubJsData(
      JSContext ctx,
      int entityId,
      HashSet<string> componentNames
    )
    {
      var global = QJS.JS_GetGlobalObject(ctx);

      // Call __cleanupComponentEntity(eid) for onDestroy + tick unregistration
      fixed (byte* pCleanup = s_cleanupFuncKey)
      {
        var cleanupFunc = QJS.JS_GetPropertyStr(ctx, global, pCleanup);
        if (QJS.JS_IsFunction(ctx, cleanupFunc) != 0)
        {
          var argv = stackalloc JSValue[1];
          argv[0] = QJS.NewInt32(ctx, entityId);
          var result = QJS.JS_Call(ctx, cleanupFunc, global, 1, argv);
          QJS.JS_FreeValue(ctx, result);
        }

        QJS.JS_FreeValue(ctx, cleanupFunc);
      }

      var pJsCompBytes = s_jsCompKey;
      fixed (byte* pJsComp = pJsCompBytes)
      {
        var jsComp = QJS.JS_GetPropertyStr(ctx, global, pJsComp);
        if (QJS.IsObject(jsComp))
          foreach (var name in componentNames)
          {
            var pNameBytes = QJS.U8(name);
            fixed (byte* pName = pNameBytes)
            {
              var store = QJS.JS_GetPropertyStr(ctx, jsComp, pName);
              if (QJS.IsObject(store))
                // Delete __js_comp[name][eid] by setting to undefined
                QJS.JS_SetPropertyUint32(ctx, store, (uint)entityId, QJS.JS_UNDEFINED);
              QJS.JS_FreeValue(ctx, store);
            }
          }

        QJS.JS_FreeValue(ctx, jsComp);
      }

      QJS.JS_FreeValue(ctx, global);
    }

    /// <summary>
    /// Allocates a tag slot for a new JS-defined component and creates its JS-side storage.
    /// Returns the slot index, or -1 if the pool is exhausted or the name conflicts with C#.
    /// </summary>
    static unsafe int AllocateSlot(JsBridgeState b, JSContext ctx, string name, string caller)
    {
      if (JsComponentRegistry.TryGetComponentType(name, out _))
      {
        Log.Error("[JsComponentStore] {0}: '{1}' conflicts with C# component", caller, name);
        return -1;
      }

      if (b.NextSlot >= MaxSlots)
      {
        Log.Error("[JsComponentStore] {0}: tag pool exhausted (max {1})", caller, MaxSlots);
        return -1;
      }

      var slot = b.NextSlot++;
      b.NameToSlot[name] = slot;
      b.SlotToName[slot] = name;

      JsComponentRegistry.Register(name, GetTagType(slot));

      // Create JS-side storage: __js_comp[name] = {}
      var global = QJS.JS_GetGlobalObject(ctx);
      var pJsCompBytes = s_jsCompKey;
      var pNameBytes = QJS.U8(name);
      fixed (byte* pJsComp = pJsCompBytes, pN = pNameBytes)
      {
        var jsComp = QJS.JS_GetPropertyStr(ctx, global, pJsComp);
        QJS.JS_SetPropertyStr(ctx, jsComp, pN, QJS.JS_NewObject(ctx));
        QJS.JS_FreeValue(ctx, jsComp);
      }
      QJS.JS_FreeValue(ctx, global);

      Log.Debug("[JsComponentStore] Defined '{0}' → slot {1}", name, slot);
      return slot;
    }

    #region Bridge Functions

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void EcsDefine(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      var b = B;
      if (b == null)
      {
        SetUndefined(outU, outTag);
        return;
      }

      var name = ArgString(ctx, argv, 0);

      if (string.IsNullOrEmpty(name))
      {
        SetUndefined(outU, outTag);
        return;
      }

      if (b.NameToSlot.ContainsKey(name))
      {
        // Tolerate re-definition for hot reload
        Log.Verbose(
          "[JsComponentStore] ecs.define: '{0}' already defined (re-define tolerated)",
          name
        );
        SetUndefined(outU, outTag);
        return;
      }

      if (AllocateSlot(b, ctx, name, "ecs.define") < 0)
      {
        SetUndefined(outU, outTag);
        return;
      }

      SetUndefined(outU, outTag);
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void EcsAdd(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      var b = B;
      if (b == null)
      {
        SetUndefined(outU, outTag);
        return;
      }

      int entityId;
      QJS.JS_ToInt32(ctx, &entityId, argv[0]);
      if (entityId <= 0)
      {
        Log.Error("[JsComponentStore] ecs.add: invalid entity id");
        SetUndefined(outU, outTag);
        return;
      }

      var name = ArgString(ctx, argv, 1);
      if (string.IsNullOrEmpty(name))
      {
        Log.Error("[JsComponentStore] ecs.add: component name required");
        SetUndefined(outU, outTag);
        return;
      }

      if (!b.NameToSlot.TryGetValue(name, out var slot))
      {
        // Auto-define on first use (supports Component classes that skip explicit define)
        slot = AllocateSlot(b, ctx, name, "ecs.add");
        if (slot < 0)
        {
          SetUndefined(outU, outTag);
          return;
        }
      }

      // Store data in __js_comp[name][eid]
      var global = QJS.JS_GetGlobalObject(ctx);
      var pJsCompBytes = s_jsCompKey;
      var pNameBytes = QJS.U8(name);
      fixed (
        byte* pJsComp = pJsCompBytes,
          pN = pNameBytes
      )
      {
        var jsComp = QJS.JS_GetPropertyStr(ctx, global, pJsComp);
        var store = QJS.JS_GetPropertyStr(ctx, jsComp, pN);

        JSValue data;
        if (argc >= 3 && QJS.IsObject(argv[2]))
          data = QJS.JS_DupValue(ctx, argv[2]);
        else
          data = QJS.JS_TRUE;

        QJS.JS_SetPropertyUint32(ctx, store, (uint)entityId, data);
        QJS.JS_FreeValue(ctx, store);
        QJS.JS_FreeValue(ctx, jsComp);
      }

      QJS.JS_FreeValue(ctx, global);

      // Track entity → component mapping
      if (!b.EntityComponents.TryGetValue(entityId, out var components))
      {
        components = new HashSet<string>();
        b.EntityComponents[entityId] = components;
      }

      components.Add(name);

      // Add ECS tag via ECB
      if (!JsECSBridge.TryGetBurstContextECB(out var ecb))
      {
        Log.Error("[JsComponentStore] ecs.add: no active ECB context");
        SetUndefined(outU, outTag);
        return;
      }

      var entity = JsECSBridge.GetEntityFromIdBurst(entityId);
      if (entity != Entity.Null)
      {
        ecb.AddComponent(entity, GetTagType(slot));
        if (b.EntitiesWithCleanup.Add(entityId))
          ecb.AddComponent(entity, new JsDataCleanup { entityId = entityId });
      }

      // Return the data or true
      if (argc >= 3 && QJS.IsObject(argv[2]))
      {
        SetResult(outU, outTag, QJS.JS_DupValue(ctx, argv[2]));
      }
      else
      {
        SetBool(outU, outTag, ctx, true);
      }
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void EcsRemove(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      var b = B;
      if (b == null)
      {
        SetUndefined(outU, outTag);
        return;
      }

      int entityId;
      QJS.JS_ToInt32(ctx, &entityId, argv[0]);
      if (entityId <= 0)
      {
        Log.Error("[JsComponentStore] ecs.remove: invalid entity id");
        SetUndefined(outU, outTag);
        return;
      }

      var name = ArgString(ctx, argv, 1);
      if (string.IsNullOrEmpty(name))
      {
        Log.Error("[JsComponentStore] ecs.remove: component name required");
        SetUndefined(outU, outTag);
        return;
      }

      if (!b.NameToSlot.TryGetValue(name, out var slot))
      {
        Log.Error("[JsComponentStore] ecs.remove: '{0}' not defined", name);
        SetUndefined(outU, outTag);
        return;
      }

      // Delete __js_comp[name][eid]
      var global = QJS.JS_GetGlobalObject(ctx);
      var pJsCompBytes = s_jsCompKey;
      var pNameBytes = QJS.U8(name);
      fixed (
        byte* pJsComp = pJsCompBytes,
          pN = pNameBytes
      )
      {
        var jsComp = QJS.JS_GetPropertyStr(ctx, global, pJsComp);
        var store = QJS.JS_GetPropertyStr(ctx, jsComp, pN);
        QJS.JS_SetPropertyUint32(ctx, store, (uint)entityId, QJS.JS_UNDEFINED);
        QJS.JS_FreeValue(ctx, store);
        QJS.JS_FreeValue(ctx, jsComp);
      }

      QJS.JS_FreeValue(ctx, global);

      // Update tracking
      if (b.EntityComponents.TryGetValue(entityId, out var components))
        components.Remove(name);

      // Remove ECS tag via ECB
      if (!JsECSBridge.TryGetBurstContextECB(out var ecb))
      {
        Log.Error("[JsComponentStore] ecs.remove: no active ECB context");
        SetUndefined(outU, outTag);
        return;
      }

      var entity = JsECSBridge.GetEntityFromIdBurst(entityId);
      if (entity == Entity.Null)
      {
        Log.Error("[JsComponentStore] ecs.remove: entity {0} not found", entityId);
        SetUndefined(outU, outTag);
        return;
      }

      ecb.RemoveComponent(entity, GetTagType(slot));
      SetUndefined(outU, outTag);
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void EcsHas(
      JSContext ctx,
      long thisU,
      long thisTag,
      int argc,
      JSValue* argv,
      long* outU,
      long* outTag
    )
    {
      var b = B;
      if (b == null)
      {
        SetBool(outU, outTag, ctx, false);
        return;
      }

      int entityId;
      QJS.JS_ToInt32(ctx, &entityId, argv[0]);
      if (entityId <= 0)
      {
        SetBool(outU, outTag, ctx, false);
        return;
      }

      var name = ArgString(ctx, argv, 1);
      if (string.IsNullOrEmpty(name))
      {
        SetBool(outU, outTag, ctx, false);
        return;
      }

      // Check JS-defined components via data store
      if (b.NameToSlot.ContainsKey(name))
      {
        var has =
          b.EntityComponents.TryGetValue(entityId, out var components) && components.Contains(name);
        SetBool(outU, outTag, ctx, has);
        return;
      }

      // Fall back to C#-defined components
      if (!JsComponentRegistry.TryGetComponentType(name, out _))
      {
        SetBool(outU, outTag, ctx, false);
        return;
      }

      var entity = JsECSBridge.GetEntityFromIdBurst(entityId);
      if (entity == Entity.Null)
      {
        SetBool(outU, outTag, ctx, false);
        return;
      }

      // If entity exists, assume component present (same as Lua impl)
      SetBool(outU, outTag, ctx, true);
    }

    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void EcsGet(
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
        SetUndefined(outU, outTag);
        return;
      }

      var name = ArgString(ctx, argv, 1);
      if (string.IsNullOrEmpty(name))
      {
        SetUndefined(outU, outTag);
        return;
      }

      // Fetch __js_comp[name][eid]
      var global = QJS.JS_GetGlobalObject(ctx);
      var pJsCompBytes = s_jsCompKey;
      var pNameBytes = QJS.U8(name);
      fixed (
        byte* pJsComp = pJsCompBytes,
          pN = pNameBytes
      )
      {
        var jsComp = QJS.JS_GetPropertyStr(ctx, global, pJsComp);
        if (!QJS.IsObject(jsComp))
        {
          QJS.JS_FreeValue(ctx, jsComp);
          QJS.JS_FreeValue(ctx, global);
          SetUndefined(outU, outTag);
          return;
        }

        var store = QJS.JS_GetPropertyStr(ctx, jsComp, pN);
        if (!QJS.IsObject(store))
        {
          QJS.JS_FreeValue(ctx, store);
          QJS.JS_FreeValue(ctx, jsComp);
          QJS.JS_FreeValue(ctx, global);
          SetUndefined(outU, outTag);
          return;
        }

        var data = QJS.JS_GetPropertyUint32(ctx, store, (uint)entityId);
        // GetPropertyUint32 already returns a new reference, so no dup needed
        SetResult(outU, outTag, data);

        QJS.JS_FreeValue(ctx, store);
        QJS.JS_FreeValue(ctx, jsComp);
      }

      QJS.JS_FreeValue(ctx, global);
    }

    #endregion

    #region Tag Pool Dispatch

    // @formatter:off
    static readonly ComponentType[] s_tagTypes =
    {
      ComponentType.ReadWrite<JsDynTag0>(),  ComponentType.ReadWrite<JsDynTag1>(),
      ComponentType.ReadWrite<JsDynTag2>(),  ComponentType.ReadWrite<JsDynTag3>(),
      ComponentType.ReadWrite<JsDynTag4>(),  ComponentType.ReadWrite<JsDynTag5>(),
      ComponentType.ReadWrite<JsDynTag6>(),  ComponentType.ReadWrite<JsDynTag7>(),
      ComponentType.ReadWrite<JsDynTag8>(),  ComponentType.ReadWrite<JsDynTag9>(),
      ComponentType.ReadWrite<JsDynTag10>(), ComponentType.ReadWrite<JsDynTag11>(),
      ComponentType.ReadWrite<JsDynTag12>(), ComponentType.ReadWrite<JsDynTag13>(),
      ComponentType.ReadWrite<JsDynTag14>(), ComponentType.ReadWrite<JsDynTag15>(),
      ComponentType.ReadWrite<JsDynTag16>(), ComponentType.ReadWrite<JsDynTag17>(),
      ComponentType.ReadWrite<JsDynTag18>(), ComponentType.ReadWrite<JsDynTag19>(),
      ComponentType.ReadWrite<JsDynTag20>(), ComponentType.ReadWrite<JsDynTag21>(),
      ComponentType.ReadWrite<JsDynTag22>(), ComponentType.ReadWrite<JsDynTag23>(),
      ComponentType.ReadWrite<JsDynTag24>(), ComponentType.ReadWrite<JsDynTag25>(),
      ComponentType.ReadWrite<JsDynTag26>(), ComponentType.ReadWrite<JsDynTag27>(),
      ComponentType.ReadWrite<JsDynTag28>(), ComponentType.ReadWrite<JsDynTag29>(),
      ComponentType.ReadWrite<JsDynTag30>(), ComponentType.ReadWrite<JsDynTag31>(),
      ComponentType.ReadWrite<JsDynTag32>(), ComponentType.ReadWrite<JsDynTag33>(),
      ComponentType.ReadWrite<JsDynTag34>(), ComponentType.ReadWrite<JsDynTag35>(),
      ComponentType.ReadWrite<JsDynTag36>(), ComponentType.ReadWrite<JsDynTag37>(),
      ComponentType.ReadWrite<JsDynTag38>(), ComponentType.ReadWrite<JsDynTag39>(),
      ComponentType.ReadWrite<JsDynTag40>(), ComponentType.ReadWrite<JsDynTag41>(),
      ComponentType.ReadWrite<JsDynTag42>(), ComponentType.ReadWrite<JsDynTag43>(),
      ComponentType.ReadWrite<JsDynTag44>(), ComponentType.ReadWrite<JsDynTag45>(),
      ComponentType.ReadWrite<JsDynTag46>(), ComponentType.ReadWrite<JsDynTag47>(),
      ComponentType.ReadWrite<JsDynTag48>(), ComponentType.ReadWrite<JsDynTag49>(),
      ComponentType.ReadWrite<JsDynTag50>(), ComponentType.ReadWrite<JsDynTag51>(),
      ComponentType.ReadWrite<JsDynTag52>(), ComponentType.ReadWrite<JsDynTag53>(),
      ComponentType.ReadWrite<JsDynTag54>(), ComponentType.ReadWrite<JsDynTag55>(),
      ComponentType.ReadWrite<JsDynTag56>(), ComponentType.ReadWrite<JsDynTag57>(),
      ComponentType.ReadWrite<JsDynTag58>(), ComponentType.ReadWrite<JsDynTag59>(),
      ComponentType.ReadWrite<JsDynTag60>(), ComponentType.ReadWrite<JsDynTag61>(),
      ComponentType.ReadWrite<JsDynTag62>(), ComponentType.ReadWrite<JsDynTag63>(),
    };
    // @formatter:on

    static ComponentType GetTagType(int slot)
    {
      if (slot < 0 || slot >= MaxSlots)
        throw new System.InvalidOperationException($"Tag pool slot {slot} out of range");
      return s_tagTypes[slot];
    }

    #endregion
  }
}
