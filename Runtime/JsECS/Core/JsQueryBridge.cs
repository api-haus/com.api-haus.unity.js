namespace UnityJS.Entities.Core
{
  using System.Collections.Generic;
  using AOT;
  using Components;
  using QJS;
  using Unity.Collections;
  using Unity.Collections.LowLevel.Unsafe;
  using Unity.Entities;
  using static Runtime.QJSHelpers;

  public static class JsQueryBridge
  {
    static readonly Dictionary<int, EntityQuery> s_queryCache = new();
    static readonly Dictionary<int, (ComponentType[] all, ComponentType[] none)> s_pendingQueries =
      new();
    static readonly Dictionary<int, int[]> s_precomputedIds = new();
    static EntityManager s_entityManager;
    static bool s_initialized;

    public static void Initialize(EntityManager entityManager)
    {
      s_entityManager = entityManager;
      s_initialized = true;
    }

    /// <summary>
    /// Called from JsSystemRunner.OnUpdate (system context) each frame.
    /// Creates EntityQuery objects for any component combinations that
    /// were first encountered inside the P/Invoke callback.
    /// </summary>
    public static void FlushPendingQueries(EntityManager entityManager)
    {
      if (s_pendingQueries.Count == 0)
        return;

      foreach (var kvp in s_pendingQueries)
      {
        var (all, none) = kvp.Value;
        var desc = new EntityQueryDesc { All = all, None = none };
        s_queryCache[kvp.Key] = entityManager.CreateEntityQuery(desc);
      }

      s_pendingQueries.Clear();
    }

    /// <summary>
    /// Called from JsSystemRunner.OnUpdate (system context) each frame.
    /// Snapshots entity IDs for all cached queries so the P/Invoke callback
    /// never touches EntityManager directly.
    /// </summary>
    public static void PrecomputeQueryResults(EntityManager entityManager)
    {
      foreach (var kvp in s_queryCache)
      {
        var query = kvp.Value;
        if (query == default)
          continue;

        var entities = query.ToEntityArray(Allocator.Temp);
        var ids = new int[entities.Length];
        var count = 0;

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

        if (count < ids.Length)
          System.Array.Resize(ref ids, count);

        s_precomputedIds[kvp.Key] = ids;
      }
    }

    public static void Shutdown()
    {
      foreach (var kvp in s_queryCache)
        if (kvp.Value != default)
          kvp.Value.Dispose();
      s_queryCache.Clear();
      s_pendingQueries.Clear();
      s_precomputedIds.Clear();
      s_initialized = false;
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
      if (!s_initialized)
      {
        SetResult(outU, outTag, QJS.JS_NewArray(ctx));
        return;
      }

      var allComponents = new List<ComponentType>();
      var noneComponents = new List<ComponentType>();

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
      if (s_precomputedIds.TryGetValue(hash, out var ids) && ids.Length > 0)
      {
        // Use a plain JS array — Int32Array via qjs_shim_new_int32array references
        // the source buffer, which is invalid after the callback returns.
        var arr = QJS.JS_NewArray(ctx);
        for (var i = 0; i < ids.Length; i++)
          QJS.JS_SetPropertyUint32(ctx, arr, (uint)i, QJS.NewInt32(ctx, ids[i]));
        SetResult(outU, outTag, arr);
        return;
      }

      // No precomputed results — register for creation from system context next frame
      if (!s_queryCache.ContainsKey(hash))
      {
        var allArray = allComponents.ToArray();
        var noneArray =
          noneComponents.Count > 0 ? noneComponents.ToArray() : System.Array.Empty<ComponentType>();
        s_pendingQueries[hash] = (allArray, noneArray);
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
