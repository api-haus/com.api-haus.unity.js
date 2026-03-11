namespace LuaECS.Core
{
	using System;
	using System.Collections.Generic;
	using LuaNET.LuaJIT;

	public static class LuaFunctionRegistry
	{
		static readonly Dictionary<string, List<Action<lua_State>>> s_registrations = new();

		public static void Register(string tableName, Action<lua_State> registerFunc)
		{
			if (!s_registrations.TryGetValue(tableName, out var list))
			{
				list = new List<Action<lua_State>>();
				s_registrations[tableName] = list;
			}

			if (!list.Contains(registerFunc))
				list.Add(registerFunc);
		}

		public static void RegisterAll(lua_State l)
		{
			foreach (var kvp in s_registrations)
			{
				var tableName = kvp.Key;
				var funcs = kvp.Value;

				Lua.lua_getglobal(l, tableName);
				if (Lua.lua_isnil(l, -1) != 0)
				{
					Lua.lua_pop(l, 1);
					Lua.lua_newtable(l);
				}

				foreach (var func in funcs)
					func(l);

				Lua.lua_setglobal(l, tableName);
			}
		}
	}
}
