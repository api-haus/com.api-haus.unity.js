using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Unity.Logging;
using UnityEditor;
using UnityEngine;
using UnityJS.Entities.Core;

namespace UnityJS.Editor
{
  public static class JsTypeStubGenerator
  {
    static string ProjectRoot => Directory.GetParent(Application.dataPath)!.FullName;
    static string TypesDir => Path.Combine(ProjectRoot, "Library/unity.js/types");
    static string OutputPath => Path.Combine(TypesDir, "unity.d.ts");
    static string TsconfigPath => Path.Combine(ProjectRoot, "tsconfig.json");

    static readonly Dictionary<string, string> TypeMap = new()
    {
      { "System.Single", "number" },
      { "System.Int32", "number" },
      { "System.Boolean", "boolean" },
      { "Unity.Mathematics.float2", "float2" },
      { "Unity.Mathematics.float3", "float3" },
      { "Unity.Mathematics.float4", "float4" },
      { "Unity.Mathematics.quaternion", "quaternion" },
    };

    [InitializeOnLoadMethod]
    static void OnDomainReload()
    {
      EnsureTypesDirectory();
      CopyHandwrittenTypes();
      GenerateStubs();
      GenerateTsconfig();
    }

    [MenuItem("Tools/JS/Generate Type Stubs")]
    public static void Generate()
    {
      EnsureTypesDirectory();
      CopyHandwrittenTypes();
      var content = GenerateContent();
      WriteFile(OutputPath, content);
      GenerateTsconfig();
      Log.Debug("[JsTypeStubs] Regenerated all type infrastructure");
    }

    static void EnsureTypesDirectory()
    {
      if (!Directory.Exists(TypesDir))
        Directory.CreateDirectory(TypesDir);
    }

    static void GenerateStubs()
    {
      var content = GenerateContent();
      WriteFileIfChanged(OutputPath, content);
    }

    // ── Hand-written type copying ──

    static void CopyHandwrittenTypes()
    {
      var packagePath = GetPackagePath();
      if (packagePath == null)
      {
        Log.Warning("[JsTypeStubs] Could not resolve unity.js package path");
        return;
      }

      var typeDefsDir = Path.Combine(packagePath, "TypeDefinitions~");
      if (!Directory.Exists(typeDefsDir))
      {
        Log.Warning("[JsTypeStubs] TypeDefinitions~ not found at {0}", typeDefsDir);
        return;
      }

      CopyFileIfChanged(Path.Combine(typeDefsDir, "globals.d.ts"), Path.Combine(TypesDir, "globals.d.ts"));
      CopyFileIfChanged(Path.Combine(typeDefsDir, "modules.d.ts"), Path.Combine(TypesDir, "modules.d.ts"));
    }

    static void CopyFileIfChanged(string src, string dst)
    {
      if (!File.Exists(src))
        return;
      var srcContent = File.ReadAllText(src);
      if (File.Exists(dst) && ComputeHash(File.ReadAllText(dst)) == ComputeHash(srcContent))
        return;
      File.WriteAllText(dst, srcContent);
    }

    // ── tsconfig.json generation ──

    static void GenerateTsconfig()
    {
      var packageIncludePath = GetPackageIncludePath();
      var sb = new StringBuilder();
      sb.AppendLine("{");
      sb.AppendLine("  \"compilerOptions\": {");
      sb.AppendLine("    \"strict\": true,");
      sb.AppendLine("    \"strictPropertyInitialization\": false,");
      sb.AppendLine("    \"target\": \"ES2020\",");
      sb.AppendLine("    \"module\": \"ES2020\",");
      sb.AppendLine("    \"moduleResolution\": \"node\",");
      sb.AppendLine("    \"rootDir\": \".\",");
      sb.AppendLine("    \"outDir\": \"Library/TscBuild\",");
      sb.AppendLine("    \"declaration\": false,");
      sb.AppendLine("    \"sourceMap\": false,");
      sb.AppendLine("    \"skipLibCheck\": true,");
      sb.AppendLine("    \"types\": [],");
      sb.AppendLine("    \"lib\": [\"ES2020\"]");
      sb.AppendLine("  },");
      sb.AppendLine("  \"include\": [");
      sb.AppendLine("    \"Library/unity.js/types/*.d.ts\",");
      sb.AppendLine("    \"Assets/StreamingAssets/unity.js/**/*.ts\"");

      if (packageIncludePath != null)
      {
        // Include fixture and test .ts files from the package
        var escaped = packageIncludePath.Replace("\\", "/");
        sb.AppendLine($"    ,\"{escaped}/Integrations/**/Fixtures~/**/*.ts\"");
        sb.AppendLine($"    ,\"{escaped}/tests~/**/*.ts\"");
      }

      sb.AppendLine("  ]");
      sb.AppendLine("}");

      WriteFileIfChanged(TsconfigPath, sb.ToString());
    }

