using System.Linq;
using JsGameCodegen;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace Tests
{
    [TestFixture]
    public class DocExtractorTests
    {
        // -- ParseSummaryXml -----------------------------------------------------

        [Test]
        public void ParseSummaryXml_Null_ReturnsNull()
        {
            Assert.IsNull(DocExtractor.ParseSummaryXml(null));
        }

        [Test]
        public void ParseSummaryXml_Empty_ReturnsNull()
        {
            Assert.IsNull(DocExtractor.ParseSummaryXml(""));
        }

        [Test]
        public void ParseSummaryXml_ValidWrapped_ExtractsSummary()
        {
            var xml = "<member name=\"T:Foo\"><summary>Hello world.</summary></member>";
            Assert.AreEqual("Hello world.", DocExtractor.ParseSummaryXml(xml));
        }

        [Test]
        public void ParseSummaryXml_SimpleWrapper_ExtractsSummary()
        {
            var xml = "<doc><summary>Some text</summary></doc>";
            Assert.AreEqual("Some text", DocExtractor.ParseSummaryXml(xml));
        }

        [Test]
        public void ParseSummaryXml_CollapseWhitespace()
        {
            var xml = "<doc><summary>\n  Multi\n  line\n  </summary></doc>";
            Assert.AreEqual("Multi line", DocExtractor.ParseSummaryXml(xml));
        }

        [Test]
        public void ParseSummaryXml_NoSummaryTag_ReturnsNull()
        {
            var xml = "<doc><remarks>No summary here</remarks></doc>";
            Assert.IsNull(DocExtractor.ParseSummaryXml(xml));
        }

        [Test]
        public void ParseSummaryXml_MalformedXml_ReturnsNull()
        {
            Assert.IsNull(DocExtractor.ParseSummaryXml("not xml at all"));
        }

        // -- GetSummaryFromNode --------------------------------------------------

        static SyntaxTree Parse(string code) =>
            CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(documentationMode: DocumentationMode.Parse));

        [Test]
        public void GetSummaryFromNode_StructDeclaration()
        {
            var tree = Parse(@"
/// <summary>My struct doc.</summary>
public struct Foo { }
");
            var node = tree.GetRoot().DescendantNodes().OfType<StructDeclarationSyntax>().Single();
            Assert.AreEqual("My struct doc.", DocExtractor.GetSummaryFromNode(node));
        }

        [Test]
        public void GetSummaryFromNode_EnumDeclaration()
        {
            var tree = Parse(@"
/// <summary>My enum doc.</summary>
public enum Bar { A, B }
");
            var node = tree.GetRoot().DescendantNodes().OfType<EnumDeclarationSyntax>().Single();
            Assert.AreEqual("My enum doc.", DocExtractor.GetSummaryFromNode(node));
        }

        [Test]
        public void GetSummaryFromNode_EnumMember()
        {
            var tree = Parse(@"
public enum Bar
{
    /// <summary>First member.</summary>
    A,
    B
}
");
            var node = tree.GetRoot().DescendantNodes().OfType<EnumMemberDeclarationSyntax>().First();
            Assert.AreEqual("First member.", DocExtractor.GetSummaryFromNode(node));
        }

        [Test]
        public void GetSummaryFromNode_FieldDeclaration()
        {
            var tree = Parse(@"
public struct S
{
    /// <summary>Speed value.</summary>
    public float speed;
}
");
            var node = tree.GetRoot().DescendantNodes().OfType<FieldDeclarationSyntax>().Single();
            Assert.AreEqual("Speed value.", DocExtractor.GetSummaryFromNode(node));
        }

        [Test]
        public void GetSummaryFromNode_VariableDeclarator_WalksUpToField()
        {
            var tree = Parse(@"
public struct S
{
    /// <summary>Speed value.</summary>
    public float speed;
}
");
            // This is what IFieldSymbol.DeclaringSyntaxReferences returns
            var node = tree.GetRoot().DescendantNodes().OfType<VariableDeclaratorSyntax>().Single();
            Assert.AreEqual("Speed value.", DocExtractor.GetSummaryFromNode(node));
        }

        [Test]
        public void GetSummaryFromNode_NoDocComment_ReturnsNull()
        {
            var tree = Parse(@"
public struct S
{
    public float speed;
}
");
            var node = tree.GetRoot().DescendantNodes().OfType<VariableDeclaratorSyntax>().Single();
            Assert.IsNull(DocExtractor.GetSummaryFromNode(node));
        }

        [Test]
        public void GetSummaryFromNode_MultipleFields_EachGetsOwnDoc()
        {
            var tree = Parse(@"
public struct S
{
    /// <summary>First.</summary>
    public float a;

    /// <summary>Second.</summary>
    public float b;
}
");
            var vars = tree.GetRoot().DescendantNodes().OfType<VariableDeclaratorSyntax>().ToList();
            Assert.AreEqual("First.", DocExtractor.GetSummaryFromNode(vars[0]));
            Assert.AreEqual("Second.", DocExtractor.GetSummaryFromNode(vars[1]));
        }

        // -- GetSummaryFromNode (DocumentationMode.None -- Unity Bee) -----------

        static SyntaxTree ParseNoDocs(string code) =>
            CSharpSyntaxTree.ParseText(code, new CSharpParseOptions(documentationMode: DocumentationMode.None));

        [Test]
        public void GetSummaryFromNode_NoDocs_StructDeclaration()
        {
            var tree = ParseNoDocs(@"
/// <summary>My struct doc.</summary>
public struct Foo { }
");
            var node = tree.GetRoot().DescendantNodes().OfType<StructDeclarationSyntax>().Single();
            Assert.AreEqual("My struct doc.", DocExtractor.GetSummaryFromNode(node));
        }

        [Test]
        public void GetSummaryFromNode_NoDocs_FieldViaVariableDeclarator()
        {
            var tree = ParseNoDocs(@"
public struct S
{
    /// <summary>Speed value.</summary>
    public float speed;
}
");
            var node = tree.GetRoot().DescendantNodes().OfType<VariableDeclaratorSyntax>().Single();
            Assert.AreEqual("Speed value.", DocExtractor.GetSummaryFromNode(node));
        }

        [Test]
        public void GetSummaryFromNode_NoDocs_NoDocComment_ReturnsNull()
        {
            var tree = ParseNoDocs(@"
public struct S
{
    // regular comment
    public float speed;
}
");
            var node = tree.GetRoot().DescendantNodes().OfType<VariableDeclaratorSyntax>().Single();
            Assert.IsNull(DocExtractor.GetSummaryFromNode(node));
        }

        // -- GetSummary (full symbol-based path) --------------------------------

        [Test]
        public void GetSummary_StructSymbol_ExtractsDoc()
        {
            var comp = CreateCompilation(@"
/// <summary>My component.</summary>
public struct MyComp { }
");
            var symbol = comp.GetTypeByMetadataName("MyComp");
            Assert.IsNotNull(symbol);
            Assert.AreEqual("My component.", DocExtractor.GetSummary(symbol));
        }

        [Test]
        public void GetSummary_FieldSymbol_ExtractsDoc()
        {
            var comp = CreateCompilation(@"
public struct MyComp
{
    /// <summary>The speed.</summary>
    public float Speed;
}
");
            var type = comp.GetTypeByMetadataName("MyComp");
            var field = type.GetMembers("Speed").Single();
            Assert.AreEqual("The speed.", DocExtractor.GetSummary(field));
        }

        [Test]
        public void GetSummary_FieldWithoutDoc_ReturnsNull()
        {
            var comp = CreateCompilation(@"
public struct MyComp
{
    public float Speed;
}
");
            var type = comp.GetTypeByMetadataName("MyComp");
            var field = type.GetMembers("Speed").Single();
            Assert.IsNull(DocExtractor.GetSummary(field));
        }

        [Test]
        public void GetSummary_EnumSymbol_ExtractsDoc()
        {
            var comp = CreateCompilation(@"
/// <summary>Directions.</summary>
public enum Dir { Up, Down }
");
            var symbol = comp.GetTypeByMetadataName("Dir");
            Assert.AreEqual("Directions.", DocExtractor.GetSummary(symbol));
        }

        [Test]
        public void GetSummary_EnumMemberSymbol_ExtractsDoc()
        {
            var comp = CreateCompilation(@"
public enum Dir
{
    /// <summary>Upward.</summary>
    Up,
    Down
}
");
            var type = comp.GetTypeByMetadataName("Dir");
            var member = type.GetMembers("Up").Single();
            Assert.AreEqual("Upward.", DocExtractor.GetSummary(member));
        }

        [Test]
        public void GetSummary_ExternalSymbol_ReturnsNull()
        {
            // Symbols from referenced assemblies have no syntax refs -- should return null, not crash
            var comp = CreateCompilation("public class C { System.String s; }");
            var stringType = comp.GetSpecialType(SpecialType.System_String);
            Assert.IsNull(DocExtractor.GetSummary(stringType));
        }

        // -- GetSummary with DocumentationMode.None (Unity Bee path) ------------

        [Test]
        public void GetSummary_NoDocs_StructSymbol_ExtractsDoc()
        {
            var comp = CreateCompilation(@"
/// <summary>My component.</summary>
public struct MyComp { }
", DocumentationMode.None);
            var symbol = comp.GetTypeByMetadataName("MyComp");
            Assert.IsNotNull(symbol);
            Assert.AreEqual("My component.", DocExtractor.GetSummary(symbol));
        }

        [Test]
        public void GetSummary_NoDocs_FieldSymbol_ExtractsDoc()
        {
            var comp = CreateCompilation(@"
public struct MyComp
{
    /// <summary>The speed.</summary>
    public float Speed;
}
", DocumentationMode.None);
            var type = comp.GetTypeByMetadataName("MyComp");
            var field = type.GetMembers("Speed").Single();
            Assert.AreEqual("The speed.", DocExtractor.GetSummary(field));
        }

        static CSharpCompilation CreateCompilation(string source, DocumentationMode docMode = DocumentationMode.Parse)
        {
            var tree = CSharpSyntaxTree.ParseText(source,
                new CSharpParseOptions(documentationMode: docMode));
            return CSharpCompilation.Create("TestAssembly",
                new[] { tree },
                new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }
    }
}
