namespace UnityJS.Entities.Core
{
  using System.Collections.Generic;
  using System.Runtime.InteropServices;
  using System.Text;
  using AOT;
  using Components;
  using Unity.Entities;
  using Unity.Logging;
  using UnityJS.QJS;

  /// <summary>
  /// Manages JS-defined component types.
  /// Structural presence tracked via ECS tag pool (JsDynTag0..63).
  /// Field data stored in JS objects (__js_comp[name][eid]).
  /// </summary>
  public static class JsComponentStore
  {
    const int MaxSlots = 64;
    static readonly byte[] s_jsCompKey = Encoding.UTF8.GetBytes("__js_comp\0");

    static readonly Dictionary<string, int> s_nameToSlot = new();
    static readonly string[] s_slotToName = new string[MaxSlots];
    static readonly Dictionary<string, Dictionary<string, string>> s_schemas = new();

    // Tracks which JS components each entity has (for cleanup)
    static readonly Dictionary<int, HashSet<string>> s_entityComponents = new();

    // Tracks which entities already have JsDataCleanup (avoid duplicate AddComponent)
    static readonly HashSet<int> s_entitiesWithCleanup = new();

    static int s_nextSlot;

    public static unsafe void Register(JSContext ctx)
    {
      var global = QJS.JS_GetGlobalObject(ctx);
      var pEcsBytes = Encoding.UTF8.GetBytes("ecs\0");
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

        var pDefineBytes = Encoding.UTF8.GetBytes("define\0");
        fixed (byte* pDefine = pDefineBytes)
        {
          var fn = QJSShim.qjs_shim_new_function(ctx, EcsDefine, pDefine, 2);
          QJS.JS_SetPropertyStr(ctx, ns, pDefine, fn);
        }
        var pAddBytes = Encoding.UTF8.GetBytes("add\0");
        fixed (byte* pAdd = pAddBytes)
        {
          var fn = QJSShim.qjs_shim_new_function(ctx, EcsAdd, pAdd, 3);
          QJS.JS_SetPropertyStr(ctx, ns, pAdd, fn);
        }
        var pRemoveBytes = Encoding.UTF8.GetBytes("remove\0");
        fixed (byte* pRemove = pRemoveBytes)
        {
          var fn = QJSShim.qjs_shim_new_function(ctx, EcsRemove, pRemove, 2);
          QJS.JS_SetPropertyStr(ctx, ns, pRemove, fn);
        }
        var pHasBytes = Encoding.UTF8.GetBytes("has\0");
        fixed (byte* pHas = pHasBytes)
        {
          var fn = QJSShim.qjs_shim_new_function(ctx, EcsHas, pHas, 2);
          QJS.JS_SetPropertyStr(ctx, ns, pHas, fn);
        }
        var pGetBytes = Encoding.UTF8.GetBytes("get\0");
        fixed (byte* pGet = pGetBytes)
        {
          var fn = QJSShim.qjs_shim_new_function(ctx, EcsGet, pGet, 2);
          QJS.JS_SetPropertyStr(ctx, ns, pGet, fn);
        }

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
      s_nameToSlot.Clear();
      s_schemas.Clear();
      s_entityComponents.Clear();
      s_entitiesWithCleanup.Clear();
      s_nextSlot = 0;

      for (var i = 0; i < MaxSlots; i++)
        s_slotToName[i] = null;
    }

    public static HashSet<string> GetEntityComponents(int entityId)
    {
      return s_entityComponents.TryGetValue(entityId, out var set) ? set : null;
    }

    public static void CleanupEntity(int entityId)
    {
      s_entityComponents.Remove(entityId);
      s_entitiesWithCleanup.Remove(entityId);
    }

    public static string GetSlotName(int slot)
    {
      return slot >= 0 && slot < MaxSlots ? s_slotToName[slot] : null;
    }

    public static bool IsDefined(string name)
    {
      return s_nameToSlot.ContainsKey(name);
    }

