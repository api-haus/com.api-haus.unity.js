using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using LuaECS.Core;
using UnityEditor;
using UnityEngine;

namespace LuaGame.Editor
{
	public static class LuaTypeStubGenerator
	{
		const string OutputPath = "Assets/StreamingAssets/lua/types/luagame.lua";

		static readonly Dictionary<string, string> TypeMap = new()
		{
			{ "System.Single", "number" },
			{ "System.Int32", "integer" },
			{ "System.Boolean", "boolean" },
			{ "Unity.Mathematics.float2", "vec2" },
			{ "Unity.Mathematics.float3", "vec3" },
			{ "Unity.Mathematics.float4", "vec4" },
			{ "Unity.Mathematics.quaternion", "quat" },
		};

		[InitializeOnLoadMethod]
		static void OnDomainReload()
		{
			var content = GenerateContent();
			if (File.Exists(OutputPath))
			{
				var existingHash = ComputeHash(File.ReadAllText(OutputPath));
				var newHash = ComputeHash(content);
				if (existingHash == newHash)
					return;
			}

			WriteOutput(content);
		}

		[MenuItem("Tools/Lua/Generate Type Stubs")]
		public static void Generate()
		{
			var content = GenerateContent();
			WriteOutput(content);
		}

		static void WriteOutput(string content)
		{
			var dir = Path.GetDirectoryName(OutputPath);
			if (!Directory.Exists(dir))
				Directory.CreateDirectory(dir);

			File.WriteAllText(OutputPath, content);
			AssetDatabase.Refresh();
			Debug.Log($"[LuaTypeStubs] Generated {OutputPath}");
		}

		static string ComputeHash(string text)
		{
			using var sha = SHA256.Create();
			var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
			return Convert.ToBase64String(bytes);
		}

		internal static string GenerateContent()
		{
			var sb = new StringBuilder();
			sb.AppendLine("-- Auto-generated LuaCATS type stubs for LuaGame bridges");
			sb.AppendLine("-- Do not edit manually — regenerate via Tools > Lua > Generate Type Stubs");
			sb.AppendLine("---@meta");
			sb.AppendLine();

			sb.AppendLine("---@alias entity integer");
			sb.AppendLine("---@alias vec2 {x: number, y: number}");
			sb.AppendLine("---@alias vec3 {x: number, y: number, z: number}");
			sb.AppendLine("---@alias vec4 {x: number, y: number, z: number, w: number}");
			sb.AppendLine("---@alias quat {x: number, y: number, z: number, w: number}");
			sb.AppendLine();

			GenerateEnums(sb);
			GenerateBridgedComponents(sb);
			GenerateBuiltinBridges(sb);
			GenerateLifecycleCallbacks(sb);

			return sb.ToString();
		}

		static readonly Dictionary<Type, string> s_enumLuaNames = new();

		static void GenerateEnums(StringBuilder sb)
		{
			s_enumLuaNames.Clear();
			sb.AppendLine("-- ── Enum Constants (auto-discovered) ─────────────────────");
			sb.AppendLine();

			var enums = new List<(string luaName, Type type)>();

			foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
			{
				Type[] types;
				try { types = asm.GetTypes(); }
				catch { continue; }

				foreach (var t in types)
				{
					if (!t.IsEnum)
						continue;
					var attr = t.GetCustomAttribute<LuaBridgeAttribute>();
					if (attr == null) continue;
					var luaName = !string.IsNullOrEmpty(attr.LuaName) ? attr.LuaName : ToScreamingSnakeCase(t.Name);
					enums.Add((luaName, t));
					s_enumLuaNames[t] = t.Name;
				}
			}

			enums.Sort((a, b) => string.Compare(a.luaName, b.luaName, StringComparison.Ordinal));

			foreach (var (luaName, type) in enums)
			{
				var enumDocs = GetEnumDocs(type);

				if (enumDocs != null && enumDocs.TryGetValue("", out var enumDesc))
					sb.AppendLine($"---{enumDesc}");

				sb.AppendLine($"---@enum {type.Name}");
				sb.AppendLine($"{luaName} = {{");
				foreach (var name in Enum.GetNames(type))
				{
					var val = Convert.ToInt32(Enum.Parse(type, name));
					if (enumDocs != null && enumDocs.TryGetValue(name, out var memberDesc))
						sb.AppendLine($"  {name} = {val}, ---{memberDesc}");
					else
						sb.AppendLine($"  {name} = {val},");
				}
				sb.AppendLine("}");
				sb.AppendLine();
			}
		}

		static Dictionary<string, string> GetComponentDocs(Type componentType)
		{
			var typeName = $"LuaECS.Generated.Lua{componentType.Name}Bridge";
			foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
			{
				var docsType = asm.GetType(typeName);
				if (docsType == null)
					continue;
				var field = docsType.GetField("Descriptions",
					BindingFlags.Public | BindingFlags.Static);
				return field?.GetValue(null) as Dictionary<string, string>;
			}
			return null;
		}

