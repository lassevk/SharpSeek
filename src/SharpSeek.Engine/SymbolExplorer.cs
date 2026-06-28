using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace SharpSeek.Engine;

/// <summary>A symbol matched by a search, with its kind and primary location.</summary>
public sealed record SymbolMatch(string Display, string Kind, ReferenceLocationInfo? Location);

/// <summary>Descriptive information about a resolved symbol.</summary>
public sealed record SymbolDetails(
    string Display,
    string Kind,
    string Accessibility,
    string? ContainingType,
    string? Documentation,
    IReadOnlyList<ReferenceLocationInfo> Locations);

/// <summary>A single declared symbol in a document outline.</summary>
public sealed record OutlineItem(string Display, string Kind, int Line);

/// <summary>
/// Read-only exploration of a loaded project: workspace symbol search, symbol details, and a
/// per-document outline.
/// </summary>
public sealed class SymbolExplorer
{
    /// <summary>
    /// Searches the project's source-declared symbols by name pattern (supports substring and
    /// camel-case matching), returning up to <paramref name="max"/> results.
    /// </summary>
    public async Task<IReadOnlyList<SymbolMatch>> SearchSymbolsAsync(
        Project project,
        string query,
        int max = 50,
        CancellationToken cancellationToken = default)
    {
        HashSet<string> handwrittenPaths = LocationDescriptor.HandwrittenPaths(project);
        IEnumerable<ISymbol> symbols = await SymbolFinder
            .FindSourceDeclarationsWithPatternAsync(
                project, query, SymbolFilter.TypeAndMember, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. symbols
                .OrderBy(symbol => symbol.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(symbol => symbol.ToDisplayString(), StringComparer.Ordinal)
                .Take(max)
                .Select(symbol =>
                {
                    List<ReferenceLocationInfo> locations =
                        LocationDescriptor.Definitions(symbol, handwrittenPaths);
                    return new SymbolMatch(
                        symbol.ToDisplayString(),
                        symbol.Kind.ToString(),
                        locations.Count > 0 ? locations[0] : null);
                })
        ];
    }

    /// <summary>Returns details (kind, accessibility, XML doc, locations) for matching symbols.</summary>
    public async Task<IReadOnlyList<SymbolDetails>> GetSymbolInfoAsync(
        Project project,
        string symbolName,
        CancellationToken cancellationToken = default)
    {
        HashSet<string> handwrittenPaths = LocationDescriptor.HandwrittenPaths(project);
        IEnumerable<ISymbol> symbols = await SymbolFinder
            .FindSourceDeclarationsAsync(project, symbolName, ignoreCase: false, cancellationToken)
            .ConfigureAwait(false);

        List<SymbolDetails> results = [];
        foreach (ISymbol symbol in symbols)
        {
            string? documentation = symbol.GetDocumentationCommentXml(cancellationToken: cancellationToken);
            results.Add(new SymbolDetails(
                symbol.ToDisplayString(),
                symbol.Kind.ToString(),
                symbol.DeclaredAccessibility.ToString(),
                symbol.ContainingType?.ToDisplayString(),
                string.IsNullOrWhiteSpace(documentation) ? null : documentation.Trim(),
                LocationDescriptor.Definitions(symbol, handwrittenPaths)));
        }

        return results;
    }

    /// <summary>
    /// Returns the types and members declared in a single document, identified by its file path
    /// (absolute, or any suffix of the document's path).
    /// </summary>
    public async Task<IReadOnlyList<OutlineItem>> DocumentOutlineAsync(
        Project project,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        Document? document = FindDocument(project, filePath);
        if (document is null)
        {
            return [];
        }

        SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        SemanticModel? model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || model is null)
        {
            return [];
        }

        List<OutlineItem> items = [];
        foreach (SyntaxNode node in root.DescendantNodes())
        {
            ISymbol? symbol = node switch
            {
                BaseTypeDeclarationSyntax or DelegateDeclarationSyntax
                    => model.GetDeclaredSymbol(node, cancellationToken),
                MethodDeclarationSyntax or ConstructorDeclarationSyntax or PropertyDeclarationSyntax
                    or IndexerDeclarationSyntax or EventDeclarationSyntax
                    => model.GetDeclaredSymbol(node, cancellationToken),
                VariableDeclaratorSyntax { Parent.Parent: BaseFieldDeclarationSyntax }
                    => model.GetDeclaredSymbol(node, cancellationToken),
                _ => null,
            };

            if (symbol is null)
            {
                continue;
            }

            int line = node.GetLocation().GetMappedLineSpan().StartLinePosition.Line + 1;
            items.Add(new OutlineItem(symbol.ToDisplayString(), symbol.Kind.ToString(), line));
        }

        return items;
    }

    private static Document? FindDocument(Project project, string filePath)
    {
        string normalized = Normalize(filePath);
        return project.Documents.FirstOrDefault(document =>
            document.FilePath is { } path
            && (string.Equals(Normalize(path), normalized, StringComparison.OrdinalIgnoreCase)
                || Normalize(path).EndsWith(normalized, StringComparison.OrdinalIgnoreCase)));
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
}
