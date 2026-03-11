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

namespace LuaGameCodegen
{
    [Generator]
    public class LuaCompileGenerator : IIncrementalGenerator
    {
        const string AttrShort = "LuaCompile";
        const string AttrFull = "LuaECS.Core.LuaCompileAttribute";

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
                    if (name == AttrShort || name == "LuaCompileAttribute"
                        || name.EndsWith(".LuaCompile") || name.EndsWith(".LuaCompileAttribute"))
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
                        System.Array.Empty<ParamInfo>(), LuaParamType.Void, summary,
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
                    var luaType = MapType(param.Type);
                    if (luaType == LuaParamType.Unsupported)
                    {
                        valid = false;
                        break;
                    }

                    parameters.Add(new ParamInfo(
                        param.Name,
                        luaType,
                        param.RefKind == RefKind.Out));
                }

                if (!valid)
                    continue;

                var returnType = MapType(symbol.ReturnType);
                if (returnType == LuaParamType.Unsupported && symbol.ReturnType.SpecialType != SpecialType.System_Void)
                    continue;

                var isVoid = symbol.ReturnType.SpecialType == SpecialType.System_Void;

                builder.Add(new CompiledMethod(
                    table, function, symbol.Name,
                    containingType.Name, containingType.ToDisplayString(), ns,
                    containingType.IsStatic,
                    parameters.ToArray(),
                    isVoid ? LuaParamType.Void : returnType,
                    summary,
                    null));
            }

            return builder.ToImmutable();
        }

        static LuaParamType MapType(ITypeSymbol type)
        {
            var name = type.ToDisplayString();
            switch (name)
            {
                case "float": return LuaParamType.Float;
                case "int": return LuaParamType.Int;
                case "bool": return LuaParamType.Bool;
                case "Unity.Mathematics.float3": return LuaParamType.Float3;
                default: return LuaParamType.Unsupported;
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
            sb.AppendLine("\tusing LuaECS.Core;");
            sb.AppendLine("\tusing LuaNET.LuaJIT;");
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
            sb.AppendLine("\t\tstatic void LuaCompiled_AutoRegister()");
            sb.AppendLine("\t\t{");
            foreach (var tableGroup in byTable)
            {
                var safeName = TableToIdentifier(tableGroup.Key);
                sb.AppendLine("\t\t\tLuaFunctionRegistry.Register(\"" + tableGroup.Key + "\", LuaCompiled_Register_" + safeName + ");");
            }
            sb.AppendLine("\t\t}");
            sb.AppendLine();

            // Per-table registration functions
            foreach (var tableGroup in byTable)
            {
                var safeName = TableToIdentifier(tableGroup.Key);
                sb.AppendLine("\t\tstatic void LuaCompiled_Register_" + safeName + "(lua_State l)");
                sb.AppendLine("\t\t{");
                foreach (var m in tableGroup)
                {
                    sb.AppendLine("\t\t\tLua.lua_pushcfunction(l, LuaCompiled_" + m.MethodName + ");");
                    sb.AppendLine("\t\t\tLua.lua_setfield(l, -2, \"" + m.Function + "\");");
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

            var fileName = first.ClassName + ".LuaCompiled.g.cs";
            ctx.AddSource(fileName, SourceText.From(sb.ToString(), Encoding.UTF8));
        }

        static void EmitWrapper(StringBuilder sb, CompiledMethod m)
        {
            sb.AppendLine("\t\t[MonoPInvokeCallback(typeof(Lua.lua_CFunction))]");
            sb.AppendLine("\t\tstatic int LuaCompiled_" + m.MethodName + "(lua_State l)");
            sb.AppendLine("\t\t{");

            // Read input args (skip out params)
            var stackIdx = 1;
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

            var returnCount = 0;

            if (m.ReturnType != LuaParamType.Void)
            {
                sb.AppendLine("\t\t\tvar result = " + m.MethodName + "(" + callArgs + ");");
                sb.AppendLine("\t\t\t" + PushCode(m.ReturnType, "result"));
                returnCount = 1;
            }
            else
            {
                sb.AppendLine("\t\t\t" + m.MethodName + "(" + callArgs + ");");
            }

            // Push out params
            for (var i = 0; i < m.Parameters.Length; i++)
            {
                var p = m.Parameters[i];
                if (!p.IsOut)
                    continue;
                sb.AppendLine("\t\t\t" + PushCode(p.Type, p.Name));
                returnCount++;
            }

            sb.AppendLine("\t\t\treturn " + returnCount + ";");
            sb.AppendLine("\t\t}");
            sb.AppendLine();
        }

        static string ReadCode(LuaParamType type, int idx)
        {
            switch (type)
            {
                case LuaParamType.Float: return "(float)Lua.lua_tonumber(l, " + idx + ")";
                case LuaParamType.Int: return "(int)Lua.lua_tointeger(l, " + idx + ")";
                case LuaParamType.Bool: return "Lua.lua_toboolean(l, " + idx + ") != 0";
                case LuaParamType.Float3: return "LuaECSBridge.TableToFloat3(l, " + idx + ")";
                default: return "default";
            }
        }

        static string PushCode(LuaParamType type, string expr)
        {
            switch (type)
            {
                case LuaParamType.Float: return "Lua.lua_pushnumber(l, " + expr + ");";
                case LuaParamType.Int: return "Lua.lua_pushinteger(l, " + expr + ");";
                case LuaParamType.Bool: return "Lua.lua_pushboolean(l, " + expr + " ? 1 : 0);";
                case LuaParamType.Float3: return "LuaECSBridge.PushFloat3AsTable(l, " + expr + ");";
                default: return "Lua.lua_pushnil(l);";
            }
        }

        static string CSharpTypeName(LuaParamType type)
        {
            switch (type)
            {
                case LuaParamType.Float: return "float";
                case LuaParamType.Int: return "int";
                case LuaParamType.Bool: return "bool";
                case LuaParamType.Float3: return "float3";
                default: return "object";
            }
        }

        static string LuaCATSType(LuaParamType type)
        {
            switch (type)
            {
                case LuaParamType.Float: return "number";
                case LuaParamType.Int: return "integer";
                case LuaParamType.Bool: return "boolean";
                case LuaParamType.Float3: return "vec3";
                default: return "any";
            }
        }

        static void GenerateStubInfo(SourceProductionContext ctx, ImmutableArray<CompiledMethod> methods)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/>");
            sb.AppendLine("namespace LuaECS.Generated");
            sb.AppendLine("{");
            sb.AppendLine("    public static class LuaCompiledStubs");
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

            ctx.AddSource("LuaCompiledStubs.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
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
                sb.Append(LuaCATSType(p.Type));
            }

            sb.Append(")");

            // Build return types
            var returns = new List<string>();
            if (m.ReturnType != LuaParamType.Void)
                returns.Add(LuaCATSType(m.ReturnType));
            foreach (var p in m.Parameters)
            {
                if (p.IsOut)
                    returns.Add(LuaCATSType(p.Type));
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

        enum LuaParamType
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
            public LuaParamType Type;
            public bool IsOut;

            public ParamInfo(string name, LuaParamType type, bool isOut)
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
            public LuaParamType ReturnType;
            public string Summary;
            public string Signature;

            public CompiledMethod(
                string table, string function, string methodName,
                string className, string fullClassName, string ns,
                bool isStaticClass,
                ParamInfo[] parameters, LuaParamType returnType, string summary,
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