		static Dictionary<string, string> GetEnumDocs(Type enumType)
		{
			var typeName = $"LuaECS.Generated.Lua{enumType.Name}Enum";
			foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
			{
				var docsType = asm.GetType(typeName);
				if (docsType == null)
					continue;
				var field = docsType.GetField("Descriptions",
					BindingFlags.Public | BindingFlags.Static);
				return field?.GetValue(null) as Dictionary<string, string>;
			}
			return null;
		}

		static void GenerateBridgedComponents(StringBuilder sb)
		{
			sb.AppendLine("-- ── Component Bridges (auto-discovered) ──────────────────");
			sb.AppendLine();

			var targets = new List<(string luaName, Type type, bool needAccessors, bool needSetters)>();

			foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
			{
				Type[] types;
				try { types = asm.GetTypes(); }
				catch { continue; }

				foreach (var t in types)
				{
					if (!t.IsValueType || t.IsEnum)
						continue;
					var attr = t.GetCustomAttribute<LuaBridgeAttribute>();
					if (attr == null) continue;
					var luaName = !string.IsNullOrEmpty(attr.LuaName) ? attr.LuaName : ToSnakeCase(t.Name);
					targets.Add((luaName, t, attr.NeedAccessors, attr.NeedSetters));
				}

				// Assembly-level [LuaBridge(typeof(T), ...)]
				foreach (var attr in asm.GetCustomAttributes<LuaBridgeAttribute>())
				{
					if (attr.ComponentType == null)
						continue;
					var luaName = !string.IsNullOrEmpty(attr.LuaName) ? attr.LuaName : ToSnakeCase(attr.ComponentType.Name);
					targets.Add((luaName, attr.ComponentType, attr.NeedAccessors, attr.NeedSetters));
				}
			}

			targets.Sort((a, b) => string.Compare(a.luaName, b.luaName, StringComparison.Ordinal));

			foreach (var (luaName, type, needAccessors, needSetters) in targets)
			{
				// Emit @class with all fields
				var className = type.Name;
				var docs = GetComponentDocs(type);

				if (docs != null && docs.TryGetValue("", out var classDesc))
					sb.AppendLine($"---{classDesc}");

				sb.AppendLine($"---@class {className}");
				var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
				foreach (var field in fields)
				{
					var luaType = MapType(field.FieldType);
					if (luaType == null) continue;
					var fieldLuaName = ToSnakeCase(field.Name);
					if (docs != null && docs.TryGetValue(fieldLuaName, out var fieldDesc))
						sb.AppendLine($"---@field {fieldLuaName} {luaType} {fieldDesc}");
					else
						sb.AppendLine($"---@field {fieldLuaName} {luaType}");
				}
				sb.AppendLine();

				sb.AppendLine($"{luaName} = {{}}");
				sb.AppendLine();

				if (needAccessors)
				{
					sb.AppendLine($"---Get component data for entity.");
					sb.AppendLine("---@param eid entity");
					sb.AppendLine($"---@return {className}?");
					sb.AppendLine($"function {luaName}.get(eid) end");
					sb.AppendLine();
				}

				if (needSetters)
				{
					sb.AppendLine($"---Set component data for entity.");
					sb.AppendLine("---@param eid entity");
					sb.AppendLine($"---@param value {className}");
					sb.AppendLine($"function {luaName}.set(eid, value) end");
					sb.AppendLine();
				}
			}
		}

		static void GenerateBuiltinBridges(StringBuilder sb)
		{
			sb.AppendLine("-- ── Built-in Bridges (auto-discovered) ───────────────────");
			sb.AppendLine();

			// Collect all stubs from [LuaCompile]-generated LuaCompiledStubs
			var stubs = new List<(string table, string function, string signature, string description)>();

			var compiledStubsType = Type.GetType("LuaECS.Generated.LuaCompiledStubs, LuaECS");
			if (compiledStubsType != null)
			{
				var field = compiledStubsType.GetField("Stubs", BindingFlags.Public | BindingFlags.Static);
				if (field?.GetValue(null) is (string table, string function, string signature, string description)[] compiledStubs)
				{
					foreach (var s in compiledStubs)
					{
						stubs.Add(s);
					}
				}
			}

			var grouped = stubs
				.GroupBy(s => s.table)
				.OrderBy(g => g.Key)
				.ToList();

			foreach (var group in grouped)
			{
				var tableName = group.Key;
				var isBuiltinTable = tableName == "math";

				if (!isBuiltinTable)
				{
					sb.AppendLine($"{tableName} = {{}}");
					sb.AppendLine();
				}

				foreach (var stub in group.OrderBy(s => s.function))
				{
					EmitFunction(sb, tableName, stub.function, stub.signature, stub.description);
				}
			}
		}

