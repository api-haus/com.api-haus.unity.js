namespace UnityJS.Entities.Core
{
	using System;
	using System.Collections.Generic;
	using UnityJS.QJS;

	public static class JsFunctionRegistry
	{
		static readonly Dictionary<string, List<Action<JSContext>>> s_registrations = new();

		public static void Register(string tableName, Action<JSContext> registerFunc)
		{
			if (!s_registrations.TryGetValue(tableName, out var list))
			{
				list = new List<Action<JSContext>>();
				s_registrations[tableName] = list;
			}

			if (!list.Contains(registerFunc))
				list.Add(registerFunc);
		}

		public static unsafe void RegisterAll(JSContext ctx)
		{
			var global = QJS.JS_GetGlobalObject(ctx);

			foreach (var kvp in s_registrations)
			{
				var tableName = kvp.Key;
				var funcs = kvp.Value;

				var tableNameBytes = System.Text.Encoding.UTF8.GetBytes(tableName + '\0');
				fixed (byte* pTableName = tableNameBytes)
				{
					var existing = QJS.JS_GetPropertyStr(ctx, global, pTableName);
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

					foreach (var func in funcs)
						func(ctx);

					QJS.JS_SetPropertyStr(ctx, global, pTableName, ns);
				}
			}

			QJS.JS_FreeValue(ctx, global);
		}
	}
}
