using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;

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
/// Read-only exploration of a loaded solution: workspace symbol search, symbol details, a
/// per-document outline, and literal search.
/// </summary>
public sealed class SymbolExplorer
{
    /// <summary>
    /// Searches the solution's source-declared symbols by name pattern (supports substring and
    /// camel-case matching), returning up to <paramref name="max"/> results.
    /// </summary>
    public async Task<IReadOnlyList<SymbolMatch>> SearchSymbolsAsync(
        Solution solution,
        string query,
        int max = 50,
        CancellationToken cancellationToken = default)
    {
        HashSet<string> handwrittenPaths = LocationDescriptor.HandwrittenPaths(solution);
        IEnumerable<ISymbol> symbols = await SymbolFinder
            .FindSourceDeclarationsWithPatternAsync(
                solution, query, SymbolFilter.TypeAndMember, cancellationToken)
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
        Solution solution,
        string symbolName,
        CancellationToken cancellationToken = default)
    {
        HashSet<string> handwrittenPaths = LocationDescriptor.HandwrittenPaths(solution);
        IEnumerable<ISymbol> symbols = await SymbolFinder
            .FindSourceDeclarationsAsync(solution, symbolName, ignoreCase: false, cancellationToken)
            .ConfigureAwait(false);

        return [.. symbols.Select(symbol => BuildDetails(symbol, handwrittenPaths, cancellationToken))];
    }

    /// <summary>
    /// Resolves the symbol referenced or declared at a position (1-based line and column) in a C#
    /// source file. Returns an empty list if the file is not a C# document in the solution or no
    /// symbol is found there.
    /// </summary>
    public async Task<IReadOnlyList<SymbolDetails>> ResolveSymbolAtAsync(
        Solution solution,
        string filePath,
        int line,
        int column,
        CancellationToken cancellationToken = default)
    {
        Document? document = FindDocument(solution, filePath);
        if (document is null)
        {
            return [];
        }

        SourceText text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        if (line < 1 || line > text.Lines.Count)
        {
            return [];
        }

        TextLine textLine = text.Lines[line - 1];
        int position = Math.Min(textLine.Start + Math.Max(0, column - 1), textLine.End);

        SyntaxNode? root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        SemanticModel? model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || model is null)
        {
            return [];
        }

        HashSet<string> handwrittenPaths = LocationDescriptor.HandwrittenPaths(solution);
        SyntaxToken token = root.FindToken(position);
        for (SyntaxNode? node = token.Parent; node is not null; node = node.Parent)
        {
            ISymbol? symbol = model.GetSymbolInfo(node, cancellationToken).Symbol
                ?? model.GetDeclaredSymbol(node, cancellationToken);
            if (symbol is not null)
            {
                return [BuildDetails(symbol, handwrittenPaths, cancellationToken)];
            }
        }

        return [];
    }

    private static SymbolDetails BuildDetails(
        ISymbol symbol,
        HashSet<string> handwrittenPaths,
        CancellationToken cancellationToken)
    {
        string? documentation = symbol.GetDocumentationCommentXml(cancellationToken: cancellationToken);
        return new SymbolDetails(
            symbol.ToDisplayString(),
            symbol.Kind.ToString(),
            symbol.DeclaredAccessibility.ToString(),
            symbol.ContainingType?.ToDisplayString(),
            string.IsNullOrWhiteSpace(documentation) ? null : documentation.Trim(),
            LocationDescriptor.Definitions(symbol, handwrittenPaths));
    }

    /// <summary>
    /// Returns the types and members declared in a single document, identified by its file path
    /// (absolute, or any suffix of the document's path).
    /// </summary>
    public async Task<IReadOnlyList<OutlineItem>> DocumentOutlineAsync(
        Solution solution,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        Document? document = FindDocument(solution, filePath);
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

    /// <summary>
    /// Finds occurrences of a literal value (string, number, or char) across the solution,
    /// including literals emitted into source-generated code, mapped back to their source.
    /// </summary>
    public async Task<IReadOnlyList<ReferenceLocationInfo>> FindLiteralUsagesAsync(
        Solution solution,
        string value,
        CancellationToken cancellationToken = default)
    {
        HashSet<string> handwrittenPaths = LocationDescriptor.HandwrittenPaths(solution);
        List<ReferenceLocationInfo> results = [];
        foreach (Project project in solution.Projects)
        {
            Compilation? compilation = await project.GetCompilationAsync(cancellationToken)
                .ConfigureAwait(false);
            if (compilation is null)
            {
                continue;
            }

            foreach (SyntaxTree tree in compilation.SyntaxTrees)
            {
                SyntaxNode root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);
                foreach (LiteralExpressionSyntax literal in root.DescendantNodes().OfType<LiteralExpressionSyntax>())
                {
                    if (string.Equals(literal.Token.ValueText, value, StringComparison.Ordinal)
                        || string.Equals(literal.Token.Text, value, StringComparison.Ordinal))
                    {
                        results.Add(LocationDescriptor.Describe(tree, literal.Span, handwrittenPaths));
                    }
                }
            }
        }

        return results;
    }

    private static Document? FindDocument(Solution solution, string filePath)
    {
        string normalized = Normalize(filePath);
        foreach (Project project in solution.Projects)
        {
            foreach (Document document in project.Documents)
            {
                if (document.FilePath is { } path
                    && (string.Equals(Normalize(path), normalized, StringComparison.OrdinalIgnoreCase)
                        || Normalize(path).EndsWith(normalized, StringComparison.OrdinalIgnoreCase)))
                {
                    return document;
                }
            }
        }

        return null;
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
}