    // ── Path resolution ──

    static string GetPackagePath()
    {
      var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
        typeof(JsTypeStubGenerator).Assembly
      );
      if (info?.resolvedPath != null)
        return info.resolvedPath;

      // Fallback: check common package locations
      var projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
      var localPath = Path.Combine(projectRoot, "Packages/com.api-haus.unity.js");
      if (Directory.Exists(localPath))
        return Path.GetFullPath(localPath);

      // Check PackageCache
      var cacheDir = Path.Combine(projectRoot, "Library/PackageCache");
      if (Directory.Exists(cacheDir))
        foreach (var dir in Directory.GetDirectories(cacheDir, "com.api-haus.unity.js*"))
          return dir;

      return null;
    }

    static string GetPackageIncludePath()
    {
      var resolved = GetPackagePath();
      if (resolved == null)
        return null;

      var projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
      // Normalize separators for comparison
      var normalizedResolved = resolved.Replace("\\", "/");
      var normalizedRoot = projectRoot.Replace("\\", "/");

      if (normalizedResolved.StartsWith(normalizedRoot))
        return normalizedResolved.Substring(normalizedRoot.Length + 1);

      return resolved;
    }

    // ── File utilities ──

    static void WriteFileIfChanged(string path, string content)
    {
      if (File.Exists(path) && ComputeHash(File.ReadAllText(path)) == ComputeHash(content))
        return;
      WriteFile(path, content);
    }

    static void WriteFile(string path, string content)
    {
      var dir = Path.GetDirectoryName(path);
      if (dir != null && !Directory.Exists(dir))
        Directory.CreateDirectory(dir);
      File.WriteAllText(path, content);
      Log.Debug("[JsTypeStubs] Wrote {0}", path);
    }

    static string ComputeHash(string text)
    {
      using var sha = SHA256.Create();
      var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
      return Convert.ToBase64String(bytes);
    }

    // ── Content generation ──

    internal static string GenerateContent()
    {
      var sb = new StringBuilder();
      sb.AppendLine("// <auto-generated>");
      sb.AppendLine("// DO NOT EDIT — this file is generated by JsTypeStubGenerator.cs");
      sb.AppendLine("// Regenerate via: Tools > JS > Generate Type Stubs");
      sb.AppendLine("// </auto-generated>");
      sb.AppendLine();

      GenerateEnums(sb);
      GenerateBridgedComponents(sb);

      return sb.ToString();
    }

    static readonly Dictionary<Type, string> s_enumNames = new();

    static void GenerateEnums(StringBuilder sb)
    {
      s_enumNames.Clear();

      var enums = new List<(string jsName, Type type)>();

      foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
      {
        Type[] types;
        try
        {
          types = asm.GetTypes();
        }
        catch
        {
          continue;
        }

        foreach (var t in types)
        {
          if (!t.IsEnum)
            continue;
          var attr = t.GetCustomAttribute<JsBridgeAttribute>();
          if (attr == null)
            continue;
          var jsName = !string.IsNullOrEmpty(attr.JsName) ? attr.JsName : t.Name;
          enums.Add((jsName, t));
          s_enumNames[t] = t.Name;
        }
      }

      enums.Sort((a, b) => string.Compare(a.jsName, b.jsName, StringComparison.Ordinal));

      sb.AppendLine("// ── Enum Type Aliases ────────────────────────────────────");
      sb.AppendLine();
      foreach (var (jsName, type) in enums)
      {
        var enumValues = new List<string>();
        foreach (var name in Enum.GetNames(type))
          enumValues.Add(Convert.ToInt32(Enum.Parse(type, name)).ToString());
        var typeName = s_enumNames[type];
        sb.AppendLine($"type {typeName} = {string.Join(" | ", enumValues)};");
        sb.AppendLine();
      }

      s_enumList = enums;
    }

    static List<(string jsName, Type type)> s_enumList;

    static Dictionary<string, string> GetComponentDocs(Type componentType)
    {
      var typeName = $"UnityJS.Entities.Generated.Js{componentType.Name}Bridge";
      foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
      {
        var docsType = asm.GetType(typeName);
        if (docsType == null)
          continue;
        var field = docsType.GetField("Descriptions", BindingFlags.Public | BindingFlags.Static);
        return field?.GetValue(null) as Dictionary<string, string>;
      }

      return null;
    }

