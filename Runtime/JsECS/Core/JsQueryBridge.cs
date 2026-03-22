namespace UnityJS.Entities.Core
{
  using System.Collections.Generic;
  using AOT;
  using Components;
  using QJS;
  using Runtime;
  using Unity.Collections;
  using Unity.Collections.LowLevel.Unsafe;
  using Unity.Entities;
  using static Runtime.QJSHelpers;

  public static class JsQueryBridge
  {
    static JsBridgeState B => JsRuntimeManager.Instance?.BridgeState as JsBridgeState;

    [System.ThreadStatic] static List<ComponentType> s_tempAll;
    [System.ThreadStatic] static List<ComponentType> s_tempNone;

    public static void Initialize(EntityManager entityManager)
    {
      var b = B;
      if (b == null)
        return;
      b.QueryEntityManager = entityManager;
      b.QueryInitialized = true;
    }

    /// <summary>
    /// Called from JsSystemRunner.OnUpdate (system context) each frame.
    /// Creates EntityQuery objects for any component combinations that
    /// were first encountered inside the P/Invoke callback.
    /// </summary>
    public static void FlushPendingQueries(EntityManager entityManager)
    {
      var b = B;
      if (b == null || b.PendingQueries.Count == 0)
        return;

      foreach (var kvp in b.PendingQueries)
      {
        var (all, none) = kvp.Value;
        var desc = new EntityQueryDesc { All = all, None = none };
        b.QueryCache[kvp.Key] = entityManager.CreateEntityQuery(desc);
      }

      b.PendingQueries.Clear();
    }

    /// <summary>
    /// Called from JsSystemRunner.OnUpdate (system context) each frame.
    /// Snapshots entity IDs for all cached queries so the P/Invoke callback
    /// never touches EntityManager directly.
    /// </summary>
    public static void PrecomputeQueryResults(EntityManager entityManager)
    {
      var b = B;
      if (b == null)
        return;

      foreach (var kvp in b.QueryCache)
      {
        var query = kvp.Value;
        if (query == default)
          continue;

        var entities = query.ToEntityArray(Allocator.Temp);
        var count = 0;

        // Reuse existing array if large enough — never shrink, track count separately
        b.PrecomputedIds.TryGetValue(kvp.Key, out var entry);
        var ids = entry.ids;
        if (ids == null || ids.Length < entities.Length)
          ids = new int[System.Math.Max(entities.Length, 64)];

        for (var i = 0; i < entities.Length; i++)
        {
          var entity = entities[i];
          var entityId = JsEntityRegistry.GetIdFromEntity(entity);
          if (entityId <= 0 && entityManager.HasComponent<JsEntityId>(entity))
            entityId = entityManager.GetComponentData<JsEntityId>(entity).value;

          if (entityId > 0)
            ids[count++] = entityId;
        }

        entities.Dispose();
        b.PrecomputedIds[kvp.Key] = (ids, count);
      }
    }

    public static void Shutdown()
    {
      var b = B;
      if (b == null)
        return;

      foreach (var kvp in b.QueryCache)
        if (kvp.Value != default)
          kvp.Value.Dispose();
      b.QueryCache.Clear();
      b.PendingQueries.Clear();
      b.PrecomputedIds.Clear();
      b.QueryInitialized = false;
    }

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

        AddFunction(ctx, ns, "query", Query, 0);

        QJS.JS_SetPropertyStr(ctx, global, pEcs, ns);
      }

      QJS.JS_FreeValue(ctx, global);
    }

    /// <summary>Query entities by component names — returns precomputed results only.</summary>
    [MonoPInvokeCallback(typeof(QJSShimCallback))]
    static unsafe void Query(
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
      if (b == null || !b.QueryInitialized)
      {
        SetResult(outU, outTag, QJS.JS_NewArray(ctx));
        return;
      }

      s_tempAll ??= new List<ComponentType>(8);
      s_tempNone ??= new List<ComponentType>(4);
      s_tempAll.Clear();
      s_tempNone.Clear();
      var allComponents = s_tempAll;
      var noneComponents = s_tempNone;

      if (argc >= 1 && QJS.IsObject(argv[0]) && QJS.JS_IsArray(ctx, argv[0]) == 0)
      {
        // Table form: { all: [...], none: [...] }
        var pAllBytes = QJS.U8("all");
        var pNoneBytes = QJS.U8("none");
        fixed (
          byte* pAll = pAllBytes,
            pNone = pNoneBytes
        )
        {
          var allArr = QJS.JS_GetPropertyStr(ctx, argv[0], pAll);
          if (QJS.IsObject(allArr) && QJS.JS_IsArray(ctx, allArr) != 0)
            ReadJsComponentArray(ctx, allArr, allComponents);
          QJS.JS_FreeValue(ctx, allArr);

          var noneArr = QJS.JS_GetPropertyStr(ctx, argv[0], pNone);
          if (QJS.IsObject(noneArr) && QJS.JS_IsArray(ctx, noneArr) != 0)
            ReadJsComponentArray(ctx, noneArr, noneComponents);
          QJS.JS_FreeValue(ctx, noneArr);
        }
      }
      else
      {
        // Varargs form: query("comp1", "comp2", ...)
        for (var i = 0; i < argc; i++)
        {
          if (!QJS.IsString(argv[i]))
            continue;
          var name = ArgString(ctx, argv, i);
          if (name != null && JsComponentRegistry.TryGetComponentType(name, out var ct))
            allComponents.Add(ct);
        }
      }

      if (allComponents.Count == 0)
      {
        SetResult(outU, outTag, QJS.JS_NewArray(ctx));
        return;
      }

      var hash = ComputeQueryHash(allComponents, noneComponents);

      // Return precomputed results if available
      if (b.PrecomputedIds.TryGetValue(hash, out var entry) && entry.count > 0)
      {
        // Use a plain JS array — Int32Array via qjs_shim_new_int32array references
        // the source buffer, which is invalid after the callback returns.
        var arr = QJS.JS_NewArray(ctx);
        for (var i = 0; i < entry.count; i++)
          QJS.JS_SetPropertyUint32(ctx, arr, (uint)i, QJS.NewInt32(ctx, entry.ids[i]));
        SetResult(outU, outTag, arr);
        return;
      }

      // No precomputed results — register for creation from system context next frame
      if (!b.QueryCache.ContainsKey(hash))
      {
        var allArray = allComponents.ToArray();
        var noneArray =
          noneComponents.Count > 0 ? noneComponents.ToArray() : System.Array.Empty<ComponentType>();
        b.PendingQueries[hash] = (allArray, noneArray);
      }

      // Return empty for this frame
      SetResult(outU, outTag, QJS.JS_NewArray(ctx));
    }

    static unsafe void ReadJsComponentArray(JSContext ctx, JSValue arr, List<ComponentType> result)
    {
      var pLengthBytes = QJS.U8("length");
      fixed (byte* pLength = pLengthBytes)
      {
        var lenVal = QJS.JS_GetPropertyStr(ctx, arr, pLength);
        int len;
        QJS.JS_ToInt32(ctx, &len, lenVal);
        QJS.JS_FreeValue(ctx, lenVal);

        for (uint i = 0; i < len; i++)
        {
          var elem = QJS.JS_GetPropertyUint32(ctx, arr, i);
          if (QJS.IsString(elem))
          {
            var name = QJS.ToManagedString(ctx, elem);
            if (name != null && JsComponentRegistry.TryGetComponentType(name, out var ct))
              result.Add(ct);
          }

          QJS.JS_FreeValue(ctx, elem);
        }
      }
    }

    static int ComputeQueryHash(
      List<ComponentType> allComponents,
      List<ComponentType> noneComponents
    )
    {
      var hash = 17;
      foreach (var ct in allComponents)
        hash = (hash * 31) + ct.TypeIndex.GetHashCode();
      hash = (hash * 31) + 0x7F7F;
      foreach (var ct in noneComponents)
        hash = (hash * 31) + ct.TypeIndex.GetHashCode();
      return hash;
    }
  }
}
