using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace JsGameCodegen
{
    /// <summary>
    /// Extracts XML doc comment summaries from Roslyn symbols and syntax nodes.
    /// Works without /doc compiler flag by falling back to syntax trivia parsing.
    /// </summary>
    public static class DocExtractor
    {
        /// <summary>
        /// Extract the summary text from a Roslyn symbol.
        /// Tries the semantic API first, then falls back to parsing syntax trivia.
        /// </summary>
        public static string GetSummary(ISymbol symbol, CancellationToken ct = default)
        {
            // Semantic path: works when /doc or GenerateDocumentationFile is enabled
            var result = ParseSummaryXml(symbol.GetDocumentationCommentXml(cancellationToken: ct));
            if (result != null)
                return result;

            // Syntax trivia path: always works because we read the raw source text
            foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
            {
                var node = syntaxRef.GetSyntax(ct);
                result = GetSummaryFromNode(node);
                if (result != null)
                    return result;
            }

            return null;
        }

        /// <summary>
        /// Extract the summary text from a syntax node's leading doc-comment trivia.
        /// Handles the common Roslyn quirk where field symbols resolve to
        /// <see cref="VariableDeclaratorSyntax"/> while the doc comment lives on the
        /// parent <see cref="FieldDeclarationSyntax"/>.
        /// </summary>
        public static string GetSummaryFromNode(SyntaxNode node)
        {
            // VariableDeclaratorSyntax (e.g. "maxSpeed") has no leading trivia;
            // walk up to FieldDeclarationSyntax (e.g. "public float maxSpeed;")
            if (node is VariableDeclaratorSyntax)
            {
                // VariableDeclaratorSyntax -> VariableDeclarationSyntax -> FieldDeclarationSyntax
                if (node.Parent?.Parent is FieldDeclarationSyntax fieldDecl)
                    node = fieldDecl;
                // EnumMemberDeclarationSyntax lands here too in some edge cases
                else if (node.Parent?.Parent is BaseFieldDeclarationSyntax baseField)
                    node = baseField;
            }

            // Collect consecutive "/// " comment lines (handles both doc trivia kinds)
            var docLines = new System.Collections.Generic.List<string>();

            foreach (var trivia in node.GetLeadingTrivia())
            {
                if (trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                    trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
                {
                    var xml = trivia.ToFullString();
                    var cleaned = Regex.Replace(xml, @"^\s*///\s?", "", RegexOptions.Multiline).Trim();
                    return ParseSummaryXml("<doc>" + cleaned + "</doc>");
                }

                // Unity Bee parses with DocumentationMode.None, so /// comments
                // appear as SingleLineCommentTrivia instead of doc comment trivia.
                if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia))
                {
                    var text = trivia.ToFullString().TrimStart();
                    if (text.StartsWith("///"))
                    {
                        // Strip "/// " prefix, keep the XML content
                        var content = text.Length > 3 ? text.Substring(3).TrimStart() : "";
                        docLines.Add(content);
                    }
                    else
                    {
                        docLines.Clear(); // non-doc comment breaks the sequence
                    }
                }
                else if (!trivia.IsKind(SyntaxKind.WhitespaceTrivia) &&
                         !trivia.IsKind(SyntaxKind.EndOfLineTrivia))
                {
                    docLines.Clear(); // other non-whitespace trivia breaks the sequence
                }
            }

            if (docLines.Count > 0)
            {
                var joined = string.Join(" ", docLines).Trim();
                return ParseSummaryXml("<doc>" + joined + "</doc>");
            }

            return null;
        }

        /// <summary>
        /// Parse a <c>&lt;summary&gt;</c> element from an XML doc string.
        /// Accepts either the raw <c>GetDocumentationCommentXml()</c> output
        /// (which wraps content in <c>&lt;member&gt;</c>) or a simple wrapper element.
        /// </summary>
        public static string ParseSummaryXml(string xml)
        {
            if (string.IsNullOrEmpty(xml))
                return null;

            try
            {
                var doc = new XmlDocument();
                doc.LoadXml(xml);
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
    }
}
