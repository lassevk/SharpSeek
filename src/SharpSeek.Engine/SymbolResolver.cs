using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace SharpSeek.Engine;

/// <summary>
/// Resolves a symbol from a name that may be a simple name (<c>Foo</c>), a
/// <c>Type.Member</c> pair, or a fully-qualified name (<c>Namespace.Type.Member</c>). The last
/// dotted segment is the symbol's own name; any preceding segments must match the symbol's
/// containing types and namespaces as a dot-boundary suffix, which lets a caller disambiguate
/// between same-named symbols (e.g. members on different types). Overloads and partial
/// declarations all match and are returned.
/// </summary>
/// <remarks>
/// Resolution covers source-generated declarations too (e.g. a Blazor <c>BuildRenderTree</c>), which
/// <see cref="SymbolFinder.FindSourceDeclarationsAsync(Solution, string, bool, CancellationToken)"/>
/// alone does not, so the declaration tools can reach members that exist only in generated code.
/// </remarks>
internal static class SymbolResolver
{
    public static async Task<IReadOnlyList<ISymbol>> ResolveAsync(
        Solution solution,
        string name,
        CancellationToken cancellationToken)
    {
        int lastDot = name.LastIndexOf('.');
        string simpleName = lastDot >= 0 ? name[(lastDot + 1)..] : name;
        bool qualified = lastDot >= 0;

        IEnumerable<ISymbol> candidates = await SymbolFinder
            .FindSourceDeclarationsAsync(solution, simpleName, ignoreCase: false, cancellationToken)
            .ConfigureAwait(false);

        List<ISymbol> matches =
            [.. candidates.Where(symbol => !qualified || QualifierMatches(symbol, name))];

        await CollectGeneratedAsync(solution, simpleName, name, qualified, matches, cancellationToken)
            .ConfigureAwait(false);

        return matches;
    }

    /// <summary>
    /// Adds declarations found in source-generated documents that match the name and are not already
    /// in <paramref name="matches"/> (a partial type declared in both hand-written and generated code
    /// is a single symbol, already present from the source-declaration search).
    /// </summary>
    private static async Task CollectGeneratedAsync(
        Solution solution,
        string simpleName,
        string fullName,
        bool qualified,
        List<ISymbol> matches,
        CancellationToken cancellationToken)
    {
        foreach (Project project in solution.Projects)
        {
            IEnumerable<Document> generated = await project
                .GetSourceGeneratedDocumentsAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (Document document in generated)
            {
                SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                SemanticModel? model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                if (root is null || model is null)
                {
                    continue;
                }

                foreach (SyntaxNode node in root.DescendantNodes())
                {
                    if (!IsDeclaration(node))
                    {
                        continue;
                    }

                    ISymbol? symbol = model.GetDeclaredSymbol(node, cancellationToken);
                    if (symbol is null
                        || !string.Equals(symbol.Name, simpleName, StringComparison.Ordinal)
                        || (qualified && !QualifierMatches(symbol, fullName)))
                    {
                        continue;
                    }

                    if (!matches.Any(existing => SymbolEqualityComparer.Default.Equals(existing, symbol)))
                    {
                        matches.Add(symbol);
                    }
                }
            }
        }
    }

    private static bool IsDeclaration(SyntaxNode node) => node is
        BaseTypeDeclarationSyntax or DelegateDeclarationSyntax or MethodDeclarationSyntax
        or ConstructorDeclarationSyntax or PropertyDeclarationSyntax or IndexerDeclarationSyntax
        or EventDeclarationSyntax or VariableDeclaratorSyntax;

    private static bool QualifierMatches(ISymbol symbol, string qualifiedName)
    {
        string path = DottedName(symbol);
        return string.Equals(path, qualifiedName, StringComparison.Ordinal)
            || path.EndsWith("." + qualifiedName, StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds the dotted path of a symbol from its containing namespaces and types down to its own
    /// name, without parameter lists or generic arity, for suffix matching against the query.
    /// </summary>
    private static string DottedName(ISymbol symbol)
    {
        List<string> parts = [symbol.Name];

        for (INamedTypeSymbol? type = symbol.ContainingType; type is not null; type = type.ContainingType)
        {
            parts.Add(type.Name);
        }

        for (INamespaceSymbol? @namespace = symbol.ContainingNamespace;
            @namespace is { IsGlobalNamespace: false };
            @namespace = @namespace.ContainingNamespace)
        {
            parts.Add(@namespace.Name);
        }

        parts.Reverse();
        return string.Join('.', parts);
    }
}
