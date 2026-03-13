using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace JsGameCodegen
{
  [Generator]
  public class JsCompileGenerator : IIncrementalGenerator
  {
    const string AttrShort = "JsCompile";
    const string AttrFull = "UnityJS.Entities.Core.JsCompileAttribute";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
      var methods = context
        .SyntaxProvider.CreateSyntaxProvider(
          predicate: static (node, _) => IsCandidate(node),
          transform: static (ctx, ct) => ExtractAll(ctx, ct)
        )
        .Where(static x => !x.IsDefaultOrEmpty)
        .SelectMany(static (x, _) => x);

      var collected = methods.Collect();

      context.RegisterSourceOutput(collected, static (ctx, entries) => Generate(ctx, entries));
    }

    static bool IsCandidate(SyntaxNode node)
    {
      if (node is not MethodDeclarationSyntax method)
        return false;

      foreach (var attrList in method.AttributeLists)
      {
        foreach (var attr in attrList.Attributes)
        {
          var name = attr.Name.ToString();
          if (
            name == AttrShort
            || name == "JsCompileAttribute"
            || name.EndsWith(".JsCompile")
            || name.EndsWith(".JsCompileAttribute")
          )
            return true;
        }
      }

      return false;
    }

    static ImmutableArray<CompiledMethod> ExtractAll(
      GeneratorSyntaxContext ctx,
      CancellationToken ct
    )
    {
      var method = (MethodDeclarationSyntax)ctx.Node;
      var symbol = ctx.SemanticModel.GetDeclaredSymbol(method, ct);
      if (symbol == null || !symbol.IsStatic || symbol.IsGenericMethod)
        return ImmutableArray<CompiledMethod>.Empty;

      var containingType = symbol.ContainingType;
      if (containingType == null)
        return ImmutableArray<CompiledMethod>.Empty;

      // Extract doc comment (shared across all attributes on this method)
      var docXml = symbol.GetDocumentationCommentXml(cancellationToken: ct);
      var summary = ExtractSummary(docXml);

      // Build namespace chain
      var ns = containingType.ContainingNamespace?.ToDisplayString();
      if (ns == "<global namespace>")
        ns = null;

      var builder = ImmutableArray.CreateBuilder<CompiledMethod>();

      foreach (var attrData in symbol.GetAttributes())
      {
        if (attrData.AttributeClass?.ToDisplayString() != AttrFull)
          continue;

        if (attrData.ConstructorArguments.Length < 2)
          continue;

        var table = attrData.ConstructorArguments[0].Value as string;
        var function = attrData.ConstructorArguments[1].Value as string;

        if (string.IsNullOrEmpty(table) || string.IsNullOrEmpty(function))
          continue;

        // Read Signature named arg
        string signature = null;
        foreach (var named in attrData.NamedArguments)
        {
          if (named.Key == "Signature" && named.Value.Value is string sig)
            signature = sig;
        }

        if (signature != null)
        {
          // Stub-only mode: no param/return validation, no partial-class check
          builder.Add(
            new CompiledMethod(
              table,
              function,
              symbol.Name,
              containingType.Name,
              containingType.ToDisplayString(),
              ns,
              containingType.IsStatic,
              System.Array.Empty<ParamInfo>(),
              JsParamType.Void,
              summary,
              signature
            )
          );
          continue;
        }

        // Full compile mode: validate params, require partial class
        var classDecl = method.Parent as TypeDeclarationSyntax;
        if (classDecl == null || !classDecl.Modifiers.Any(SyntaxKind.PartialKeyword))
          continue;

        var parameters = new List<ParamInfo>();
        var valid = true;
        foreach (var param in symbol.Parameters)
        {
          var jsType = MapType(param.Type);
          if (jsType == JsParamType.Unsupported)
          {
            valid = false;
            break;
          }

          parameters.Add(new ParamInfo(param.Name, jsType, param.RefKind == RefKind.Out));
        }

        if (!valid)
          continue;

        var returnType = MapType(symbol.ReturnType);
        if (
          returnType == JsParamType.Unsupported
          && symbol.ReturnType.SpecialType != SpecialType.System_Void
        )
          continue;

        var isVoid = symbol.ReturnType.SpecialType == SpecialType.System_Void;

        builder.Add(
          new CompiledMethod(
            table,
            function,
            symbol.Name,
            containingType.Name,
            containingType.ToDisplayString(),
            ns,
            containingType.IsStatic,
            parameters.ToArray(),
            isVoid ? JsParamType.Void : returnType,
            summary,
            null
          )
        );
      }

      return builder.ToImmutable();
    }

    static JsParamType MapType(ITypeSymbol type)
    {
      var name = type.ToDisplayString();
      switch (name)
      {
        case "float":
          return JsParamType.Float;
        case "double":
          return JsParamType.Float;
        case "int":
          return JsParamType.Int;
        case "bool":
          return JsParamType.Bool;
        case "Unity.Mathematics.float2":
          return JsParamType.Float2;
        case "Unity.Mathematics.float3":
          return JsParamType.Float3;
        case "Unity.Mathematics.float4":
          return JsParamType.Float4;
        case "Unity.Mathematics.quaternion":
          return JsParamType.Quaternion;
        default:
          return JsParamType.Unsupported;
      }
    }

    static string ExtractSummary(string docXml)
    {
      if (string.IsNullOrEmpty(docXml))
        return null;

      try
      {
        var doc = new XmlDocument();
        doc.LoadXml(docXml);
        var summaryNode = doc.SelectSingleNode("//summary");
        if (summaryNode == null)
          return null;

        var text = summaryNode.InnerText;
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length > 0 ? text : null;
      }
      catch
      {
        return null;
      }
    }

    static void Generate(SourceProductionContext ctx, ImmutableArray<CompiledMethod> methods)
    {
      if (methods.Length == 0)
        return;

      // Group ALL methods by containing class (both full-compile and Signature-mode)
      var byClass = methods.GroupBy(m => m.FullClassName).ToList();

      foreach (var classGroup in byClass)
      {
        GenerateClassPartial(ctx, classGroup.Key, classGroup.ToList());
      }

      // Generate stub info class (includes both full and stub-only)
      GenerateStubInfo(ctx, methods);
    }

    static void GenerateClassPartial(
      SourceProductionContext ctx,
      string fullClassName,
      List<CompiledMethod> methods
    )
    {
      var first = methods[0];
      var sb = new StringBuilder();

      sb.AppendLine("// <auto-generated/>");

      if (!string.IsNullOrEmpty(first.Namespace))
      {
        sb.AppendLine("namespace " + first.Namespace);
        sb.AppendLine("{");
      }

      sb.AppendLine("\tusing AOT;");
      sb.AppendLine("\tusing UnityJS.Entities.Core;");
      sb.AppendLine("\tusing UnityJS.QJS;");
      sb.AppendLine("\tusing UnityJS.Runtime;");
      sb.AppendLine("\tusing Unity.Mathematics;");
      sb.AppendLine();

      var staticMod = first.IsStaticClass ? "static " : "";
      sb.AppendLine("\t" + staticMod + "partial class " + first.ClassName);
      sb.AppendLine("\t{");

      // Group by (table, function) for overload dispatch
      var byTableFunc = methods.GroupBy(m => new { m.Table, m.Function }).ToList();

      // Check if we need DetectVecSize helper (only for full-compile overload groups)
      var needsDetectVecSize = byTableFunc.Any(g => g.Count(m => m.Signature == null) > 1);

      if (needsDetectVecSize)
      {
        EmitDetectVecSize(sb);
      }

      // Group by table for registration
      var byTable = byTableFunc.GroupBy(g => g.Key.Table).ToList();

      // Auto-register method
      sb.AppendLine("#if UNITY_EDITOR");
      sb.AppendLine("\t\t[UnityEditor.InitializeOnLoadMethod]");
      sb.AppendLine("#endif");
      sb.AppendLine(
        "\t\t[UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.AfterAssembliesLoaded)]"
      );
      sb.AppendLine("\t\tstatic void JsCompiled_AutoRegister()");
      sb.AppendLine("\t\t{");
      foreach (var tableGroup in byTable)
      {
        var safeName = TableToIdentifier(tableGroup.Key);
        sb.AppendLine(
          "\t\t\tJsFunctionRegistry.Register(\""
            + tableGroup.Key
            + "\", JsCompiled_Register_"
            + safeName
            + ");"
        );
      }
      sb.AppendLine("\t\t}");
      sb.AppendLine();

      // Per-table registration functions — register one JS function per (table, function) group
      foreach (var tableGroup in byTable)
      {
        var safeName = TableToIdentifier(tableGroup.Key);
        sb.AppendLine(
          "\t\tstatic unsafe void JsCompiled_Register_" + safeName + "(JSContext ctx, JSValue ns)"
        );
        sb.AppendLine("\t\t{");
        foreach (var funcGroup in tableGroup)
        {
          var funcName = funcGroup.Key.Function;
          var isSignatureMode = funcGroup.Any(m => m.Signature != null);
          var fullOverloads = funcGroup.Where(m => m.Signature == null).ToList();

          string wrapperName;
          int argc;

          if (isSignatureMode && fullOverloads.Count == 0)
          {
            // Pure Signature-mode: the method already has QJSShimCallback signature
            wrapperName = funcGroup.First().MethodName;
            argc = CountSignatureParams(funcGroup.First().Signature);
          }
          else
          {
            // Full-compile: use generated wrapper
            wrapperName = "JsCompiled_" + funcName;
            if (fullOverloads.Count == 1)
              wrapperName = "JsCompiled_" + fullOverloads[0].MethodName;
            argc = fullOverloads.Max(m => m.Parameters.Count(p => !p.IsOut));
          }

          sb.AppendLine(
            "\t\t\tvar p_"
              + funcName
              + " = System.Text.Encoding.UTF8.GetBytes(\""
              + funcName
              + "\\0\");"
          );
          sb.AppendLine("\t\t\tfixed (byte* pp_" + funcName + " = p_" + funcName + ")");
          sb.AppendLine("\t\t\t{");
          sb.AppendLine(
            "\t\t\t\tvar fn = QJSShim.qjs_shim_new_function(ctx, "
              + wrapperName
              + ", pp_"
              + funcName
              + ", "
              + argc
              + ");"
          );
          sb.AppendLine("\t\t\t\tQJS.JS_SetPropertyStr(ctx, ns, pp_" + funcName + ", fn);");
          sb.AppendLine("\t\t\t}");
        }
        sb.AppendLine("\t\t}");
        sb.AppendLine();
      }

      // Emit wrappers — single methods get direct wrappers, groups get dispatch
      // Skip Signature-mode methods (they have manual [MonoPInvokeCallback] implementations)
      var fullByTableFunc = byTableFunc
        .Select(g => new { g.Key, Methods = g.Where(m => m.Signature == null).ToList() })
        .Where(g => g.Methods.Count > 0)
        .ToList();

      foreach (var funcGroup in fullByTableFunc)
      {
        if (funcGroup.Methods.Count == 1)
        {
          EmitWrapper(sb, funcGroup.Methods[0]);
        }
        else
        {
          EmitDispatchWrapper(sb, funcGroup.Key.Function, funcGroup.Methods);
        }
      }

      sb.AppendLine("\t}");

      if (!string.IsNullOrEmpty(first.Namespace))
        sb.AppendLine("}");

      var fileName = first.ClassName + ".JsCompiled.g.cs";
      ctx.AddSource(fileName, SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    static void EmitDetectVecSize(StringBuilder sb)
    {
      sb.AppendLine("\t\tstatic readonly byte[] s_dvs_z = {(byte)'z', 0};");
      sb.AppendLine("\t\tstatic readonly byte[] s_dvs_w = {(byte)'w', 0};");
      sb.AppendLine();
      sb.AppendLine("\t\tstatic unsafe int DetectVecSize(JSContext ctx, JSValue val)");
      sb.AppendLine("\t\t{");
      sb.AppendLine("\t\t\tfixed (byte* pw = s_dvs_w)");
      sb.AppendLine("\t\t\t{");
      sb.AppendLine("\t\t\t\tvar w = QJS.JS_GetPropertyStr(ctx, val, pw);");
      sb.AppendLine("\t\t\t\tvar has = !QJS.IsUndefined(w);");
      sb.AppendLine("\t\t\t\tQJS.JS_FreeValue(ctx, w);");
      sb.AppendLine("\t\t\t\tif (has) return 4;");
      sb.AppendLine("\t\t\t}");
      sb.AppendLine("\t\t\tfixed (byte* pz = s_dvs_z)");
      sb.AppendLine("\t\t\t{");
      sb.AppendLine("\t\t\t\tvar z = QJS.JS_GetPropertyStr(ctx, val, pz);");
      sb.AppendLine("\t\t\t\tvar has = !QJS.IsUndefined(z);");
      sb.AppendLine("\t\t\t\tQJS.JS_FreeValue(ctx, z);");
      sb.AppendLine("\t\t\t\tif (has) return 3;");
      sb.AppendLine("\t\t\t}");
      sb.AppendLine("\t\t\treturn 2;");
      sb.AppendLine("\t\t}");
      sb.AppendLine();
    }

    static void EmitWrapper(StringBuilder sb, CompiledMethod m)
    {
      sb.AppendLine("\t\t[MonoPInvokeCallback(typeof(QJSShimCallback))]");
      sb.AppendLine(
        "\t\tstatic unsafe void JsCompiled_"
          + m.MethodName
          + "(JSContext ctx, long thisU, long thisTag,"
      );
      sb.AppendLine("\t\t\tint argc, JSValue* argv, long* outU, long* outTag)");
      sb.AppendLine("\t\t{");

      // Read input args (skip out params)
      var stackIdx = 0;
      for (var i = 0; i < m.Parameters.Length; i++)
      {
        var p = m.Parameters[i];
        if (p.IsOut)
        {
          sb.AppendLine("\t\t\t" + CSharpTypeName(p.Type) + " " + p.Name + ";");
        }
        else
        {
          sb.AppendLine("\t\t\tvar " + p.Name + " = " + ReadCode(p.Type, stackIdx) + ";");
          stackIdx++;
        }
      }

      // Build call
      var callArgs = new StringBuilder();
      for (var i = 0; i < m.Parameters.Length; i++)
      {
        if (i > 0)
          callArgs.Append(", ");
        if (m.Parameters[i].IsOut)
          callArgs.Append("out ");
        callArgs.Append(m.Parameters[i].Name);
      }

      if (m.ReturnType != JsParamType.Void)
      {
        sb.AppendLine("\t\t\tvar result = " + m.MethodName + "(" + callArgs + ");");
        sb.AppendLine("\t\t\tvar retVal = " + PushCode(m.ReturnType, "result") + ";");
        sb.AppendLine("\t\t\t*outU = retVal.u;");
        sb.AppendLine("\t\t\t*outTag = retVal.tag;");
      }
      else
      {
        sb.AppendLine("\t\t\t" + m.MethodName + "(" + callArgs + ");");
        sb.AppendLine("\t\t\tvar retVal = QJS.JS_UNDEFINED;");
        sb.AppendLine("\t\t\t*outU = retVal.u;");
        sb.AppendLine("\t\t\t*outTag = retVal.tag;");
      }

      sb.AppendLine("\t\t}");
      sb.AppendLine();
    }

    static void EmitDispatchWrapper(
      StringBuilder sb,
      string funcName,
      List<CompiledMethod> overloads
    )
    {
      // Categorize overloads by first param type
      CompiledMethod? floatOverload = null;
      CompiledMethod? float2Overload = null;
      CompiledMethod? float3Overload = null;
      CompiledMethod? float4Overload = null;

      foreach (var m in overloads)
      {
        if (m.Parameters.Length == 0)
          continue;
        var firstType = m.Parameters[0].Type;
        switch (firstType)
        {
          case JsParamType.Float:
            floatOverload = m;
            break;
          case JsParamType.Float2:
            float2Overload = m;
            break;
          case JsParamType.Float3:
            float3Overload = m;
            break;
          case JsParamType.Float4:
            float4Overload = m;
            break;
        }
      }

      sb.AppendLine("\t\t[MonoPInvokeCallback(typeof(QJSShimCallback))]");
      sb.AppendLine(
        "\t\tstatic unsafe void JsCompiled_"
          + funcName
          + "(JSContext ctx, long thisU, long thisTag,"
      );
      sb.AppendLine("\t\t\tint argc, JSValue* argv, long* outU, long* outTag)");
      sb.AppendLine("\t\t{");

      // Check if first arg is a number → scalar overload
      if (floatOverload != null)
      {
        sb.AppendLine("\t\t\tif (QJS.IsNumber(argv[0]))");
        sb.AppendLine("\t\t\t{");
        EmitInlineCall(sb, floatOverload.Value, "\t\t\t\t");
        sb.AppendLine("\t\t\t\treturn;");
        sb.AppendLine("\t\t\t}");
      }

      // Vector dispatch
      var hasVecOverloads =
        float2Overload != null || float3Overload != null || float4Overload != null;
      if (hasVecOverloads)
      {
        sb.AppendLine("\t\t\tvar _sz = DetectVecSize(ctx, argv[0]);");

        if (float4Overload != null)
        {
          sb.AppendLine("\t\t\tif (_sz == 4)");
          sb.AppendLine("\t\t\t{");
          EmitInlineCall(sb, float4Overload.Value, "\t\t\t\t");
          sb.AppendLine("\t\t\t}");
          sb.Append("\t\t\telse ");
        }

        if (float3Overload != null)
        {
          sb.AppendLine("if (_sz == 3)");
          sb.AppendLine("\t\t\t{");
          EmitInlineCall(sb, float3Overload.Value, "\t\t\t\t");
          sb.AppendLine("\t\t\t}");
          sb.Append("\t\t\telse ");
        }

        if (float2Overload != null)
        {
          sb.AppendLine();
          sb.AppendLine("\t\t\t{");
          EmitInlineCall(sb, float2Overload.Value, "\t\t\t\t");
          sb.AppendLine("\t\t\t}");
        }
        else
        {
          // Fallback to float3 if no float2
          sb.AppendLine();
          sb.AppendLine("\t\t\t{");
          if (float3Overload != null)
            EmitInlineCall(sb, float3Overload.Value, "\t\t\t\t");
          else
          {
            sb.AppendLine("\t\t\t\t*outU = QJS.JS_UNDEFINED.u;");
            sb.AppendLine("\t\t\t\t*outTag = QJS.JS_UNDEFINED.tag;");
          }
          sb.AppendLine("\t\t\t}");
        }
      }

      sb.AppendLine("\t\t}");
      sb.AppendLine();
    }

    static void EmitInlineCall(StringBuilder sb, CompiledMethod m, string indent)
    {
      // Read args
      var stackIdx = 0;
      for (var i = 0; i < m.Parameters.Length; i++)
      {
        var p = m.Parameters[i];
        if (p.IsOut)
        {
          sb.AppendLine(indent + CSharpTypeName(p.Type) + " _p" + i + ";");
        }
        else
        {
          sb.AppendLine(indent + "var _p" + i + " = " + ReadCode(p.Type, stackIdx) + ";");
          stackIdx++;
        }
      }

      // Build call args
      var callArgs = new StringBuilder();
      for (var i = 0; i < m.Parameters.Length; i++)
      {
        if (i > 0)
          callArgs.Append(", ");
        if (m.Parameters[i].IsOut)
          callArgs.Append("out ");
        callArgs.Append("_p" + i);
      }

      if (m.ReturnType != JsParamType.Void)
      {
        sb.AppendLine(indent + "var _ret = " + m.MethodName + "(" + callArgs + ");");
        sb.AppendLine(indent + "var _rv = " + PushCode(m.ReturnType, "_ret") + ";");
        sb.AppendLine(indent + "*outU = _rv.u; *outTag = _rv.tag;");
      }
      else
      {
        sb.AppendLine(indent + m.MethodName + "(" + callArgs + ");");
        sb.AppendLine(indent + "*outU = QJS.JS_UNDEFINED.u; *outTag = QJS.JS_UNDEFINED.tag;");
      }
    }

    static string ReadCode(JsParamType type, int argIdx)
    {
      switch (type)
      {
        case JsParamType.Float:
          return "QJSHelpers.ArgFloat(ctx, argv, " + argIdx + ")";
        case JsParamType.Int:
          return "QJSHelpers.ArgInt(ctx, argv, " + argIdx + ")";
        case JsParamType.Bool:
          return "QJS.JS_ToBool(ctx, argv[" + argIdx + "]) != 0";
        case JsParamType.Float2:
          return "JsStateExtensions.JsObjectToFloat2(ctx, argv[" + argIdx + "])";
        case JsParamType.Float3:
          return "JsStateExtensions.JsObjectToFloat3(ctx, argv[" + argIdx + "])";
        case JsParamType.Float4:
          return "JsStateExtensions.JsObjectToFloat4(ctx, argv[" + argIdx + "])";
        case JsParamType.Quaternion:
          return "JsStateExtensions.JsObjectToQuaternion(ctx, argv[" + argIdx + "])";
        default:
          return "default";
      }
    }

    static string PushCode(JsParamType type, string expr)
    {
      switch (type)
      {
        case JsParamType.Float:
          return "QJS.NewFloat64(ctx, " + expr + ")";
        case JsParamType.Int:
          return "QJS.NewInt32(ctx, " + expr + ")";
        case JsParamType.Bool:
          return "QJS.JS_NewBool(ctx, " + expr + " ? 1 : 0)";
        case JsParamType.Float2:
          return "JsStateExtensions.Float2ToJsObject(ctx, " + expr + ")";
        case JsParamType.Float3:
          return "JsStateExtensions.Float3ToJsObject(ctx, " + expr + ")";
        case JsParamType.Float4:
          return "JsStateExtensions.Float4ToJsObject(ctx, " + expr + ")";
        case JsParamType.Quaternion:
          return "JsStateExtensions.QuaternionToJsObject(ctx, " + expr + ")";
        default:
          return "QJS.JS_UNDEFINED";
      }
    }

    static string CSharpTypeName(JsParamType type)
    {
      switch (type)
      {
        case JsParamType.Float:
          return "float";
        case JsParamType.Int:
          return "int";
        case JsParamType.Bool:
          return "bool";
        case JsParamType.Float2:
          return "float2";
        case JsParamType.Float3:
          return "float3";
        case JsParamType.Float4:
          return "float4";
        case JsParamType.Quaternion:
          return "quaternion";
        default:
          return "object";
      }
    }

    static string TsTypeName(JsParamType type)
    {
      switch (type)
      {
        case JsParamType.Float:
          return "number";
        case JsParamType.Int:
          return "number";
        case JsParamType.Bool:
          return "boolean";
        case JsParamType.Float2:
          return "float2";
        case JsParamType.Float3:
          return "float3";
        case JsParamType.Float4:
          return "float4";
        case JsParamType.Quaternion:
          return "quaternion";
        default:
          return "any";
      }
    }

    static void GenerateStubInfo(
      SourceProductionContext ctx,
      ImmutableArray<CompiledMethod> methods
    )
    {
      // Group by (table, function) for TS signature generation
      var grouped = methods
        .OrderBy(x => x.Table)
        .ThenBy(x => x.Function)
        .GroupBy(m => new { m.Table, m.Function })
        .ToList();

      var sb = new StringBuilder();
      sb.AppendLine("// <auto-generated/>");
      sb.AppendLine("namespace UnityJS.Entities.Generated");
      sb.AppendLine("{");
      sb.AppendLine("    public static class JsCompiledStubs");
      sb.AppendLine("    {");
      sb.AppendLine(
        "        public static readonly (string table, string function, string tsSignature, string description)[] Stubs = new[]"
      );
      sb.AppendLine("        {");

      foreach (var group in grouped)
      {
        var tsSig = BuildTsSignature(group.ToList());
        var desc = group.First().Summary ?? "";
        sb.AppendLine(
          "            (\""
            + EscapeString(group.Key.Table)
            + "\", \""
            + EscapeString(group.Key.Function)
            + "\", \""
            + EscapeString(tsSig)
            + "\", \""
            + EscapeString(desc)
            + "\"),"
        );
      }

      sb.AppendLine("        };");
      sb.AppendLine("    }");
      sb.AppendLine("}");

      ctx.AddSource("JsCompiledStubs.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    static string BuildTsSignature(List<CompiledMethod> group)
    {
      // If any has explicit Signature, use it
      var withSig = group.FirstOrDefault(m => m.Signature != null);
      if (withSig.Signature != null)
        return withSig.Signature;

      if (group.Count == 1)
      {
        return BuildSingleTsSignature(group[0]);
      }

      // Multiple overloads — detect pattern
      var firstParamTypes = new HashSet<JsParamType>(
        group.Where(m => m.Parameters.Length > 0).Select(m => m.Parameters[0].Type)
      );

      var returnTypes = new HashSet<JsParamType>(group.Select(m => m.ReturnType));

      // Componentwise pattern: first param type == return type for each overload
      var isComponentwise = group.All(m =>
        m.Parameters.Length > 0 && m.ReturnType == m.Parameters[0].Type
      );

      // Vector-to-scalar pattern: return type is always Float
      var isVectorToScalar = returnTypes.Count == 1 && returnTypes.Contains(JsParamType.Float);

      if (isComponentwise)
      {
        // Build generic: <T extends ...>(x: T, ...): T
        var types = new List<string>();
        if (firstParamTypes.Contains(JsParamType.Float))
          types.Add("number");
        if (firstParamTypes.Contains(JsParamType.Float2))
          types.Add("float2");
        if (firstParamTypes.Contains(JsParamType.Float3))
          types.Add("float3");
        if (firstParamTypes.Contains(JsParamType.Float4))
          types.Add("float4");

        var constraint = string.Join(" | ", types);

        // Use first overload as template for param names/count
        var template = group[0];
        var paramSb = new StringBuilder();
        for (var i = 0; i < template.Parameters.Length; i++)
        {
          if (i > 0)
            paramSb.Append(", ");
          var p = template.Parameters[i];
          // If the param type matches the first param type in all overloads, use T
          var allSameSlot = group.All(m =>
            m.Parameters.Length > i && m.Parameters[i].Type == m.Parameters[0].Type
          );
          if (allSameSlot)
            paramSb.Append(p.Name + ": T");
          else
            paramSb.Append(p.Name + ": " + TsTypeName(p.Type));
        }

        return "<T extends " + constraint + ">(" + paramSb + "): T";
      }

      if (isVectorToScalar)
      {
        // Build generic: <T extends float2 | float3 | float4>(a: T, b: T): number
        var types = new List<string>();
        if (firstParamTypes.Contains(JsParamType.Float))
          types.Add("number");
        if (firstParamTypes.Contains(JsParamType.Float2))
          types.Add("float2");
        if (firstParamTypes.Contains(JsParamType.Float3))
          types.Add("float3");
        if (firstParamTypes.Contains(JsParamType.Float4))
          types.Add("float4");

        var constraint = string.Join(" | ", types);
        var template = group[0];
        var paramSb = new StringBuilder();
        for (var i = 0; i < template.Parameters.Length; i++)
        {
          if (i > 0)
            paramSb.Append(", ");
          var p = template.Parameters[i];
          var allSameSlot = group.All(m =>
            m.Parameters.Length > i && m.Parameters[i].Type == m.Parameters[0].Type
          );
          if (allSameSlot)
            paramSb.Append(p.Name + ": T");
          else
            paramSb.Append(p.Name + ": " + TsTypeName(p.Type));
        }

        return "<T extends " + constraint + ">(" + paramSb + "): number";
      }

      // Fallback: just emit the first overload's signature
      return BuildSingleTsSignature(group[0]);
    }

    static string BuildSingleTsSignature(CompiledMethod m)
    {
      var sb = new StringBuilder("(");

      var first = true;
      foreach (var p in m.Parameters)
      {
        if (p.IsOut)
          continue;
        if (!first)
          sb.Append(", ");
        first = false;
        sb.Append(p.Name + ": " + TsTypeName(p.Type));
      }

      sb.Append("): ");
      sb.Append(m.ReturnType == JsParamType.Void ? "void" : TsTypeName(m.ReturnType));

      return sb.ToString();
    }

    static int CountSignatureParams(string signature)
    {
      if (string.IsNullOrEmpty(signature))
        return 0;
      var open = signature.IndexOf('(');
      var close = signature.IndexOf(')');
      if (open < 0 || close < 0 || close <= open + 1)
        return 0;
      var inside = signature.Substring(open + 1, close - open - 1).Trim();
      if (inside.Length == 0)
        return 0;
      return inside.Split(',').Length;
    }

    static string TableToIdentifier(string table)
    {
      return table.Replace(".", "_");
    }

    static string EscapeString(string s)
    {
      return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    enum JsParamType
    {
      Float,
      Int,
      Bool,
      Float2,
      Float3,
      Float4,
      Quaternion,
      Void,
      Unsupported,
    }

    struct ParamInfo
    {
      public string Name;
      public JsParamType Type;
      public bool IsOut;

      public ParamInfo(string name, JsParamType type, bool isOut)
      {
        Name = name;
        Type = type;
        IsOut = isOut;
      }
    }

    struct CompiledMethod
    {
      public string Table;
      public string Function;
      public string MethodName;
      public string ClassName;
      public string FullClassName;
      public string Namespace;
      public bool IsStaticClass;
      public ParamInfo[] Parameters;
      public JsParamType ReturnType;
      public string Summary;
      public string Signature;

      public CompiledMethod(
        string table,
        string function,
        string methodName,
        string className,
        string fullClassName,
        string ns,
        bool isStaticClass,
        ParamInfo[] parameters,
        JsParamType returnType,
        string summary,
        string signature
      )
      {
        Table = table;
        Function = function;
        MethodName = methodName;
        ClassName = className;
        FullClassName = fullClassName;
        Namespace = ns;
        IsStaticClass = isStaticClass;
        Parameters = parameters;
        ReturnType = returnType;
        Summary = summary;
        Signature = signature;
      }
    }
  }
}
