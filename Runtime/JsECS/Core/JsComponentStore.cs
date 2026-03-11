namespace UnityJS.Entities.Core
{
	using System.Collections.Generic;
	using System.Text;
	using AOT;
	using Components;
	using UnityJS.QJS;
	using Unity.Entities;
	using Unity.Logging;

	/// <summary>
	/// Manages JS-defined component types.
	/// Structural presence tracked via ECS tag pool (JsDynTag0..63).
	/// Field data stored in JS objects.
	/// </summary>
	public static class JsComponentStore
	{
		const int MaxSlots = 64;

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
			// Registration of ecs.define, ecs.add, ecs.remove, ecs.has as stubs
			// Bodies will be implemented in Stage 4
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
					var fn = QJSShim.qjs_shim_new_function(ctx, EcsDefine, pDefine, 0);
					QJS.JS_SetPropertyStr(ctx, ns, pDefine, fn);
				}
				var pAddBytes = Encoding.UTF8.GetBytes("add\0");
				fixed (byte* pAdd = pAddBytes)
				{
					var fn = QJSShim.qjs_shim_new_function(ctx, EcsAdd, pAdd, 0);
					QJS.JS_SetPropertyStr(ctx, ns, pAdd, fn);
				}
				var pRemoveBytes = Encoding.UTF8.GetBytes("remove\0");
				fixed (byte* pRemove = pRemoveBytes)
				{
					var fn = QJSShim.qjs_shim_new_function(ctx, EcsRemove, pRemove, 0);
					QJS.JS_SetPropertyStr(ctx, ns, pRemove, fn);
				}
				var pHasBytes = Encoding.UTF8.GetBytes("has\0");
				fixed (byte* pHas = pHasBytes)
				{
					var fn = QJSShim.qjs_shim_new_function(ctx, EcsHas, pHas, 0);
					QJS.JS_SetPropertyStr(ctx, ns, pHas, fn);
				}
				var pGetBytes = Encoding.UTF8.GetBytes("get\0");
				fixed (byte* pGet = pGetBytes)
				{
					var fn = QJSShim.qjs_shim_new_function(ctx, EcsGet, pGet, 0);
					QJS.JS_SetPropertyStr(ctx, ns, pGet, fn);
				}

				QJS.JS_SetPropertyStr(ctx, global, pEcs, ns);
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

		#region Bridge Function Stubs

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void EcsDefine(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			// Stub — full implementation in Stage 4
			var undef = QJS.JS_UNDEFINED;
			*outU = undef.u;
			*outTag = undef.tag;
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void EcsAdd(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			var undef = QJS.JS_UNDEFINED;
			*outU = undef.u;
			*outTag = undef.tag;
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void EcsRemove(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			var undef = QJS.JS_UNDEFINED;
			*outU = undef.u;
			*outTag = undef.tag;
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void EcsHas(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			var undef = QJS.JS_UNDEFINED;
			*outU = undef.u;
			*outTag = undef.tag;
		}

		[MonoPInvokeCallback(typeof(QJSShimCallback))]
		static unsafe void EcsGet(JSContext ctx, long thisU, long thisTag,
			int argc, JSValue* argv, long* outU, long* outTag)
		{
			var undef = QJS.JS_UNDEFINED;
			*outU = undef.u;
			*outTag = undef.tag;
		}

		#endregion

		#region Tag Pool Dispatch

		// @formatter:off
		static ComponentType GetTagType(int slot) => slot switch
		{
			0  => ComponentType.ReadWrite<JsDynTag0>(),  1  => ComponentType.ReadWrite<JsDynTag1>(),
			2  => ComponentType.ReadWrite<JsDynTag2>(),  3  => ComponentType.ReadWrite<JsDynTag3>(),
			4  => ComponentType.ReadWrite<JsDynTag4>(),  5  => ComponentType.ReadWrite<JsDynTag5>(),
			6  => ComponentType.ReadWrite<JsDynTag6>(),  7  => ComponentType.ReadWrite<JsDynTag7>(),
			8  => ComponentType.ReadWrite<JsDynTag8>(),  9  => ComponentType.ReadWrite<JsDynTag9>(),
			10 => ComponentType.ReadWrite<JsDynTag10>(), 11 => ComponentType.ReadWrite<JsDynTag11>(),
			12 => ComponentType.ReadWrite<JsDynTag12>(), 13 => ComponentType.ReadWrite<JsDynTag13>(),
			14 => ComponentType.ReadWrite<JsDynTag14>(), 15 => ComponentType.ReadWrite<JsDynTag15>(),
			16 => ComponentType.ReadWrite<JsDynTag16>(), 17 => ComponentType.ReadWrite<JsDynTag17>(),
			18 => ComponentType.ReadWrite<JsDynTag18>(), 19 => ComponentType.ReadWrite<JsDynTag19>(),
			20 => ComponentType.ReadWrite<JsDynTag20>(), 21 => ComponentType.ReadWrite<JsDynTag21>(),
			22 => ComponentType.ReadWrite<JsDynTag22>(), 23 => ComponentType.ReadWrite<JsDynTag23>(),
			24 => ComponentType.ReadWrite<JsDynTag24>(), 25 => ComponentType.ReadWrite<JsDynTag25>(),
			26 => ComponentType.ReadWrite<JsDynTag26>(), 27 => ComponentType.ReadWrite<JsDynTag27>(),
			28 => ComponentType.ReadWrite<JsDynTag28>(), 29 => ComponentType.ReadWrite<JsDynTag29>(),
			30 => ComponentType.ReadWrite<JsDynTag30>(), 31 => ComponentType.ReadWrite<JsDynTag31>(),
			32 => ComponentType.ReadWrite<JsDynTag32>(), 33 => ComponentType.ReadWrite<JsDynTag33>(),
			34 => ComponentType.ReadWrite<JsDynTag34>(), 35 => ComponentType.ReadWrite<JsDynTag35>(),
			36 => ComponentType.ReadWrite<JsDynTag36>(), 37 => ComponentType.ReadWrite<JsDynTag37>(),
			38 => ComponentType.ReadWrite<JsDynTag38>(), 39 => ComponentType.ReadWrite<JsDynTag39>(),
			40 => ComponentType.ReadWrite<JsDynTag40>(), 41 => ComponentType.ReadWrite<JsDynTag41>(),
			42 => ComponentType.ReadWrite<JsDynTag42>(), 43 => ComponentType.ReadWrite<JsDynTag43>(),
			44 => ComponentType.ReadWrite<JsDynTag44>(), 45 => ComponentType.ReadWrite<JsDynTag45>(),
			46 => ComponentType.ReadWrite<JsDynTag46>(), 47 => ComponentType.ReadWrite<JsDynTag47>(),
			48 => ComponentType.ReadWrite<JsDynTag48>(), 49 => ComponentType.ReadWrite<JsDynTag49>(),
			50 => ComponentType.ReadWrite<JsDynTag50>(), 51 => ComponentType.ReadWrite<JsDynTag51>(),
			52 => ComponentType.ReadWrite<JsDynTag52>(), 53 => ComponentType.ReadWrite<JsDynTag53>(),
			54 => ComponentType.ReadWrite<JsDynTag54>(), 55 => ComponentType.ReadWrite<JsDynTag55>(),
			56 => ComponentType.ReadWrite<JsDynTag56>(), 57 => ComponentType.ReadWrite<JsDynTag57>(),
			58 => ComponentType.ReadWrite<JsDynTag58>(), 59 => ComponentType.ReadWrite<JsDynTag59>(),
			60 => ComponentType.ReadWrite<JsDynTag60>(), 61 => ComponentType.ReadWrite<JsDynTag61>(),
			62 => ComponentType.ReadWrite<JsDynTag62>(), 63 => ComponentType.ReadWrite<JsDynTag63>(),
			_ => throw new System.InvalidOperationException($"Tag pool slot {slot} out of range"),
		};
		// @formatter:on

		#endregion
	}
}
