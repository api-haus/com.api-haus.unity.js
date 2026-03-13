namespace UnityJS.Entities.Core
{
  using System.Collections.Generic;
  using System.Runtime.InteropServices;
  using System.Text;
  using AOT;
  using Components;
  using Unity.Collections;
  using Unity.Entities;
  using UnityJS.QJS;

  public static class JsQueryBridge
  {
    static readonly Dictionary<int, EntityQuery> s_queryCache = new();
    static EntityManager s_entityManager;
    static bool s_initialized;

    public static void Initialize(EntityManager entityManager)
    {
      s_entityManager = entityManager;
      s_initialized = true;
    }

    public static void Shutdown()
    {
      foreach (var kvp in s_queryCache)
      {
        if (kvp.Value != default)
          kvp.Value.Dispose();
      }
      s_queryCache.Clear();
      s_initialized = false;
    }

    public static unsafe void Register(JSContext ctx)
    {
      // Get or create ecs namespace
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

        var pQueryBytes = Encoding.UTF8.GetBytes("query\0");
        fixed (byte* pQuery = pQueryBytes)
        {
          var fn = QJSShim.qjs_shim_new_function(ctx, Query, pQuery, 0);
          QJS.JS_SetPropertyStr(ctx, ns, pQuery, fn);
        }

        QJS.JS_SetPropertyStr(ctx, global, pEcs, ns);
      }
      QJS.JS_FreeValue(ctx, global);
    }

    /// <summary>Query entities by component names.</summary>
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
        var empty = QJS.JS_NewArray(ctx);
        *outU = empty.u;
        *outTag = empty.tag;
        return;
      }

      var allComponents = new List<ComponentType>();
      var noneComponents = new List<ComponentType>();

      if (argc >= 1 && QJS.IsObject(argv[0]) && QJS.JS_IsArray(ctx, argv[0]) == 0)
      {
        // Table form: { all: [...], none: [...] }
        var pAllBytes = Encoding.UTF8.GetBytes("all\0");
        var pNoneBytes = Encoding.UTF8.GetBytes("none\0");
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
          var ptr = QJS.JS_ToCString(ctx, argv[i]);
          var name = Marshal.PtrToStringUTF8((nint)ptr);
          QJS.JS_FreeCString(ctx, ptr);
          if (name != null && JsComponentRegistry.TryGetComponentType(name, out var ct))
            allComponents.Add(ct);
        }
      }

      if (allComponents.Count == 0)
      {
        var empty = QJS.JS_NewArray(ctx);
        *outU = empty.u;
        *outTag = empty.tag;
        return;
      }

      var query = GetOrCreateQuery(allComponents, noneComponents);
      var entities = query.ToEntityArray(Allocator.Temp);

      var arr = QJS.JS_NewArray(ctx);
      uint resultIndex = 0;
      for (var i = 0; i < entities.Length; i++)
      {
        var entity = entities[i];
        var entityId = JsEntityRegistry.GetIdFromEntity(entity);
        if (entityId <= 0)
        {
          if (s_entityManager.HasComponent<JsEntityId>(entity))
            entityId = s_entityManager.GetComponentData<JsEntityId>(entity).value;
        }

        if (entityId > 0)
        {
          QJS.JS_SetPropertyUint32(ctx, arr, resultIndex++, QJS.NewInt32(ctx, entityId));
        }
      }
      entities.Dispose();

      *outU = arr.u;
      *outTag = arr.tag;
    }

    static unsafe void ReadJsComponentArray(JSContext ctx, JSValue arr, List<ComponentType> result)
    {
      var pLengthBytes = Encoding.UTF8.GetBytes("length\0");
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
            var ptr = QJS.JS_ToCString(ctx, elem);
            var name = Marshal.PtrToStringUTF8((nint)ptr);
            QJS.JS_FreeCString(ctx, ptr);
            if (name != null && JsComponentRegistry.TryGetComponentType(name, out var ct))
              result.Add(ct);
          }
          QJS.JS_FreeValue(ctx, elem);
        }
      }
    }

    static EntityQuery GetOrCreateQuery(
      List<ComponentType> allComponents,
      List<ComponentType> noneComponents
    )
    {
      var hash = ComputeQueryHash(allComponents, noneComponents);
      if (s_queryCache.TryGetValue(hash, out var cached))
        return cached;

      var desc = new EntityQueryDesc
      {
        All = allComponents.ToArray(),
        None =
          noneComponents.Count > 0 ? noneComponents.ToArray() : System.Array.Empty<ComponentType>(),
      };

      var query = s_entityManager.CreateEntityQuery(desc);
      s_queryCache[hash] = query;
      return query;
    }

    static int ComputeQueryHash(
      List<ComponentType> allComponents,
      List<ComponentType> noneComponents
    )
    {
      var hash = 17;
      foreach (var ct in allComponents)
        hash = hash * 31 + ct.TypeIndex.GetHashCode();
      hash = hash * 31 + 0x7F7F;
      foreach (var ct in noneComponents)
        hash = hash * 31 + ct.TypeIndex.GetHashCode();
      return hash;
    }
  }
}
