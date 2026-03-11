namespace LuaECS.Core
{
	using System.Text;
	using AOT;
	using Drawing;
	using LuaNET.LuaJIT;
	using UnityEngine;

	public static partial class LuaECSBridge
	{
		static Color s_currentDrawColor = Color.white;
		static float s_currentDuration;

		internal static void RegisterDrawFunctions(lua_State l)
		{
			Lua.lua_newtable(l);

			RegisterFunction(l, "line", Draw_Line);
			RegisterFunction(l, "ray", Draw_Ray);
			RegisterFunction(l, "arrow", Draw_Arrow);

			RegisterFunction(l, "wire_sphere", Draw_WireSphere);
			RegisterFunction(l, "wire_box", Draw_WireBox);
			RegisterFunction(l, "wire_capsule", Draw_WireCapsule);
			RegisterFunction(l, "circle_xz", Draw_CircleXZ);

			RegisterFunction(l, "solid_box", Draw_SolidBox);
			RegisterFunction(l, "solid_circle", Draw_SolidCircle);

			RegisterFunction(l, "label_2d", Draw_Label2D);

			RegisterFunction(l, "set_color", Draw_SetColor);
			RegisterFunction(l, "with_duration", Draw_WithDuration);

			Lua.lua_setglobal(l, "draw");
		}

		/// <summary>Draw a debug line.</summary>
		[LuaCompile("draw", "line", Signature = "fun(from: vec3, to: vec3)")]
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Draw_Line(lua_State l)
		{
			if (Lua.lua_gettop(l) < 2)
				return 0;

			var from = TableToFloat3(l, 1);
			var to = TableToFloat3(l, 2);

			if (s_currentDuration > 0)
			{
				using (Draw.ingame.WithDuration(s_currentDuration))
				{
					Draw.ingame.Line(from, to, s_currentDrawColor);
				}
			}
			else
			{
				Draw.ingame.Line(from, to, s_currentDrawColor);
			}

			return 0;
		}

		/// <summary>Draw a debug ray.</summary>
		[LuaCompile("draw", "ray", Signature = "fun(origin: vec3, direction: vec3)")]
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Draw_Ray(lua_State l)
		{
			if (Lua.lua_gettop(l) < 2)
				return 0;

			var origin = TableToFloat3(l, 1);
			var direction = TableToFloat3(l, 2);

			if (s_currentDuration > 0)
			{
				using (Draw.ingame.WithDuration(s_currentDuration))
				{
					Draw.ingame.Ray(origin, direction, s_currentDrawColor);
				}
			}
			else
			{
				Draw.ingame.Ray(origin, direction, s_currentDrawColor);
			}

			return 0;
		}

		/// <summary>Draw a debug arrow.</summary>
		[LuaCompile("draw", "arrow", Signature = "fun(from: vec3, to: vec3)")]
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Draw_Arrow(lua_State l)
		{
			if (Lua.lua_gettop(l) < 2)
				return 0;

			var from = TableToFloat3(l, 1);
			var to = TableToFloat3(l, 2);

			if (s_currentDuration > 0)
			{
				using (Draw.ingame.WithDuration(s_currentDuration))
				{
					Draw.ingame.Arrow(from, to, s_currentDrawColor);
				}
			}
			else
			{
				Draw.ingame.Arrow(from, to, s_currentDrawColor);
			}

			return 0;
		}

		/// <summary>Draw a wireframe sphere.</summary>
		[LuaCompile("draw", "wire_sphere", Signature = "fun(center: vec3, radius: number)")]
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Draw_WireSphere(lua_State l)
		{
			if (Lua.lua_gettop(l) < 2)
				return 0;

			var center = TableToFloat3(l, 1);
			var radius = (float)Lua.lua_tonumber(l, 2);

			if (s_currentDuration > 0)
			{
				using (Draw.ingame.WithDuration(s_currentDuration))
				{
					Draw.ingame.WireSphere(center, radius, s_currentDrawColor);
				}
			}
			else
			{
				Draw.ingame.WireSphere(center, radius, s_currentDrawColor);
			}

			return 0;
		}

		/// <summary>Draw a wireframe box.</summary>
		[LuaCompile("draw", "wire_box", Signature = "fun(center: vec3, size: vec3)")]
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Draw_WireBox(lua_State l)
		{
			if (Lua.lua_gettop(l) < 2)
				return 0;

			var center = TableToFloat3(l, 1);
			var size = TableToFloat3(l, 2);

			if (s_currentDuration > 0)
			{
				using (Draw.ingame.WithDuration(s_currentDuration))
				{
					Draw.ingame.WireBox(center, size, s_currentDrawColor);
				}
			}
			else
			{
				Draw.ingame.WireBox(center, size, s_currentDrawColor);
			}

			return 0;
		}

		/// <summary>Draw a wireframe capsule.</summary>
		[LuaCompile("draw", "wire_capsule", Signature = "fun(start: vec3, end_pos: vec3, radius: number)")]
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Draw_WireCapsule(lua_State l)
		{
			if (Lua.lua_gettop(l) < 3)
				return 0;

			var start = TableToFloat3(l, 1);
			var end = TableToFloat3(l, 2);
			var radius = (float)Lua.lua_tonumber(l, 3);

			if (s_currentDuration > 0)
			{
				using (Draw.ingame.WithDuration(s_currentDuration))
				{
					Draw.ingame.WireCapsule(start, end, radius, s_currentDrawColor);
				}
			}
			else
			{
				Draw.ingame.WireCapsule(start, end, radius, s_currentDrawColor);
			}

			return 0;
		}

		/// <summary>Draw a circle on the XZ plane.</summary>
		[LuaCompile("draw", "circle_xz", Signature = "fun(center: vec3, radius: number)")]
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Draw_CircleXZ(lua_State l)
		{
			if (Lua.lua_gettop(l) < 2)
				return 0;

			var center = TableToFloat3(l, 1);
			var radius = (float)Lua.lua_tonumber(l, 2);

			if (s_currentDuration > 0)
			{
				using (Draw.ingame.WithDuration(s_currentDuration))
				{
					Draw.ingame.xz.Circle(center, radius, s_currentDrawColor);
				}
			}
			else
			{
				Draw.ingame.xz.Circle(center, radius, s_currentDrawColor);
			}

			return 0;
		}

		/// <summary>Draw a solid box.</summary>
		[LuaCompile("draw", "solid_box", Signature = "fun(center: vec3, size: vec3)")]
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Draw_SolidBox(lua_State l)
		{
			if (Lua.lua_gettop(l) < 2)
				return 0;

			var center = TableToFloat3(l, 1);
			var size = TableToFloat3(l, 2);

			if (s_currentDuration > 0)
			{
				using (Draw.ingame.WithDuration(s_currentDuration))
				{
					Draw.ingame.SolidBox(center, size, s_currentDrawColor);
				}
			}
			else
			{
				Draw.ingame.SolidBox(center, size, s_currentDrawColor);
			}

			return 0;
		}

		/// <summary>Draw a solid circle.</summary>
		[LuaCompile("draw", "solid_circle", Signature = "fun(center: vec3, normal: vec3, radius: number)")]
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Draw_SolidCircle(lua_State l)
		{
			if (Lua.lua_gettop(l) < 3)
				return 0;

			var center = TableToFloat3(l, 1);
			var normal = TableToFloat3(l, 2);
			var radius = (float)Lua.lua_tonumber(l, 3);

			if (s_currentDuration > 0)
			{
				using (Draw.ingame.WithDuration(s_currentDuration))
				{
					Draw.ingame.SolidCircle(center, normal, radius, s_currentDrawColor);
				}
			}
			else
			{
				Draw.ingame.SolidCircle(center, normal, radius, s_currentDrawColor);
			}

			return 0;
		}

		/// <summary>Draw a 2D text label at a world position.</summary>
		[LuaCompile("draw", "label_2d", Signature = "fun(position: vec3, text: string)")]
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Draw_Label2D(lua_State l)
		{
			if (Lua.lua_gettop(l) < 2)
				return 0;

			var position = TableToFloat3(l, 1);

			ulong len = 0;
			unsafe
			{
				var ptr = Lua.lua_tolstring_ptr(l, 2, ref len);
				if (ptr == null || len == 0)
					return 0;

				var text = Encoding.UTF8.GetString(ptr, (int)len);

				if (s_currentDuration > 0)
				{
					using (Draw.ingame.WithDuration(s_currentDuration))
					{
						Draw.ingame.Label2D(position, text, s_currentDrawColor);
					}
				}
				else
				{
					Draw.ingame.Label2D(position, text, s_currentDrawColor);
				}
			}

			return 0;
		}

		/// <summary>Set color for subsequent draw calls.</summary>
		[LuaCompile("draw", "set_color", Signature = "fun(r: number, g: number, b: number, a?: number)")]
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Draw_SetColor(lua_State l)
		{
			var r = (float)Lua.lua_tonumber(l, 1);
			var g = (float)Lua.lua_tonumber(l, 2);
			var b = (float)Lua.lua_tonumber(l, 3);
			var a = Lua.lua_gettop(l) >= 4 ? (float)Lua.lua_tonumber(l, 4) : 1f;

			s_currentDrawColor = new Color(r, g, b, a);
			return 0;
		}

		/// <summary>Set duration for subsequent draw calls.</summary>
		[LuaCompile("draw", "with_duration", Signature = "fun(duration: number)")]
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Draw_WithDuration(lua_State l)
		{
			s_currentDuration = (float)Lua.lua_tonumber(l, 1);
			return 0;
		}
	}
}
