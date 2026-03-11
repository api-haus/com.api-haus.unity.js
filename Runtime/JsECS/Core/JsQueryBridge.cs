namespace UnityJS.Entities.Core
{
	using System.Collections.Generic;
	using System.Text;
	using AOT;
	using Components;
	using UnityJS.QJS;
	using Unity.Collections;
	using Unity.Entities;

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

		/// <summary>Query entities by component names. Stub — returns empty array.</summary>
		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void Query(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			// Stub — full query implementation in Stage 4
			var arr = QJS.JS_NewArray(ctx);
			*outU = arr.u;
			*outTag = arr.tag;
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
				None = noneComponents.Count > 0 ? noneComponents.ToArray() : System.Array.Empty<ComponentType>(),
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