    /// <summary>
    /// Scrubs JS-side data for an entity during cleanup.
    /// </summary>
    public static unsafe void ScrubJsData(
      JSContext ctx,
      int entityId,
      HashSet<string> componentNames
    )
    {
      var global = QJS.JS_GetGlobalObject(ctx);
      var pJsCompBytes = s_jsCompKey;
      fixed (byte* pJsComp = pJsCompBytes)
      {
        var jsComp = QJS.JS_GetPropertyStr(ctx, global, pJsComp);
        if (QJS.IsObject(jsComp))
        {
          foreach (var name in componentNames)
          {
            var pNameBytes = Encoding.UTF8.GetBytes(name + "\0");
            fixed (byte* pName = pNameBytes)
            {
              var store = QJS.JS_GetPropertyStr(ctx, jsComp, pName);
              if (QJS.IsObject(store))
              {
                // Delete __js_comp[name][eid] by setting to undefined
                QJS.JS_SetPropertyUint32(ctx, store, (uint)entityId, QJS.JS_UNDEFINED);
              }
              QJS.JS_FreeValue(ctx, store);
            }
          }
        }
        QJS.JS_FreeValue(ctx, jsComp);
      }
      QJS.JS_FreeValue(ctx, global);
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
      var ptr = QJS.JS_ToCString(ctx, argv[0]);
      var name = Marshal.PtrToStringUTF8((nint)ptr);
      QJS.JS_FreeCString(ctx, ptr);

      if (string.IsNullOrEmpty(name))
      {
        JsECSBridge.SetUndefined(outU, outTag);
        return;
      }

      // Check for collision with C#-defined components
      if (JsComponentRegistry.TryGetComponentType(name, out _))
      {
        Log.Error("[JsComponentStore] ecs.define: '{0}' already exists as a C# component", name);
        JsECSBridge.SetUndefined(outU, outTag);
        return;
      }

      if (s_nameToSlot.ContainsKey(name))
      {
        Log.Error("[JsComponentStore] ecs.define: '{0}' already defined", name);
        JsECSBridge.SetUndefined(outU, outTag);
        return;
      }

      if (s_nextSlot >= MaxSlots)
      {
        Log.Error("[JsComponentStore] ecs.define: tag pool exhausted (max {0})", MaxSlots);
        JsECSBridge.SetUndefined(outU, outTag);
        return;
      }

      // Assign tag slot
      var slot = s_nextSlot++;
      s_nameToSlot[name] = slot;
      s_slotToName[slot] = name;

      // Register in JsComponentRegistry so ecs.query() resolves this name
      var tagType = GetTagType(slot);
      JsComponentRegistry.Register(name, tagType);

      // Create JS-side storage: __js_comp[name] = {}
      var global = QJS.JS_GetGlobalObject(ctx);
      var pJsCompBytes = s_jsCompKey;
      var pNameBytes = Encoding.UTF8.GetBytes(name + "\0");
      fixed (
        byte* pJsComp = pJsCompBytes,
          pN = pNameBytes
      )
      {
        var jsComp = QJS.JS_GetPropertyStr(ctx, global, pJsComp);
        QJS.JS_SetPropertyStr(ctx, jsComp, pN, QJS.JS_NewObject(ctx));
        QJS.JS_FreeValue(ctx, jsComp);
      }
      QJS.JS_FreeValue(ctx, global);

      Log.Info("[JsComponentStore] Defined '{0}' → slot {1}", name, slot);
      JsECSBridge.SetUndefined(outU, outTag);
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
      int entityId;
      QJS.JS_ToInt32(ctx, &entityId, argv[0]);
      if (entityId <= 0)
      {
        Log.Error("[JsComponentStore] ecs.add: invalid entity id");
        JsECSBridge.SetUndefined(outU, outTag);
        return;
      }

      var ptr = QJS.JS_ToCString(ctx, argv[1]);
      var name = Marshal.PtrToStringUTF8((nint)ptr);
      QJS.JS_FreeCString(ctx, ptr);
      if (string.IsNullOrEmpty(name))
      {
        Log.Error("[JsComponentStore] ecs.add: component name required");
        JsECSBridge.SetUndefined(outU, outTag);
        return;
      }

      if (!s_nameToSlot.TryGetValue(name, out var slot))
      {
        Log.Error("[JsComponentStore] ecs.add: '{0}' not defined (call ecs.define first)", name);
        JsECSBridge.SetUndefined(outU, outTag);
        return;
      }

      // Store data in __js_comp[name][eid]
      var global = QJS.JS_GetGlobalObject(ctx);
      var pJsCompBytes = s_jsCompKey;
      var pNameBytes = Encoding.UTF8.GetBytes(name + "\0");
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
      if (!s_entityComponents.TryGetValue(entityId, out var components))
      {
        components = new HashSet<string>();
        s_entityComponents[entityId] = components;
      }
      components.Add(name);

      // Add ECS tag via ECB
      if (!JsECSBridge.TryGetBurstContextECB(out var ecb))
      {
        Log.Error("[JsComponentStore] ecs.add: no active ECB context");
        JsECSBridge.SetUndefined(outU, outTag);
        return;
      }

      var entity = JsECSBridge.GetEntityFromIdBurst(entityId);
      if (entity == Entity.Null)
      {
        Log.Error("[JsComponentStore] ecs.add: entity {0} not found", entityId);
        JsECSBridge.SetUndefined(outU, outTag);
        return;
      }

      ecb.AddComponent(entity, GetTagType(slot));

      // Add JsDataCleanup if not already present
      if (s_entitiesWithCleanup.Add(entityId))
        ecb.AddComponent(entity, new JsDataCleanup { entityId = entityId });

      // Return the data or true
      if (argc >= 3 && QJS.IsObject(argv[2]))
      {
        var dup = QJS.JS_DupValue(ctx, argv[2]);
        *outU = dup.u;
        *outTag = dup.tag;
      }
      else
      {
        JsECSBridge.SetBool(outU, outTag, ctx, true);
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
      int entityId;
      QJS.JS_ToInt32(ctx, &entityId, argv[0]);
      if (entityId <= 0)
      {
        Log.Error("[JsComponentStore] ecs.remove: invalid entity id");
        JsECSBridge.SetUndefined(outU, outTag);
        return;
      }

      var namePtr = QJS.JS_ToCString(ctx, argv[1]);
      var name = Marshal.PtrToStringUTF8((nint)namePtr);
      QJS.JS_FreeCString(ctx, namePtr);
      if (string.IsNullOrEmpty(name))
      {
        Log.Error("[JsComponentStore] ecs.remove: component name required");
        JsECSBridge.SetUndefined(outU, outTag);
        return;
      }

      if (!s_nameToSlot.TryGetValue(name, out var slot))
      {
        Log.Error("[JsComponentStore] ecs.remove: '{0}' not defined", name);
        JsECSBridge.SetUndefined(outU, outTag);
        return;
      }

      // Delete __js_comp[name][eid]
      var global = QJS.JS_GetGlobalObject(ctx);
      var pJsCompBytes = s_jsCompKey;
      var pNameBytes = Encoding.UTF8.GetBytes(name + "\0");
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
      if (s_entityComponents.TryGetValue(entityId, out var components))
        components.Remove(name);

      // Remove ECS tag via ECB
      if (!JsECSBridge.TryGetBurstContextECB(out var ecb))
      {
        Log.Error("[JsComponentStore] ecs.remove: no active ECB context");
        JsECSBridge.SetUndefined(outU, outTag);
        return;
      }

      var entity = JsECSBridge.GetEntityFromIdBurst(entityId);
      if (entity == Entity.Null)
      {
        Log.Error("[JsComponentStore] ecs.remove: entity {0} not found", entityId);
        JsECSBridge.SetUndefined(outU, outTag);
        return;
      }

      ecb.RemoveComponent(entity, GetTagType(slot));
      JsECSBridge.SetUndefined(outU, outTag);
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
      int entityId;
      QJS.JS_ToInt32(ctx, &entityId, argv[0]);
      if (entityId <= 0) { JsECSBridge.SetBool(outU, outTag, ctx, false); return; }

      var namePtr = QJS.JS_ToCString(ctx, argv[1]);
      var name = Marshal.PtrToStringUTF8((nint)namePtr);
      QJS.JS_FreeCString(ctx, namePtr);
      if (string.IsNullOrEmpty(name)) { JsECSBridge.SetBool(outU, outTag, ctx, false); return; }

      // Check JS-defined components via data store
      if (s_nameToSlot.ContainsKey(name))
      {
        var has = s_entityComponents.TryGetValue(entityId, out var components) && components.Contains(name);
        JsECSBridge.SetBool(outU, outTag, ctx, has);
        return;
      }

      // Fall back to C#-defined components
      if (!JsComponentRegistry.TryGetComponentType(name, out _)) { JsECSBridge.SetBool(outU, outTag, ctx, false); return; }

      var entity = JsECSBridge.GetEntityFromIdBurst(entityId);
      if (entity == Entity.Null) { JsECSBridge.SetBool(outU, outTag, ctx, false); return; }

      // If entity exists, assume component present (same as Lua impl)
      JsECSBridge.SetBool(outU, outTag, ctx, true);
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
        JsECSBridge.SetUndefined(outU, outTag);
        return;
      }

      var namePtr = QJS.JS_ToCString(ctx, argv[1]);
      var name = Marshal.PtrToStringUTF8((nint)namePtr);
      QJS.JS_FreeCString(ctx, namePtr);
      if (string.IsNullOrEmpty(name))
      {
        JsECSBridge.SetUndefined(outU, outTag);
        return;
      }

      // Fetch __js_comp[name][eid]
      var global = QJS.JS_GetGlobalObject(ctx);
      var pJsCompBytes = s_jsCompKey;
      var pNameBytes = Encoding.UTF8.GetBytes(name + "\0");
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
          JsECSBridge.SetUndefined(outU, outTag);
          return;
        }

