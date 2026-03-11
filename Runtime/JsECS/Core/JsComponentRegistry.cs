namespace UnityJS.Entities.Core
{
	using System;
	using System.Collections.Generic;
	using UnityJS.QJS;
	using Unity.Entities;

	public delegate void JsLookupUpdater(ref SystemState state);

	public static class JsComponentRegistry
	{
		static readonly Dictionary<string, ComponentType> s_components = new();
		static readonly List<Action<JSContext>> s_bridgeRegistrations = new();
		static readonly List<JsLookupUpdater> s_lookupUpdaters = new();

		public static void Register(string jsName, ComponentType componentType)
		{
			s_components[jsName] = componentType;
		}

		public static void RegisterBridge(
			string jsName,
			ComponentType componentType,
			Action<JSContext> registerFunc,
			JsLookupUpdater updateLookupFunc
		)
		{
			s_components[jsName] = componentType;
			s_bridgeRegistrations.Add(registerFunc);
			s_lookupUpdaters.Add(updateLookupFunc);
		}

		public static void RegisterEnum(Action<JSContext> registerFunc)
		{
			s_bridgeRegistrations.Add(registerFunc);
		}

		public static bool TryGetComponentType(string jsName, out ComponentType componentType)
		{
			return s_components.TryGetValue(jsName, out componentType);
		}

		public static void RegisterAllBridges(JSContext ctx)
		{
			foreach (var reg in s_bridgeRegistrations)
			{
				reg(ctx);
			}
		}

		public static void UpdateAllLookups(ref SystemState state)
		{
			foreach (var updater in s_lookupUpdaters)
			{
				updater(ref state);
			}
		}
	}
}
