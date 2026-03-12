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
            var methods = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsCandidate(node),
                    transform: static (ctx, ct) => ExtractAll(ctx, ct))
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
                    if (name == AttrShort || name == "JsCompileAttribute"
                        || name.EndsWith(".JsCompile") || name.EndsWith(".JsCompileAttribute"))
                        return true;
                }
            }

            return false;
        }

        static ImmutableArray<CompiledMethod> ExtractAll(GeneratorSyntaxContext ctx, CancellationToken ct)
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
                    builder.Add(new CompiledMethod(
                        table, function, symbol.Name,
                        containingType.Name, containingType.ToDisplayString(), ns,
                        containingType.IsStatic,
                        System.Array.Empty<ParamInfo>(), JsParamType.Void, summary,
                        signature));
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

                    parameters.Add(new ParamInfo(
                        param.Name,
                        jsType,
                        param.RefKind == RefKind.Out));
                }

                if (!valid)
                    continue;

                var returnType = MapType(symbol.ReturnType);
                if (returnType == JsParamType.Unsupported && symbol.ReturnType.SpecialType != SpecialType.System_Void)
                    continue;

                var isVoid = symbol.ReturnType.SpecialType == SpecialType.System_Void;

                builder.Add(new CompiledMethod(
                    table, function, symbol.Name,
                    containingType.Name, containingType.ToDisplayString(), ns,
                    containingType.IsStatic,
                    parameters.ToArray(),
                    isVoid ? JsParamType.Void : returnType,
                    summary,
                    null));
            }

            return builder.ToImmutable();
        }

        static JsParamType MapType(ITypeSymbol type)
        {
            var name = type.ToDisplayString();
            switch (name)
            {
                case "float": return JsParamType.Float;
                case "int": return JsParamType.Int;
                case "bool": return JsParamType.Bool;
                case "Unity.Mathematics.float3": return JsParamType.Float3;
                default: return JsParamType.Unsupported;
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

            // Separate full-compile vs stub-only
            var fullMethods = methods.Where(m => m.Signature == null).ToList();
            var allMethods = methods; // stubs need all

            // Group full-compile by containing class
            if (fullMethods.Count > 0)
            {
                var byClass = fullMethods
                    .GroupBy(m => m.FullClassName)
                    .ToList();

                foreach (var classGroup in byClass)
                {
                    GenerateClassPartial(ctx, classGroup.Key, classGroup.ToList());
                }
            }

            // Generate stub info class (includes both full and stub-only)
            GenerateStubInfo(ctx, allMethods);
        }

        static void GenerateClassPartial(
            SourceProductionContext ctx,
            string fullClassName,
            List<CompiledMethod> methods)
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
            sb.AppendLine("\tusing Unity.Mathematics;");
            sb.AppendLine();

            var staticMod = first.IsStaticClass ? "static " : "";
            sb.AppendLine("\t" + staticMod + "partial class " + first.ClassName);
            sb.AppendLine("\t{");

            // Group by table for registration
            var byTable = methods.GroupBy(m => m.Table).ToList();

            // Auto-register method
            sb.AppendLine("#if UNITY_EDITOR");
            sb.AppendLine("\t\t[UnityEditor.InitializeOnLoadMethod]");
            sb.AppendLine("#endif");
            sb.AppendLine("\t\t[UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.AfterAssembliesLoaded)]");
            sb.AppendLine("\t\tstatic void JsCompiled_AutoRegister()");
            sb.AppendLine("\t\t{");
            foreach (var tableGroup in byTable)
            {
                var safeName = TableToIdentifier(tableGroup.Key);
                sb.AppendLine("\t\t\tJsFunctionRegistry.Register(\"" + tableGroup.Key + "\", JsCompiled_Register_" + safeName + ");");
            }
            sb.AppendLine("\t\t}");
            sb.AppendLine();

            // Per-table registration functions
            foreach (var tableGroup in byTable)
            {
                var safeName = TableToIdentifier(tableGroup.Key);
                sb.AppendLine("\t\tstatic unsafe void JsCompiled_Register_" + safeName + "(JSContext ctx)");
                sb.AppendLine("\t\t{");
                foreach (var m in tableGroup)
                {
                    var argc = m.Parameters.Count(p => !p.IsOut);
                    sb.AppendLine("\t\t\tvar p_" + m.MethodName + " = System.Text.Encoding.UTF8.GetBytes(\"" + m.Function + "\\0\");");
                    sb.AppendLine("\t\t\tfixed (byte* pp_" + m.MethodName + " = p_" + m.MethodName + ")");
                    sb.AppendLine("\t\t\t{");
                    sb.AppendLine("\t\t\t\tvar fn = QJSShim.qjs_shim_new_function(ctx, JsCompiled_" + m.MethodName + ", pp_" + m.MethodName + ", " + argc + ");");
                    sb.AppendLine("\t\t\t\tQJS.JS_SetPropertyStr(ctx, ctx, pp_" + m.MethodName + ", fn);");
                    sb.AppendLine("\t\t\t}");
                }
                sb.AppendLine("\t\t}");
                sb.AppendLine();
            }

            // Per-method wrappers
            foreach (var m in methods)
            {
                EmitWrapper(sb, m);
            }

            sb.AppendLine("\t}");

            if (!string.IsNullOrEmpty(first.Namespace))
                sb.AppendLine("}");

            var fileName = first.ClassName + ".JsCompiled.g.cs";
            ctx.AddSource(fileName, SourceText.From(sb.ToString(), Encoding.UTF8));
        }

        static void EmitWrapper(StringBuilder sb, CompiledMethod m)
        {
            sb.AppendLine("\t\t[MonoPInvokeCallback(typeof(QJSShimCallback))]");
            sb.AppendLine("\t\tstatic unsafe void JsCompiled_" + m.MethodName + "(JSContext ctx, long thisU, long thisTag,");
            sb.AppendLine("\t\t\tint argc, JSValue* argv, long* outU, long* outTag)");
            sb.AppendLine("\t\t{");

            // Read input args (skip out params)
            var stackIdx = 0;
            for (var i = 0; i < m.Parameters.Length; i++)
            {
                var p = m.Parameters[i];
                if (p.IsOut)
                {
                    // Declare local for out param
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
                if (i > 0) callArgs.Append(", ");
                if (m.Parameters[i].IsOut)
                    callArgs.Append("out ");
                callArgs.Append(m.Parameters[i].Name);
            }

            var hasReturnOrOut = m.ReturnType != JsParamType.Void || m.Parameters.Any(p => p.IsOut);

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
                case JsParamType.Float3:
                    return "JsECSBridge.ArgToFloat3(ctx, argv, " + argIdx + ")";
                default: return "default";
            }
        }

        static string PushCode(JsParamType type, string expr)
        {
            switch (type)
            {
                case JsParamType.Float: return "QJS.NewFloat64(ctx, " + expr + ")";
                case JsParamType.Int: return "QJS.NewInt32(ctx, " + expr + ")";
                case JsParamType.Bool: return "QJS.JS_NewBool(ctx, " + expr + " ? 1 : 0)";
                case JsParamType.Float3: return "JsECSBridge.Float3ToNewObject(ctx, " + expr + ")";
                default: return "QJS.JS_UNDEFINED";
            }
        }

        static string CSharpTypeName(JsParamType type)
        {
            switch (type)
            {
                case JsParamType.Float: return "float";
                case JsParamType.Int: return "int";
                case JsParamType.Bool: return "bool";
                case JsParamType.Float3: return "float3";
                default: return "object";
            }
        }

        static string JsCATSType(JsParamType type)
        {
            switch (type)
            {
                case JsParamType.Float: return "number";
                case JsParamType.Int: return "integer";
                case JsParamType.Bool: return "boolean";
                case JsParamType.Float3: return "vec3";
                default: return "any";
            }
        }

        static void GenerateStubInfo(SourceProductionContext ctx, ImmutableArray<CompiledMethod> methods)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("namespace UnityJS.Entities.Generated");
            sb.AppendLine("{");
            sb.AppendLine("    public static class JsCompiledStubs");
            sb.AppendLine("    {");
            sb.AppendLine("        public static readonly (string table, string function, string signature, string description)[] Stubs = new[]");
            sb.AppendLine("        {");

            foreach (var m in methods.OrderBy(x => x.Table).ThenBy(x => x.Function))
            {
                var sig = m.Signature ?? BuildSignature(m);
                var desc = m.Summary ?? "";
                sb.AppendLine("            (\"" + EscapeString(m.Table) + "\", \"" + EscapeString(m.Function)
                              + "\", \"" + EscapeString(sig) + "\", \"" + EscapeString(desc) + "\"),");
            }

            sb.AppendLine("        };");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            ctx.AddSource("JsCompiledStubs.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        }

        static string BuildSignature(CompiledMethod m)
        {
            var sb = new StringBuilder("fun(");

            var first = true;
            foreach (var p in m.Parameters)
            {
                if (p.IsOut)
                    continue;
                if (!first) sb.Append(", ");
                first = false;
                sb.Append(SnakeCaseHelper.ToSnakeCase(p.Name));
                sb.Append(": ");
                sb.Append(JsCATSType(p.Type));
            }

            sb.Append(")");

            // Build return types
            var returns = new List<string>();
            if (m.ReturnType != JsParamType.Void)
                returns.Add(JsCATSType(m.ReturnType));
            foreach (var p in m.Parameters)
            {
                if (p.IsOut)
                    returns.Add(JsCATSType(p.Type));
            }

            if (returns.Count > 0)
            {
                sb.Append(": ");
                sb.Append(string.Join(", ", returns));
            }

            return sb.ToString();
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
            Float3,
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
                string table, string function, string methodName,
                string className, string fullClassName, string ns,
                bool isStaticClass,
                ParamInfo[] parameters, JsParamType returnType, string summary,
                string signature)
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
