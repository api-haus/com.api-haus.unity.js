using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace JsGameCodegen
{
  [Generator]
  public class JsBridgeGenerator : IIncrementalGenerator
  {
    const string JsBridgeAttrShort = "JsBridge";
    const string JsBridgeAttrFull = "UnityJS.Entities.Core.JsBridgeAttribute";
    const string IComponentDataFull = "Unity.Entities.IComponentData";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
      // [JsBridge] on structs
      var structTargets = context
        .SyntaxProvider.CreateSyntaxProvider(
          predicate: static (node, _) => IsStructWithJsBridgeAttr(node),
          transform: static (ctx, ct) => ExtractFromStruct(ctx, ct)
        )
        .Where(static x => x != null)
        .Select(static (x, _) => x.Value);

      context.RegisterSourceOutput(structTargets, GenerateBridge);

      // [assembly: JsBridge(typeof(T), ...)] on assembly
      var assemblyTargets = context
        .SyntaxProvider.CreateSyntaxProvider(
          predicate: static (node, _) => IsAssemblyJsBridgeAttr(node),
          transform: static (ctx, ct) => ExtractFromAssemblyAttribute(ctx, ct)
        )
        .Where(static x => x != null)
        .Select(static (x, _) => x.Value);

      context.RegisterSourceOutput(assemblyTargets, GenerateBridge);

      // [JsBridge] on enums
      var enumTargets = context
        .SyntaxProvider.CreateSyntaxProvider(
          predicate: static (node, _) => IsEnumWithJsBridgeAttr(node),
          transform: static (ctx, ct) => ExtractEnum(ctx, ct)
        )
        .Where(static x => x != null)
        .Select(static (x, _) => x.Value);

      context.RegisterSourceOutput(enumTargets, GenerateEnum);
    }

    // -- Syntax predicates ---------------------------------------------------

    static bool IsStructWithJsBridgeAttr(SyntaxNode node)
    {
      if (node is not StructDeclarationSyntax s)
        return false;
      return HasJsBridgeAttribute(s.AttributeLists);
    }

    static bool IsEnumWithJsBridgeAttr(SyntaxNode node)
    {
      if (node is not EnumDeclarationSyntax e)
        return false;
      return HasJsBridgeAttribute(e.AttributeLists);
    }

    static bool IsAssemblyJsBridgeAttr(SyntaxNode node)
    {
      if (node is not AttributeSyntax attr)
        return false;
      if (attr.Parent is not AttributeListSyntax attrList)
        return false;
      if (attrList.Target == null || attrList.Target.Identifier.Text != "assembly")
        return false;
      return IsJsBridgeAttrName(attr.Name.ToString());
    }

    static bool HasJsBridgeAttribute(SyntaxList<AttributeListSyntax> attrLists)
    {
      foreach (var attrList in attrLists)
      {
        foreach (var attr in attrList.Attributes)
        {
          if (IsJsBridgeAttrName(attr.Name.ToString()))
            return true;
        }
      }
      return false;
    }

    static bool IsJsBridgeAttrName(string name)
    {
      return name == JsBridgeAttrShort
        || name == "JsBridgeAttribute"
        || name.EndsWith(".JsBridge")
        || name.EndsWith(".JsBridgeAttribute");
    }

    // -- Semantic transforms (struct/assembly bridge) ------------------------

    static BridgeTarget? ExtractFromStruct(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
      var structSyntax = (StructDeclarationSyntax)ctx.Node;
      var symbol = ctx.SemanticModel.GetDeclaredSymbol(structSyntax, ct);
      if (symbol == null || !ImplementsIComponentData(symbol))
        return null;

      foreach (var attrData in symbol.GetAttributes())
      {
        if (attrData.AttributeClass?.ToDisplayString() != JsBridgeAttrFull)
          continue;

        var jsName = (string)null;
        var needAccessors = true;
        var needSetters = true;

        if (attrData.ConstructorArguments.Length > 0)
          jsName = attrData.ConstructorArguments[0].Value as string;

        foreach (var named in attrData.NamedArguments)
        {
          if (named.Key == "NeedAccessors" && named.Value.Value is bool na)
            needAccessors = na;
          else if (named.Key == "NeedSetters" && named.Value.Value is bool ns)
            needSetters = ns;
        }

        if (string.IsNullOrEmpty(jsName))
          jsName = symbol.Name;

        var fields = GetSupportedFields(symbol, needAccessors, needSetters, ct);
        if (fields.Length == 0)
          return null;

        var desc = DocExtractor.GetSummary(symbol, ct);
        return new BridgeTarget(
          symbol.Name,
          symbol.ToDisplayString(),
          jsName,
          needAccessors,
          needSetters,
          fields,
          desc
        );
      }

      return null;
    }

    static BridgeTarget? ExtractFromAssemblyAttribute(
      GeneratorSyntaxContext ctx,
      CancellationToken ct
    )
    {
      var attrSyntax = (AttributeSyntax)ctx.Node;

      var attrSymbol = ctx.SemanticModel.GetSymbolInfo(attrSyntax, ct).Symbol;
      if (attrSymbol == null)
        return null;

      var containingType = attrSymbol.ContainingType?.ToDisplayString();
      if (containingType != JsBridgeAttrFull)
        return null;

      // Get attribute arguments from syntax since we can't easily get AttributeData here
      var args = attrSyntax.ArgumentList?.Arguments;
      if (args == null || args.Value.Count == 0)
        return null;

      // First arg should be typeof(T)
      if (args.Value[0].Expression is not TypeOfExpressionSyntax typeOfExpr)
        return null;

      var typeInfo = ctx.SemanticModel.GetTypeInfo(typeOfExpr.Type, ct);
      if (typeInfo.Type is not INamedTypeSymbol targetType)
        return null;

      if (!ImplementsIComponentData(targetType))
        return null;

      string jsName = null;
      var needAccessors = true;
      var needSetters = true;

      // Parse remaining args (positional or named)
      for (var i = 1; i < args.Value.Count; i++)
      {
        var arg = args.Value[i];
        var constVal = ctx.SemanticModel.GetConstantValue(arg.Expression, ct);
        if (!constVal.HasValue)
          continue;

        if (arg.NameEquals != null)
        {
          var propName = arg.NameEquals.Name.Identifier.Text;
          if (propName == "jsName" && constVal.Value is string s)
            jsName = s;
          else if (propName == "NeedAccessors" && constVal.Value is bool na)
            needAccessors = na;
          else if (propName == "NeedSetters" && constVal.Value is bool ns)
            needSetters = ns;
        }
        else
        {
          // positional
          if (i == 1 && constVal.Value is string s)
            jsName = s;
        }
      }

      if (string.IsNullOrEmpty(jsName))
        jsName = targetType.Name;

      var fields = GetSupportedFields(targetType, needAccessors, needSetters, ct);
      if (fields.Length == 0)
        return null;

      var desc = DocExtractor.GetSummary(targetType, ct);
      return new BridgeTarget(
        targetType.Name,
        targetType.ToDisplayString(),
        jsName,
        needAccessors,
        needSetters,
        fields,
        desc
      );
    }

    // -- Semantic transform (enum) -------------------------------------------

    static EnumTarget? ExtractEnum(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
      var enumSyntax = (EnumDeclarationSyntax)ctx.Node;
      if (ctx.SemanticModel.GetDeclaredSymbol(enumSyntax, ct) is not INamedTypeSymbol symbol)
        return null;

      foreach (var attrData in symbol.GetAttributes())
      {
        if (attrData.AttributeClass?.ToDisplayString() != JsBridgeAttrFull)
          continue;

        string jsName = null;
        if (attrData.ConstructorArguments.Length > 0)
          jsName = attrData.ConstructorArguments[0].Value as string;

        if (string.IsNullOrEmpty(jsName))
          jsName = SnakeCaseHelper.ToScreamingSnakeCase(symbol.Name);

        var enumDesc = DocExtractor.GetSummary(symbol, ct);

        var members = ImmutableArray.CreateBuilder<EnumMember>();
        foreach (var member in symbol.GetMembers())
        {
          if (member is IFieldSymbol field && field.HasConstantValue)
          {
            var memberDesc = DocExtractor.GetSummary(field, ct);
            members.Add(new EnumMember(field.Name, (int)field.ConstantValue, memberDesc));
          }
        }

        return new EnumTarget(symbol.Name, jsName, members.ToImmutable(), enumDesc);
      }

      return null;
    }

    // -- Helpers -------------------------------------------------------------

    static bool ImplementsIComponentData(INamedTypeSymbol symbol)
    {
      foreach (var iface in symbol.AllInterfaces)
      {
        if (iface.ToDisplayString() == IComponentDataFull)
          return true;
      }
      return false;
    }

    static ImmutableArray<FieldInfo> GetSupportedFields(
      INamedTypeSymbol type,
      bool needAccessors,
      bool needSetters,
      CancellationToken ct = default
    )
    {
      var builder = ImmutableArray.CreateBuilder<FieldInfo>();
      foreach (var member in type.GetMembers())
      {
        if (!(member is IFieldSymbol field))
          continue;
        if (field.IsStatic || field.IsConst || field.DeclaredAccessibility != Accessibility.Public)
          continue;
        var jsType = MapFieldType(field.Type);
        if (jsType == JsFieldType.Unsupported)
          continue;
        string enumTypeName = null;
        if (jsType == JsFieldType.Enum)
          enumTypeName = field.Type.ToDisplayString();
        var desc = DocExtractor.GetSummary(field, ct);
        builder.Add(
          new FieldInfo(
            field.Name,
            field.Name,
            jsType,
            needAccessors,
            needSetters,
            enumTypeName,
            desc
          )
        );
      }
      return builder.ToImmutable();
    }

    static JsFieldType MapFieldType(ITypeSymbol type)
    {
      var name = type.ToDisplayString();
      switch (name)
      {
        case "float":
          return JsFieldType.Float;
        case "int":
          return JsFieldType.Int;
        case "bool":
          return JsFieldType.Bool;
        case "Unity.Mathematics.float2":
          return JsFieldType.Float2;
        case "Unity.Mathematics.float3":
          return JsFieldType.Float3;
        case "Unity.Mathematics.float4":
          return JsFieldType.Float4;
        case "Unity.Mathematics.quaternion":
          return JsFieldType.Quaternion;
        default:
          if (type.TypeKind == TypeKind.Enum)
            return JsFieldType.Enum;
          return JsFieldType.Unsupported;
      }
    }

    static string EscapeString(string s)
    {
      return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    /// <summary>
    /// Emit a C# static byte array literal: static readonly byte[] s_name = { (byte)'c', ..., 0 };
    /// </summary>
    static void EmitByteArray(StringBuilder sb, string varName, string value)
    {
      sb.Append("\t\tstatic readonly byte[] " + varName + " = { ");
      for (var i = 0; i < value.Length; i++)
      {
        if (i > 0)
          sb.Append(", ");
        sb.Append("(byte)'" + value[i] + "'");
      }
      sb.Append(", 0 };");
      sb.AppendLine();
    }

    // -- Code generation (struct/assembly bridge) ----------------------------

    static void GenerateBridge(SourceProductionContext ctx, BridgeTarget target)
    {
      var sb = new StringBuilder();
      var typeName = target.TypeName;
      var fullTypeName = target.FullTypeName;
      var jsName = target.JsName;
      var needAccessors = target.NeedAccessors;
      var needSetters = target.NeedSetters;
      var fields = target.Fields;
      var className = "Js" + typeName + "Bridge";

      sb.AppendLine("// <auto-generated/>");
      sb.AppendLine("namespace UnityJS.Entities.Generated");
      sb.AppendLine("{");
      sb.AppendLine("\tusing AOT;");
      sb.AppendLine("\tusing Core;");
      sb.AppendLine("\tusing UnityJS.QJS;");
      sb.AppendLine("\tusing Unity.Burst;");
      sb.AppendLine("\tusing Unity.Entities;");
      sb.AppendLine("\tusing Unity.Mathematics;");
      sb.AppendLine("\tusing UnityJS.Runtime;");
      sb.AppendLine();

      sb.AppendLine("\tpublic static class " + className);
      sb.AppendLine("\t{");
      sb.AppendLine("\t\tstruct LookupMarker_" + typeName + " { }");
      sb.AppendLine();
      sb.AppendLine(
        "\t\tstatic readonly SharedStatic<ComponentLookup<" + fullTypeName + ">> s_lookup ="
      );
      sb.AppendLine(
        "\t\t\tSharedStatic<ComponentLookup<"
          + fullTypeName
          + ">>.GetOrCreate<LookupMarker_"
          + typeName
          + ", ComponentLookup<"
          + fullTypeName
          + ">>();"
      );
      sb.AppendLine();

      // Static byte arrays for property names
      foreach (var field in fields)
        EmitByteArray(sb, "s_" + field.JsName, field.JsName);
      // Also emit byte arrays for compound field sub-properties
      var needsXY = fields.Any(f =>
        f.Type == JsFieldType.Float2
        || f.Type == JsFieldType.Float3
        || f.Type == JsFieldType.Float4
        || f.Type == JsFieldType.Quaternion
      );
      var needsZ = fields.Any(f =>
        f.Type == JsFieldType.Float3
        || f.Type == JsFieldType.Float4
        || f.Type == JsFieldType.Quaternion
      );
      var needsW = fields.Any(f =>
        f.Type == JsFieldType.Float4 || f.Type == JsFieldType.Quaternion
      );
      if (needsXY)
      {
        EmitByteArray(sb, "s_x", "x");
        EmitByteArray(sb, "s_y", "y");
      }
      if (needsZ)
        EmitByteArray(sb, "s_z", "z");
      if (needsW)
        EmitByteArray(sb, "s_w", "w");

      // Byte arrays for "get" and "set" function names
      if (needAccessors)
        EmitByteArray(sb, "s_get", "get");
      if (needSetters)
        EmitByteArray(sb, "s_set", "set");
      EmitByteArray(sb, "s___name", "__name");
      sb.AppendLine();

      // Auto-register (deferred — TypeManager may not be initialized yet at AfterAssembliesLoaded)
      sb.AppendLine("#if UNITY_EDITOR");
      sb.AppendLine("\t\t[UnityEditor.InitializeOnLoadMethod]");
      sb.AppendLine("#endif");
      sb.AppendLine(
        "\t\t[UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.AfterAssembliesLoaded)]"
      );
      sb.AppendLine("\t\tstatic void AutoRegister()");
      sb.AppendLine("\t\t{");
      sb.AppendLine("\t\t\tJsComponentRegistry.RegisterBridgeDeferred(");
      sb.AppendLine("\t\t\t\t\"" + jsName + "\",");
      sb.AppendLine("\t\t\t\t() => ComponentType.ReadWrite<" + fullTypeName + ">(),");
      sb.AppendLine("\t\t\t\tRegister,");
      sb.AppendLine("\t\t\t\tUpdateLookup");
      sb.AppendLine("\t\t\t);");
      sb.AppendLine("\t\t}");
      sb.AppendLine();

      // Register
      sb.AppendLine("\t\tpublic static unsafe void Register(JSContext ctx)");
      sb.AppendLine("\t\t{");
      sb.AppendLine("\t\t\tvar ns = QJS.JS_NewObject(ctx);");
      if (needAccessors)
      {
        sb.AppendLine("\t\t\tfixed (byte* pGet = s_get)");
        sb.AppendLine("\t\t\t{");
        sb.AppendLine(
          "\t\t\t\tvar fn = QJSShim.qjs_shim_new_function(ctx, Get_Component, pGet, 1);"
        );
        sb.AppendLine("\t\t\t\tQJS.JS_SetPropertyStr(ctx, ns, pGet, fn);");
        sb.AppendLine("\t\t\t}");
      }
      if (needSetters)
      {
        sb.AppendLine("\t\t\tfixed (byte* pSet = s_set)");
        sb.AppendLine("\t\t\t{");
        sb.AppendLine(
          "\t\t\t\tvar fn = QJSShim.qjs_shim_new_function(ctx, Set_Component, pSet, 2);"
        );
        sb.AppendLine("\t\t\t\tQJS.JS_SetPropertyStr(ctx, ns, pSet, fn);");
        sb.AppendLine("\t\t\t}");
      }
      sb.AppendLine("\t\t\tvar global = QJS.JS_GetGlobalObject(ctx);");
      sb.AppendLine(
        "\t\t\tvar pNameBytes = System.Text.Encoding.UTF8.GetBytes(\"" + jsName + "\\0\");"
      );
      sb.AppendLine("\t\t\tfixed (byte* pName = pNameBytes, pN = s___name)");
      sb.AppendLine("\t\t\t{");
      sb.AppendLine("\t\t\t\tQJS.JS_SetPropertyStr(ctx, ns, pN, QJS.JS_NewString(ctx, pName));");
      sb.AppendLine("\t\t\t\tQJS.JS_SetPropertyStr(ctx, global, pName, ns);");
      sb.AppendLine("\t\t\t}");
      sb.AppendLine("\t\t\tQJS.JS_FreeValue(ctx, global);");
      sb.AppendLine("\t\t}");
      sb.AppendLine();

      // UpdateLookup
      sb.AppendLine("\t\tpublic static void UpdateLookup(ref SystemState state)");
      sb.AppendLine("\t\t{");
      sb.AppendLine(
        "\t\t\ts_lookup.Data = state.GetComponentLookup<"
          + fullTypeName
          + ">("
          + (!needSetters ? "true" : "false")
          + ");"
      );
      sb.AppendLine("\t\t}");
      sb.AppendLine();

      // Get/Set component functions
      if (needAccessors)
        EmitGetComponent(sb, fields, fullTypeName);
      if (needSetters)
        EmitSetComponent(sb, fields, fullTypeName);

      // Emit Descriptions dictionary for editor stub generation
      var hasAnyDoc = target.Description != null || fields.Any(f => f.Description != null);
      if (hasAnyDoc)
      {
        sb.AppendLine(
          "\t\tpublic static readonly System.Collections.Generic.Dictionary<string, string> Descriptions = new()"
        );
        sb.AppendLine("\t\t{");
        if (target.Description != null)
          sb.AppendLine("\t\t\t{ \"\", \"" + EscapeString(target.Description) + "\" },");
        foreach (var field in fields)
        {
          if (field.Description != null)
            sb.AppendLine(
              "\t\t\t{ \""
                + EscapeString(field.JsName)
                + "\", \""
                + EscapeString(field.Description)
                + "\" },"
            );
        }
        sb.AppendLine("\t\t};");
      }

      sb.AppendLine("\t}");
      sb.AppendLine("}");

      ctx.AddSource(className + ".g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    static void EmitGetComponent(
      StringBuilder sb,
      ImmutableArray<FieldInfo> fields,
      string fullTypeName
    )
    {
      sb.AppendLine("\t\t[MonoPInvokeCallback(typeof(QJSShimCallback))]");
      sb.AppendLine(
        "\t\tstatic unsafe void Get_Component(JSContext ctx, long thisU, long thisTag,"
      );
      sb.AppendLine("\t\t\tint argc, JSValue* argv, long* outU, long* outTag)");
      sb.AppendLine("\t\t{");
      sb.AppendLine("\t\t\tint entityId;");
      sb.AppendLine("\t\t\tQJS.JS_ToInt32(ctx, &entityId, argv[0]);");
      sb.AppendLine("\t\t\tvar entity = JsECSBridge.GetEntityFromIdBurst(entityId);");
      sb.AppendLine("\t\t\tif (entity == Entity.Null || !s_lookup.Data.HasComponent(entity))");
      sb.AppendLine("\t\t\t{");
      sb.AppendLine("\t\t\t\tvar undef = QJS.JS_UNDEFINED;");
      sb.AppendLine("\t\t\t\t*outU = undef.u;");
      sb.AppendLine("\t\t\t\t*outTag = undef.tag;");
      sb.AppendLine("\t\t\t\treturn;");
      sb.AppendLine("\t\t\t}");
      sb.AppendLine("\t\t\tvar comp = s_lookup.Data[entity];");
      sb.AppendLine("\t\t\tvar obj = QJS.JS_NewObject(ctx);");

      foreach (var field in fields)
      {
        sb.AppendLine("\t\t\tfixed (byte* p_" + field.JsName + " = s_" + field.JsName + ")");
        switch (field.Type)
        {
          case JsFieldType.Float:
            sb.AppendLine(
              "\t\t\t\tQJS.JS_SetPropertyStr(ctx, obj, p_"
                + field.JsName
                + ", QJS.NewFloat64(ctx, comp."
                + field.Name
                + "));"
            );
            break;
          case JsFieldType.Int:
            sb.AppendLine(
              "\t\t\t\tQJS.JS_SetPropertyStr(ctx, obj, p_"
                + field.JsName
                + ", QJS.NewInt32(ctx, comp."
                + field.Name
                + "));"
            );
            break;
          case JsFieldType.Bool:
            sb.AppendLine(
              "\t\t\t\tQJS.JS_SetPropertyStr(ctx, obj, p_"
                + field.JsName
                + ", QJS.JS_NewBool(ctx, comp."
                + field.Name
                + " ? 1 : 0));"
            );
            break;
          case JsFieldType.Enum:
            sb.AppendLine(
              "\t\t\t\tQJS.JS_SetPropertyStr(ctx, obj, p_"
                + field.JsName
                + ", QJS.NewInt32(ctx, (int)comp."
                + field.Name
                + "));"
            );
            break;
          case JsFieldType.Float2:
            sb.AppendLine("\t\t\t{");
            sb.AppendLine(
              "\t\t\t\tvar sub = JsStateExtensions.Float2ToJsObject(ctx, comp." + field.Name + ");"
            );
            sb.AppendLine("\t\t\t\tQJS.JS_SetPropertyStr(ctx, obj, p_" + field.JsName + ", sub);");
            sb.AppendLine("\t\t\t}");
            break;
          case JsFieldType.Float3:
            sb.AppendLine("\t\t\t{");
            sb.AppendLine(
              "\t\t\t\tvar sub = JsStateExtensions.Float3ToJsObject(ctx, comp." + field.Name + ");"
            );
            sb.AppendLine("\t\t\t\tQJS.JS_SetPropertyStr(ctx, obj, p_" + field.JsName + ", sub);");
            sb.AppendLine("\t\t\t}");
            break;
          case JsFieldType.Float4:
            sb.AppendLine("\t\t\t{");
            sb.AppendLine(
              "\t\t\t\tvar sub = JsStateExtensions.Float4ToJsObject(ctx, comp." + field.Name + ");"
            );
            sb.AppendLine("\t\t\t\tQJS.JS_SetPropertyStr(ctx, obj, p_" + field.JsName + ", sub);");
            sb.AppendLine("\t\t\t}");
            break;
          case JsFieldType.Quaternion:
            sb.AppendLine("\t\t\t{");
            sb.AppendLine(
              "\t\t\t\tvar sub = JsStateExtensions.QuaternionToJsObject(ctx, comp." + field.Name + ");"
            );
            sb.AppendLine("\t\t\t\tQJS.JS_SetPropertyStr(ctx, obj, p_" + field.JsName + ", sub);");
            sb.AppendLine("\t\t\t}");
            break;
        }
      }

      sb.AppendLine("\t\t\t*outU = obj.u;");
      sb.AppendLine("\t\t\t*outTag = obj.tag;");
      sb.AppendLine("\t\t}");
      sb.AppendLine();
    }

    static void EmitSetComponent(
      StringBuilder sb,
      ImmutableArray<FieldInfo> fields,
      string fullTypeName
    )
    {
      sb.AppendLine("\t\t[MonoPInvokeCallback(typeof(QJSShimCallback))]");
      sb.AppendLine(
        "\t\tstatic unsafe void Set_Component(JSContext ctx, long thisU, long thisTag,"
      );
      sb.AppendLine("\t\t\tint argc, JSValue* argv, long* outU, long* outTag)");
      sb.AppendLine("\t\t{");
      sb.AppendLine("\t\t\tint entityId;");
      sb.AppendLine("\t\t\tQJS.JS_ToInt32(ctx, &entityId, argv[0]);");
      sb.AppendLine("\t\t\tvar entity = JsECSBridge.GetEntityFromIdBurst(entityId);");
      sb.AppendLine("\t\t\tif (entity == Entity.Null || !s_lookup.Data.HasComponent(entity))");
      sb.AppendLine("\t\t\t{");
      sb.AppendLine("\t\t\t\tvar undef = QJS.JS_UNDEFINED;");
      sb.AppendLine("\t\t\t\t*outU = undef.u;");
      sb.AppendLine("\t\t\t\t*outTag = undef.tag;");
      sb.AppendLine("\t\t\t\treturn;");
      sb.AppendLine("\t\t\t}");
      sb.AppendLine("\t\t\tvar comp = s_lookup.Data[entity];");
      sb.AppendLine("\t\t\tvar data = argv[1];");

      foreach (var field in fields)
      {
        sb.AppendLine("\t\t\tfixed (byte* p_" + field.JsName + " = s_" + field.JsName + ")");
        sb.AppendLine("\t\t\t{");

        switch (field.Type)
        {
          case JsFieldType.Float:
            sb.AppendLine(
              "\t\t\t\tvar v_"
                + field.JsName
                + " = QJS.JS_GetPropertyStr(ctx, data, p_"
                + field.JsName
                + ");"
            );
            sb.AppendLine(
              "\t\t\t\tdouble d_"
                + field.JsName
                + "; QJS.JS_ToFloat64(ctx, &d_"
                + field.JsName
                + ", v_"
                + field.JsName
                + ");"
            );
            sb.AppendLine("\t\t\t\tcomp." + field.Name + " = (float)d_" + field.JsName + ";");
            sb.AppendLine("\t\t\t\tQJS.JS_FreeValue(ctx, v_" + field.JsName + ");");
            break;
          case JsFieldType.Int:
            sb.AppendLine(
              "\t\t\t\tvar v_"
                + field.JsName
                + " = QJS.JS_GetPropertyStr(ctx, data, p_"
                + field.JsName
                + ");"
            );
            sb.AppendLine(
              "\t\t\t\tint i_"
                + field.JsName
                + "; QJS.JS_ToInt32(ctx, &i_"
                + field.JsName
                + ", v_"
                + field.JsName
                + ");"
            );
            sb.AppendLine("\t\t\t\tcomp." + field.Name + " = i_" + field.JsName + ";");
            sb.AppendLine("\t\t\t\tQJS.JS_FreeValue(ctx, v_" + field.JsName + ");");
            break;
          case JsFieldType.Bool:
            sb.AppendLine(
              "\t\t\t\tvar v_"
                + field.JsName
                + " = QJS.JS_GetPropertyStr(ctx, data, p_"
                + field.JsName
                + ");"
            );
            sb.AppendLine(
              "\t\t\t\tcomp." + field.Name + " = QJS.JS_ToBool(ctx, v_" + field.JsName + ") != 0;"
            );
            sb.AppendLine("\t\t\t\tQJS.JS_FreeValue(ctx, v_" + field.JsName + ");");
            break;
          case JsFieldType.Enum:
            sb.AppendLine(
              "\t\t\t\tvar v_"
                + field.JsName
                + " = QJS.JS_GetPropertyStr(ctx, data, p_"
                + field.JsName
                + ");"
            );
            sb.AppendLine(
              "\t\t\t\tint e_"
                + field.JsName
                + "; QJS.JS_ToInt32(ctx, &e_"
                + field.JsName
                + ", v_"
                + field.JsName
                + ");"
            );
            sb.AppendLine(
              "\t\t\t\tcomp."
                + field.Name
                + " = ("
                + field.EnumTypeName
                + ")e_"
                + field.JsName
                + ";"
            );
            sb.AppendLine("\t\t\t\tQJS.JS_FreeValue(ctx, v_" + field.JsName + ");");
            break;
          case JsFieldType.Float2:
            EmitReadCompound(sb, field, new[] { "x", "y" }, false);
            break;
          case JsFieldType.Float3:
            EmitReadCompound(sb, field, new[] { "x", "y", "z" }, false);
            break;
          case JsFieldType.Float4:
            EmitReadCompound(sb, field, new[] { "x", "y", "z", "w" }, false);
            break;
          case JsFieldType.Quaternion:
            EmitReadCompound(sb, field, new[] { "x", "y", "z", "w" }, true);
            break;
        }

        sb.AppendLine("\t\t\t}");
      }

      sb.AppendLine("\t\t\ts_lookup.Data[entity] = comp;");
      sb.AppendLine("\t\t\tvar ret = QJS.JS_UNDEFINED;");
      sb.AppendLine("\t\t\t*outU = ret.u;");
      sb.AppendLine("\t\t\t*outTag = ret.tag;");
      sb.AppendLine("\t\t}");
      sb.AppendLine();
    }

    static void EmitReadCompound(
      StringBuilder sb,
      FieldInfo field,
      string[] components,
      bool isQuaternion
    )
    {
      var valueSuffix = isQuaternion ? ".value" : "";
      sb.AppendLine(
        "\t\t\t\tvar v_"
          + field.JsName
          + " = QJS.JS_GetPropertyStr(ctx, data, p_"
          + field.JsName
          + ");"
      );
      foreach (var c in components)
      {
        sb.AppendLine("\t\t\t\tfixed (byte* p" + c + "_" + field.JsName + " = s_" + c + ")");
        sb.AppendLine("\t\t\t\t{");
        sb.AppendLine(
          "\t\t\t\t\tvar cv_"
            + c
            + " = QJS.JS_GetPropertyStr(ctx, v_"
            + field.JsName
            + ", p"
            + c
            + "_"
            + field.JsName
            + ");"
        );
        sb.AppendLine(
          "\t\t\t\t\tdouble cd_" + c + "; QJS.JS_ToFloat64(ctx, &cd_" + c + ", cv_" + c + ");"
        );
        sb.AppendLine(
          "\t\t\t\t\tcomp." + field.Name + valueSuffix + "." + c + " = (float)cd_" + c + ";"
        );
        sb.AppendLine("\t\t\t\t\tQJS.JS_FreeValue(ctx, cv_" + c + ");");
        sb.AppendLine("\t\t\t\t}");
      }
      sb.AppendLine("\t\t\t\tQJS.JS_FreeValue(ctx, v_" + field.JsName + ");");
    }

    // -- Code generation (enum) ----------------------------------------------

    static void GenerateEnum(SourceProductionContext ctx, EnumTarget target)
    {
      var className = "Js" + target.TypeName + "Enum";
      var sb = new StringBuilder();

      sb.AppendLine("// <auto-generated/>");
      sb.AppendLine("namespace UnityJS.Entities.Generated");
      sb.AppendLine("{");
      sb.AppendLine("\tusing Core;");
      sb.AppendLine("\tusing UnityJS.QJS;");
      sb.AppendLine();
      sb.AppendLine("\tpublic static class " + className);
      sb.AppendLine("\t{");

      sb.AppendLine("#if UNITY_EDITOR");
      sb.AppendLine("\t\t[UnityEditor.InitializeOnLoadMethod]");
      sb.AppendLine("#endif");
      sb.AppendLine(
        "\t\t[UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.AfterAssembliesLoaded)]"
      );
      sb.AppendLine(
        "\t\tstatic void AutoRegister() => JsComponentRegistry.RegisterEnum(Register);"
      );
      sb.AppendLine();

      sb.AppendLine("\t\tpublic static unsafe void Register(JSContext ctx)");
      sb.AppendLine("\t\t{");
      sb.AppendLine("\t\t\tvar ns = QJS.JS_NewObject(ctx);");
      foreach (var member in target.Members)
      {
        sb.AppendLine(
          "\t\t\tvar p"
            + member.Name
            + " = System.Text.Encoding.UTF8.GetBytes(\""
            + member.Name
            + "\\0\");"
        );
        sb.AppendLine("\t\t\tfixed (byte* pp" + member.Name + " = p" + member.Name + ")");
        sb.AppendLine(
          "\t\t\t\tQJS.JS_SetPropertyStr(ctx, ns, pp"
            + member.Name
            + ", QJS.NewInt32(ctx, "
            + member.Value
            + "));"
        );
      }
      sb.AppendLine("\t\t\tvar global = QJS.JS_GetGlobalObject(ctx);");
      sb.AppendLine(
        "\t\t\tvar pName = System.Text.Encoding.UTF8.GetBytes(\"" + target.JsName + "\\0\");"
      );
      sb.AppendLine("\t\t\tfixed (byte* pp = pName)");
      sb.AppendLine("\t\t\t\tQJS.JS_SetPropertyStr(ctx, global, pp, ns);");
      sb.AppendLine("\t\t\tQJS.JS_FreeValue(ctx, global);");
      sb.AppendLine("\t\t}");

      // Emit Descriptions dictionary
      var hasAnyDoc = target.Description != null || target.Members.Any(m => m.Description != null);
      if (hasAnyDoc)
      {
        sb.AppendLine();
        sb.AppendLine(
          "\t\tpublic static readonly System.Collections.Generic.Dictionary<string, string> Descriptions = new()"
        );
        sb.AppendLine("\t\t{");
        if (target.Description != null)
          sb.AppendLine("\t\t\t{ \"\", \"" + EscapeString(target.Description) + "\" },");
        foreach (var member in target.Members)
        {
          if (member.Description != null)
            sb.AppendLine(
              "\t\t\t{ \""
                + EscapeString(member.Name)
                + "\", \""
                + EscapeString(member.Description)
                + "\" },"
            );
        }
        sb.AppendLine("\t\t};");
      }

      sb.AppendLine("\t}");
      sb.AppendLine("}");

      ctx.AddSource(className + ".g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    // -- Data types (plain structs for netstandard2.0) -----------------------

    struct BridgeTarget
    {
      public string TypeName;
      public string FullTypeName;
      public string JsName;
      public bool NeedAccessors;
      public bool NeedSetters;
      public ImmutableArray<FieldInfo> Fields;
      public string Description;

      public BridgeTarget(
        string typeName,
        string fullTypeName,
        string jsName,
        bool needAccessors,
        bool needSetters,
        ImmutableArray<FieldInfo> fields,
        string description = null
      )
      {
        TypeName = typeName;
        FullTypeName = fullTypeName;
        JsName = jsName;
        NeedAccessors = needAccessors;
        NeedSetters = needSetters;
        Fields = fields;
        Description = description;
      }
    }

    struct FieldInfo
    {
      public string Name;
      public string JsName;
      public JsFieldType Type;
      public bool HasGetter;
      public bool HasSetter;
      public string EnumTypeName;
      public string Description;

      public FieldInfo(
        string name,
        string jsName,
        JsFieldType type,
        bool needAccessors,
        bool needSetters,
        string enumTypeName = null,
        string description = null
      )
      {
        Name = name;
        JsName = jsName;
        Type = type;
        HasGetter = needAccessors;
        HasSetter = needSetters;
        EnumTypeName = enumTypeName;
        Description = description;
      }
    }

    enum JsFieldType
    {
      Float,
      Int,
      Bool,
      Float2,
      Float3,
      Float4,
      Quaternion,
      Enum,
      Unsupported,
    }

    struct EnumTarget
    {
      public string TypeName;
      public string JsName;
      public ImmutableArray<EnumMember> Members;
      public string Description;

      public EnumTarget(
        string typeName,
        string jsName,
        ImmutableArray<EnumMember> members,
        string description
      )
      {
        TypeName = typeName;
        JsName = jsName;
        Members = members;
        Description = description;
      }
    }

    struct EnumMember
    {
      public string Name;
      public int Value;
      public string Description;

      public EnumMember(string name, int value, string description)
      {
        Name = name;
        Value = value;
        Description = description;
      }
    }
  }
}