        var store = QJS.JS_GetPropertyStr(ctx, jsComp, pN);
        if (!QJS.IsObject(store))
        {
          QJS.JS_FreeValue(ctx, store);
          QJS.JS_FreeValue(ctx, jsComp);
          QJS.JS_FreeValue(ctx, global);
          JsECSBridge.SetUndefined(outU, outTag);
          return;
        }

        var data = QJS.JS_GetPropertyUint32(ctx, store, (uint)entityId);
        // DupValue before returning (caller owns the return)
        // GetPropertyUint32 already returns a new reference, so no dup needed
        *outU = data.u;
        *outTag = data.tag;

        QJS.JS_FreeValue(ctx, store);
        QJS.JS_FreeValue(ctx, jsComp);
      }
      QJS.JS_FreeValue(ctx, global);
    }

    #endregion

    #region Tag Pool Dispatch

    // @formatter:off
    static ComponentType GetTagType(int slot) =>
      slot switch
      {
        0 => ComponentType.ReadWrite<JsDynTag0>(),
        1 => ComponentType.ReadWrite<JsDynTag1>(),
        2 => ComponentType.ReadWrite<JsDynTag2>(),
        3 => ComponentType.ReadWrite<JsDynTag3>(),
        4 => ComponentType.ReadWrite<JsDynTag4>(),
        5 => ComponentType.ReadWrite<JsDynTag5>(),
        6 => ComponentType.ReadWrite<JsDynTag6>(),
        7 => ComponentType.ReadWrite<JsDynTag7>(),
        8 => ComponentType.ReadWrite<JsDynTag8>(),
        9 => ComponentType.ReadWrite<JsDynTag9>(),
        10 => ComponentType.ReadWrite<JsDynTag10>(),
        11 => ComponentType.ReadWrite<JsDynTag11>(),
        12 => ComponentType.ReadWrite<JsDynTag12>(),
        13 => ComponentType.ReadWrite<JsDynTag13>(),
        14 => ComponentType.ReadWrite<JsDynTag14>(),
        15 => ComponentType.ReadWrite<JsDynTag15>(),
        16 => ComponentType.ReadWrite<JsDynTag16>(),
        17 => ComponentType.ReadWrite<JsDynTag17>(),
        18 => ComponentType.ReadWrite<JsDynTag18>(),
        19 => ComponentType.ReadWrite<JsDynTag19>(),
        20 => ComponentType.ReadWrite<JsDynTag20>(),
        21 => ComponentType.ReadWrite<JsDynTag21>(),
        22 => ComponentType.ReadWrite<JsDynTag22>(),
        23 => ComponentType.ReadWrite<JsDynTag23>(),
        24 => ComponentType.ReadWrite<JsDynTag24>(),
        25 => ComponentType.ReadWrite<JsDynTag25>(),
        26 => ComponentType.ReadWrite<JsDynTag26>(),
        27 => ComponentType.ReadWrite<JsDynTag27>(),
        28 => ComponentType.ReadWrite<JsDynTag28>(),
        29 => ComponentType.ReadWrite<JsDynTag29>(),
        30 => ComponentType.ReadWrite<JsDynTag30>(),
        31 => ComponentType.ReadWrite<JsDynTag31>(),
        32 => ComponentType.ReadWrite<JsDynTag32>(),
        33 => ComponentType.ReadWrite<JsDynTag33>(),
        34 => ComponentType.ReadWrite<JsDynTag34>(),
        35 => ComponentType.ReadWrite<JsDynTag35>(),
        36 => ComponentType.ReadWrite<JsDynTag36>(),
        37 => ComponentType.ReadWrite<JsDynTag37>(),
        38 => ComponentType.ReadWrite<JsDynTag38>(),
        39 => ComponentType.ReadWrite<JsDynTag39>(),
        40 => ComponentType.ReadWrite<JsDynTag40>(),
        41 => ComponentType.ReadWrite<JsDynTag41>(),
        42 => ComponentType.ReadWrite<JsDynTag42>(),
        43 => ComponentType.ReadWrite<JsDynTag43>(),
        44 => ComponentType.ReadWrite<JsDynTag44>(),
        45 => ComponentType.ReadWrite<JsDynTag45>(),
        46 => ComponentType.ReadWrite<JsDynTag46>(),
        47 => ComponentType.ReadWrite<JsDynTag47>(),
        48 => ComponentType.ReadWrite<JsDynTag48>(),
        49 => ComponentType.ReadWrite<JsDynTag49>(),
        50 => ComponentType.ReadWrite<JsDynTag50>(),
        51 => ComponentType.ReadWrite<JsDynTag51>(),
        52 => ComponentType.ReadWrite<JsDynTag52>(),
        53 => ComponentType.ReadWrite<JsDynTag53>(),
        54 => ComponentType.ReadWrite<JsDynTag54>(),
        55 => ComponentType.ReadWrite<JsDynTag55>(),
        56 => ComponentType.ReadWrite<JsDynTag56>(),
        57 => ComponentType.ReadWrite<JsDynTag57>(),
        58 => ComponentType.ReadWrite<JsDynTag58>(),
        59 => ComponentType.ReadWrite<JsDynTag59>(),
        60 => ComponentType.ReadWrite<JsDynTag60>(),
        61 => ComponentType.ReadWrite<JsDynTag61>(),
        62 => ComponentType.ReadWrite<JsDynTag62>(),
        63 => ComponentType.ReadWrite<JsDynTag63>(),
        _ => throw new System.InvalidOperationException($"Tag pool slot {slot} out of range"),
      };
    // @formatter:on

    #endregion
  }
}
