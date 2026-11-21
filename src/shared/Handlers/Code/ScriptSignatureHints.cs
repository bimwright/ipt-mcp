using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Bimwright.Ipt.Shared.Handlers.Code;

/// <summary>
/// For an argument/ref compile error (CS1620, CS1503, CS1615, CS1501, CS7036) in a <c>send_code</c> script: the real
/// signatures of the member being called, resolved with the semantic model (not by name), so the hint names the
/// overloads the script can actually use. Only Inventor API and Ipt helper members get a hint. Host-free (Roslyn only).
/// </summary>
public static class ScriptSignatureHints
{
    private const int MaxSignatures = 3;

    private static readonly SymbolDisplayFormat TypeFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

    public static string? ForDiagnostic(Diagnostic d, Compilation compilation)
    {
        if (d.Id is not ("CS1620" or "CS1503" or "CS1615" or "CS1501" or "CS7036")) return null;
        var tree = d.Location.SourceTree;
        if (tree is null) return null;
        var call = CalledExpression(tree.GetRoot().FindNode(d.Location.SourceSpan, getInnermostNodeForTie: true), d.Location.SourceSpan,
            onArgument: d.Id is "CS1620" or "CS1503" or "CS1615");
        if (call is null) return null;

        var model = compilation.GetSemanticModel(tree);
        var info = model.GetSymbolInfo(call);
        IEnumerable<ISymbol> symbols = info.Symbol is { } s ? new[] { s } : info.CandidateSymbols;
        if (!symbols.Any())
            symbols = model.GetMemberGroup(call is InvocationExpressionSyntax i ? i.Expression : ((ElementAccessExpressionSyntax)call).Expression);

        var members = symbols.Where(m => m is IMethodSymbol or IPropertySymbol { IsIndexer: false, Parameters.Length: > 0 })
            .Distinct<ISymbol>(SymbolEqualityComparer.Default).ToArray();
        if (members.Length == 0 || !members.All(m => IsApi(m.ContainingType))) return null;

        var lines = members.Select(Render).Distinct().Take(MaxSignatures).ToArray();
        var hasRef = members.Take(MaxSignatures).Any(m => Parameters(m).Any(p => p.RefKind == RefKind.Ref));
        return "Signature: " + string.Join(" | ", lines) +
            (hasRef ? ". A ref argument must be a variable of exactly that type, e.g. double[] p = { x / 10, y / 10, z / 10 }; then pass ref p." : ".");
    }

    /// <summary>
    /// The invocation or element access the diagnostic belongs to: for an argument error the call whose argument list
    /// holds that argument, otherwise (CS1501/CS7036 sit on the callee name) the call whose callee contains the span.
    /// </summary>
    private static ExpressionSyntax? CalledExpression(SyntaxNode node, Microsoft.CodeAnalysis.Text.TextSpan span, bool onArgument)
    {
        for (SyntaxNode? n = node; n != null; n = n.Parent)
        {
            switch (n)
            {
                case ArgumentSyntax arg:
                    return onArgument && arg.Parent?.Parent is InvocationExpressionSyntax or ElementAccessExpressionSyntax
                        ? (ExpressionSyntax)arg.Parent!.Parent! : null;
                case BaseObjectCreationExpressionSyntax:
                    return null;
                case InvocationExpressionSyntax inv when inv.Expression.Span.Contains(span):
                    return inv;
                // A wrong argument count on an indexed property sits on the whole element access.
                case ElementAccessExpressionSyntax ea when ea.Expression.Span.Contains(span) || (!onArgument && ea.Span == span):
                    return ea;
                case StatementSyntax:
                    return null;
            }
        }
        return null;
    }

    private static bool IsApi(INamedTypeSymbol? type)
    {
        var ns = type?.ContainingNamespace?.ToDisplayString();
        return ns is "Inventor" or "Bimwright.Ipt.Scripting";
    }

    private static IEnumerable<IParameterSymbol> Parameters(ISymbol m) =>
        m is IMethodSymbol method ? method.Parameters : ((IPropertySymbol)m).Parameters;

    private static string Render(ISymbol m)
    {
        var args = string.Join(", ", Parameters(m).Select(p =>
            (p.RefKind switch { RefKind.Ref => "ref ", RefKind.Out => "out ", _ => "" }) +
            p.Type.ToDisplayString(TypeFormat) + " " + p.Name + (p.IsOptional ? " = optional" : "")));
        var returns = m is IMethodSymbol method ? method.ReturnType : ((IPropertySymbol)m).Type;
        var owner = m.ContainingType.ContainingType is { } outer ? outer.Name + "." + m.ContainingType.Name : m.ContainingType.Name;
        // An indexed property (COM) is used with brackets: body.IsPointInside[ref p].
        return m is IPropertySymbol
            ? $"{owner}.{m.Name}[{args}] -> {returns.ToDisplayString(TypeFormat)}"
            : $"{owner}.{m.Name}({args}) -> {returns.ToDisplayString(TypeFormat)}";
    }
}
