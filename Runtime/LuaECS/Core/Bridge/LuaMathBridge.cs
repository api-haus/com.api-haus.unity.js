namespace LuaECS.Core
{
	using AOT;
	using LuaNET.LuaJIT;
	using Unity.Burst;
	using Unity.CharacterController;
	using Unity.Mathematics;

	public static partial class LuaECSBridge
	{
		internal static void RegisterMathFunctions(lua_State l)
		{
			// Extend Lua's built-in math table (don't replace it)
			Lua.lua_getglobal(l, "math");

			// rgb_to_hsv stays hand-written (multi-return: h, s, v as separate numbers)
			RegisterFunction(l, "rgb_to_hsv", Math_RgbToHsv);

			Lua.lua_pop(l, 1);
		}

		/// <summary>Cross product of two vectors.</summary>
		[LuaCompile("math", "cross")]
		static float3 Cross(float3 a, float3 b) => math.cross(a, b);

		/// <summary>Dot product of two vectors.</summary>
		[LuaCompile("math", "dot")]
		static float Dot(float3 a, float3 b) => math.dot(a, b);

		/// <summary>Normalize a vector (safe, returns zero for zero-length).</summary>
		[LuaCompile("math", "normalize")]
		static float3 Normalize(float3 v) => math.normalizesafe(v);

		/// <summary>Length of a vector.</summary>
		[LuaCompile("math", "length")]
		static float Length(float3 v) => math.length(v);

		/// <summary>Squared length of a vector.</summary>
		[LuaCompile("math", "length_sq")]
		static float LengthSq(float3 v) => math.lengthsq(v);

		/// <summary>Linearly interpolate between two vectors.</summary>
		[LuaCompile("math", "lerp")]
		static float3 Lerp(float3 a, float3 b, float t) => math.lerp(a, b, t);

		/// <summary>Clamp a vector to a maximum length.</summary>
		[LuaCompile("math", "clamp_length")]
		static float3 ClampLength(float3 v, float maxLen) => MathUtilities.ClampToMaxLength(v, maxLen);

		/// <summary>Project a vector onto a plane.</summary>
		[LuaCompile("math", "project_on_plane")]
		static float3 ProjectOnPlane(float3 v, float3 planeNormal) => MathUtilities.ProjectOnPlane(v, planeNormal);

		/// <summary>Reorient a vector on a plane along a direction.</summary>
		[LuaCompile("math", "reorient_on_plane")]
		static float3 ReorientOnPlane(float3 v, float3 planeNormal, float3 direction) =>
			MathUtilities.ReorientVectorOnPlaneAlongDirection(v, planeNormal, direction);

		/// <summary>Distance between two points.</summary>
		[LuaCompile("math", "distance")]
		static float Distance(float3 a, float3 b) => math.distance(a, b);

		/// <summary>Convert HSV (hue 0-360) to RGB.</summary>
		[LuaCompile("math", "hsv_to_rgb")]
		static float3 HsvToRgb(float h, float s, float v)
		{
			h = ((h % 360f) + 360f) % 360f;
			var c = v * s;
			var x = c * (1f - math.abs((h / 60f) % 2f - 1f));
			var m = v - c;

			float3 rgb;
			if (h < 60f) rgb = new float3(c, x, 0f);
			else if (h < 120f) rgb = new float3(x, c, 0f);
			else if (h < 180f) rgb = new float3(0f, c, x);
			else if (h < 240f) rgb = new float3(0f, x, c);
			else if (h < 300f) rgb = new float3(x, 0f, c);
			else rgb = new float3(c, 0f, x);

			return rgb + m;
		}

		/// <summary>Convert RGB to HSV. Returns h, s, v.</summary>
		[LuaCompile("math", "rgb_to_hsv", Signature = "fun(rgb: vec3): number, number, number")]
		[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]
		static int Math_RgbToHsv(lua_State l)
		{
			var rgb = TableToFloat3(l, 1);
			var cMax = math.max(rgb.x, math.max(rgb.y, rgb.z));
			var cMin = math.min(rgb.x, math.min(rgb.y, rgb.z));
			var delta = cMax - cMin;

			float h = 0f;
			if (delta > 1e-6f)
			{
				if (cMax == rgb.x) h = 60f * (((rgb.y - rgb.z) / delta) % 6f);
				else if (cMax == rgb.y) h = 60f * ((rgb.z - rgb.x) / delta + 2f);
				else h = 60f * ((rgb.x - rgb.y) / delta + 4f);
			}
			if (h < 0f) h += 360f;

			var s = cMax > 1e-6f ? delta / cMax : 0f;

			Lua.lua_pushnumber(l, h);
			Lua.lua_pushnumber(l, s);
			Lua.lua_pushnumber(l, cMax);
			return 3;
		}

		/// <summary>Convert Oklab to linear RGB.</summary>
		[LuaCompile("math", "oklab_to_rgb")]
		static float3 OklabToRgb(float3 lab)
		{
			var lp = lab.x + 0.3963377774f * lab.y + 0.2158037573f * lab.z;
			var mp = lab.x - 0.1055613458f * lab.y - 0.0638541728f * lab.z;
			var sp = lab.x - 0.0894841775f * lab.y - 1.2914855480f * lab.z;

			var ll = lp * lp * lp;
			var mm = mp * mp * mp;
			var ss = sp * sp * sp;

			var r = +4.0767416621f * ll - 3.3077115913f * mm + 0.2309699292f * ss;
			var g = -1.2684380046f * ll + 2.6097574011f * mm - 0.3413193965f * ss;
			var b = -0.0041960863f * ll - 0.7034186147f * mm + 1.7076147010f * ss;

			return new float3(r, g, b);
		}

		/// <summary>Convert linear RGB to Oklab.</summary>
		[LuaCompile("math", "rgb_to_oklab")]
		static float3 RgbToOklab(float3 rgb)
		{
			var ll = 0.4122214708f * rgb.x + 0.5363325363f * rgb.y + 0.0514459929f * rgb.z;
			var mm = 0.2119034982f * rgb.x + 0.6806995451f * rgb.y + 0.1073969566f * rgb.z;
			var ss = 0.0883024619f * rgb.x + 0.2817188376f * rgb.y + 0.6299787005f * rgb.z;

			var lc = math.pow(math.max(ll, 0f), 1f / 3f);
			var mc = math.pow(math.max(mm, 0f), 1f / 3f);
			var sc = math.pow(math.max(ss, 0f), 1f / 3f);

			var L = 0.2104542553f * lc + 0.7936177850f * mc - 0.0040720468f * sc;
			var a = 1.9779984951f * lc - 2.4285922050f * mc + 0.4505937099f * sc;
			var bv = 0.0259040371f * lc + 0.7827717662f * mc - 0.8086757660f * sc;

			return new float3(L, a, bv);
		}
	}
}