		static void EmitFunction(StringBuilder sb, string tableName, string function, string signature, string description)
		{
			var sig = signature;

			if (!sig.StartsWith("fun("))
				return;

			if (!string.IsNullOrEmpty(description))
				sb.AppendLine($"---{description}");

			var inner = sig.Substring(4);

			var closeParen = inner.IndexOf(')');
			if (closeParen < 0)
				return;

			var paramsPart = inner.Substring(0, closeParen);
			var afterParen = inner.Substring(closeParen + 1).TrimStart();
			string returnsPart = null;
			if (afterParen.StartsWith(":"))
				returnsPart = afterParen.Substring(1).TrimStart();

			var paramNames = new List<string>();
			if (!string.IsNullOrWhiteSpace(paramsPart))
			{
				var paramEntries = SplitParams(paramsPart);
				foreach (var entry in paramEntries)
				{
					var trimmed = entry.Trim();
					var colonIdx = trimmed.IndexOf(':');
					if (colonIdx < 0) continue;

					var name = trimmed.Substring(0, colonIdx).TrimEnd();
					var type = trimmed.Substring(colonIdx + 1).TrimStart();

					var isOptional = name.EndsWith("?");
					if (isOptional)
						name = name.TrimEnd('?');

					if (name == "...")
					{
						sb.AppendLine($"---@param ... {type}");
						paramNames.Add("...");
					}
					else if (isOptional)
					{
						sb.AppendLine($"---@param {name}? {type}");
						paramNames.Add(name);
					}
					else
					{
						sb.AppendLine($"---@param {name} {type}");
						paramNames.Add(name);
					}
				}
			}

			if (!string.IsNullOrEmpty(returnsPart))
				sb.AppendLine($"---@return {returnsPart}");

			var paramList = string.Join(", ", paramNames);
			sb.AppendLine($"function {tableName}.{function}({paramList}) end");
			sb.AppendLine();
		}

		static List<string> SplitParams(string paramsPart)
		{
			var result = new List<string>();
			var depth = 0;
			var start = 0;

			for (var i = 0; i < paramsPart.Length; i++)
			{
				var c = paramsPart[i];
				if (c == '(' || c == '{' || c == '<')
					depth++;
				else if (c == ')' || c == '}' || c == '>')
					depth--;
				else if (c == ',' && depth == 0)
				{
					result.Add(paramsPart.Substring(start, i - start));
					start = i + 1;
				}
			}

			result.Add(paramsPart.Substring(start));
			return result;
		}

		static void GenerateLifecycleCallbacks(StringBuilder sb)
		{
			sb.AppendLine("-- ── ECS Script Lifecycle ─────────────────────────────────");
			sb.AppendLine();
			sb.AppendLine("---Called once when the script is first attached to an entity.");
			sb.AppendLine("---@param entity entity");
			sb.AppendLine("---@param state table");
			sb.AppendLine("function OnInit(entity, state) end");
			sb.AppendLine();
			sb.AppendLine("---Called every tick (rate depends on script config).");
			sb.AppendLine("---@param dt number delta time in seconds");
			sb.AppendLine("function OnUpdate(dt) end");
			sb.AppendLine();
			sb.AppendLine("---Called before the entity is destroyed.");
			sb.AppendLine("---@param entity entity");
			sb.AppendLine("---@param state table");
			sb.AppendLine("function OnDestroy(entity, state) end");
			sb.AppendLine();
			sb.AppendLine("---Called when a command is sent to the entity.");
			sb.AppendLine("---@param entity entity");
			sb.AppendLine("---@param state table");
			sb.AppendLine("---@param cmd string");
			sb.AppendLine("function OnCommand(entity, state, cmd) end");
		}

		static string MapType(Type type)
		{
			if (TypeMap.TryGetValue(type.FullName ?? "", out var luaType))
				return luaType;
			if (type.IsEnum && s_enumLuaNames.TryGetValue(type, out var enumAlias))
				return enumAlias;
			return null;
		}

		static string ToSnakeCase(string input)
		{
			if (string.IsNullOrEmpty(input)) return input;
			var sb = new StringBuilder();
			for (var i = 0; i < input.Length; i++)
			{
				var c = input[i];
				if (char.IsUpper(c))
				{
					if (i > 0 && !char.IsUpper(input[i - 1]))
						sb.Append('_');
					else if (i > 0 && i < input.Length - 1 && char.IsUpper(input[i - 1]) &&
					         char.IsLower(input[i + 1]))
						sb.Append('_');
					sb.Append(char.ToLowerInvariant(c));
				}
				else
				{
					sb.Append(c);
				}
			}

			return sb.ToString();
		}

		static string ToScreamingSnakeCase(string input)
		{
			return ToSnakeCase(input).ToUpperInvariant();
		}
	}
}