    static Dictionary<string, string> GetEnumDocs(Type enumType)
    {
      var typeName = $"UnityJS.Entities.Generated.Js{enumType.Name}Enum";
      foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
      {
        var docsType = asm.GetType(typeName);
        if (docsType == null)
          continue;
        var field = docsType.GetField("Descriptions", BindingFlags.Public | BindingFlags.Static);
        return field?.GetValue(null) as Dictionary<string, string>;
      }

      return null;
    }

    static void GenerateBridgedComponents(StringBuilder sb)
    {
      sb.AppendLine("// ── Component Interfaces (ambient) ──────────────────────");
      sb.AppendLine();

      var targets = new List<(string jsName, Type type, bool needAccessors, bool needSetters)>();

      foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
      {
        Type[] types;
        try
        {
          types = asm.GetTypes();
        }
        catch
        {
          continue;
        }

        foreach (var t in types)
        {
          if (!t.IsValueType || t.IsEnum)
            continue;
          var attr = t.GetCustomAttribute<JsBridgeAttribute>();
          if (attr == null)
            continue;
          var jsName = !string.IsNullOrEmpty(attr.JsName) ? attr.JsName : t.Name;
          targets.Add((jsName, t, attr.NeedAccessors, attr.NeedSetters));
        }

        foreach (var attr in asm.GetCustomAttributes<JsBridgeAttribute>())
        {
          if (attr.ComponentType == null)
            continue;
          var jsName = !string.IsNullOrEmpty(attr.JsName) ? attr.JsName : attr.ComponentType.Name;
          targets.Add((jsName, attr.ComponentType, attr.NeedAccessors, attr.NeedSetters));
        }
      }

      targets.Sort((a, b) => string.Compare(a.jsName, b.jsName, StringComparison.Ordinal));

      foreach (var (jsName, type, needAccessors, needSetters) in targets)
      {
        var className = type.Name;
        var docs = GetComponentDocs(type);

        if (docs != null && docs.TryGetValue("", out var classDesc))
          sb.AppendLine($"/** {classDesc} */");

        sb.AppendLine($"interface {className} {{");
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
        foreach (var field in fields)
        {
          var tsType = MapType(field.FieldType);
          if (tsType == null)
            continue;
          var fieldJsName = field.Name;
          if (docs != null && docs.TryGetValue(fieldJsName, out var fieldDesc))
            sb.AppendLine($"  /** {fieldDesc} */");
          sb.AppendLine($"  {fieldJsName}: {tsType};");
        }

        sb.AppendLine("}");
        sb.AppendLine();
      }

      sb.AppendLine("// ── Module: unity.js/components ─────────────────────────");
      sb.AppendLine();
      sb.AppendLine("declare module 'unity.js/components' {");

      if (s_enumList != null)
      {
        foreach (var (jsName, type) in s_enumList)
        {
          var enumDocs = GetEnumDocs(type);

          if (enumDocs != null && enumDocs.TryGetValue("", out var enumDesc))
            sb.AppendLine($"  /** {enumDesc} */");

          sb.AppendLine($"  export const {jsName}: {{");
          foreach (var name in Enum.GetNames(type))
          {
            var val = Convert.ToInt32(Enum.Parse(type, name));
            if (enumDocs != null && enumDocs.TryGetValue(name, out var memberDesc))
              sb.AppendLine($"    /** {memberDesc} */");
            sb.AppendLine($"    readonly {name}: {val};");
          }

          sb.AppendLine("  };");
          sb.AppendLine();
        }
      }

      foreach (var (jsName, type, needAccessors, needSetters) in targets)
      {
        var className = type.Name;
        var isComponent = typeof(Unity.Entities.IComponentData).IsAssignableFrom(type);
        if (!isComponent)
          continue;

        if (needSetters)
        {
          sb.AppendLine($"  export const {jsName}: ComponentAccessor<{className}>;");
        }
        else if (needAccessors)
        {
          sb.AppendLine($"  export const {jsName}: ReadonlyComponentAccessor<{className}>;");
        }
      }

      sb.AppendLine("}");
      sb.AppendLine();
    }

    static string MapType(Type type)
    {
      if (TypeMap.TryGetValue(type.FullName ?? "", out var tsType))
        return tsType;
      if (type.IsEnum && s_enumNames.TryGetValue(type, out _))
        return type.Name;
      return null;
    }
  }
}
