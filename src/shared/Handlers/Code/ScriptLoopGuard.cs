using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Bimwright.Ipt.Shared.Handlers.Code;

/// <summary>
/// Puts <c>IptGuard.Check(source, line)</c> at the top of every <c>for</c>/<c>foreach</c>/<c>while</c>/<c>do</c> body of a
/// <c>send_code</c> source (script + modules, after <c>SendCodeSource.Build</c>). The call is written on the line where
/// the body starts and adds no line breaks, so diagnostics and runtime locations keep their line numbers; the
/// <c>#line</c> directives of the built source are trivia and stay where they are. Host-free (Roslyn only).
/// </summary>
public static class ScriptLoopGuard
{
    public const string CheckCall = "global::Bimwright.Ipt.Scripting.IptGuard.Check";

    public static string Instrument(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(kind: SourceCodeKind.Script));
        var root = tree.GetRoot();
        if (!root.DescendantNodes().Any(IsLoop)) return source;
        return new Rewriter().Visit(root)!.ToFullString();
    }

    private static bool IsLoop(SyntaxNode n) =>
        n is ForStatementSyntax or CommonForEachStatementSyntax or WhileStatementSyntax or DoStatementSyntax;

    private sealed class Rewriter : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitForStatement(ForStatementSyntax node)
        {
            var visited = (ForStatementSyntax)base.VisitForStatement(node)!;
            return visited.WithStatement(Guard(node, visited.Statement));
        }

        public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node)
        {
            var visited = (ForEachStatementSyntax)base.VisitForEachStatement(node)!;
            return visited.WithStatement(Guard(node, visited.Statement));
        }

        public override SyntaxNode? VisitForEachVariableStatement(ForEachVariableStatementSyntax node)
        {
            var visited = (ForEachVariableStatementSyntax)base.VisitForEachVariableStatement(node)!;
            return visited.WithStatement(Guard(node, visited.Statement));
        }

        public override SyntaxNode? VisitWhileStatement(WhileStatementSyntax node)
        {
            var visited = (WhileStatementSyntax)base.VisitWhileStatement(node)!;
            return visited.WithStatement(Guard(node, visited.Statement));
        }

        public override SyntaxNode? VisitDoStatement(DoStatementSyntax node)
        {
            var visited = (DoStatementSyntax)base.VisitDoStatement(node)!;
            return visited.WithStatement(Guard(node, visited.Statement));
        }

        /// <summary>
        /// Block body: insert the call right after <c>{</c>, on the same line. Single-statement body: wrap it in braces
        /// written with spaces only. The original node gives the mapped (#line) source and line for the message.
        /// </summary>
        private static StatementSyntax Guard(SyntaxNode loop, StatementSyntax body)
        {
            var span = loop.GetLocation().GetMappedLineSpan();
            var source = string.IsNullOrEmpty(span.Path) ? SendCodeSourceName : span.Path;
            var line = span.StartLinePosition.Line + 1;
            var call = SyntaxFactory.ParseStatement(
                    $"{CheckCall}({SymbolDisplay.FormatLiteral(source, quote: true)}, {line}); ")
                .WithLeadingTrivia(SyntaxFactory.Space);
            if (body is BlockSyntax block)
                return block.WithStatements(block.Statements.Insert(0, call));
            var open = SyntaxFactory.Token(SyntaxKind.OpenBraceToken).WithLeadingTrivia(body.GetLeadingTrivia());
            var close = SyntaxFactory.Token(SyntaxKind.CloseBraceToken).WithLeadingTrivia(SyntaxFactory.Space)
                .WithTrailingTrivia(body.GetTrailingTrivia());
            var inner = body.WithLeadingTrivia(SyntaxFactory.Space).WithTrailingTrivia();
            return SyntaxFactory.Block(open, SyntaxFactory.List(new[] { call, inner }), close);
        }

        private const string SendCodeSourceName = "script";
    }
}
