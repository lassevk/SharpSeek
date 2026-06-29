using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace SharpSeek.Engine;

/// <summary>The line range of a single declaration of a symbol.</summary>
/// <param name="SymbolDisplay">The resolved symbol this declaration belongs to.</param>
/// <param name="FilePath">The file the range refers to (generated declarations are mapped back where <c>#line</c> allows).</param>
/// <param name="StartLine">1-based first line of the declaration, including a leading XML-doc comment when present.</param>
/// <param name="EndLine">1-based last line of the declaration.</param>
/// <param name="Origin">Whether the declaration is hand-written or source-generated.</param>
/// <param name="GeneratedFilePath">For generated declarations, the generated document the range physically lives in.</param>
public sealed record DeclarationRange(
    string SymbolDisplay,
    string FilePath,
    int StartLine,
    int EndLine,
    ReferenceOrigin Origin,
    string? GeneratedFilePath);

/// <summary>
/// Returns the source spans of a symbol's declarations: the line range (for the caller to read the
/// live file itself) and, for source-generated members, the text directly from the in-memory
/// generated tree that the caller's own file reader cannot open. Each declaration covers, from the
/// top, the leading XML-documentation comment (when present), attributes, signature, and body.
/// </summary>
public sealed class DeclarationReader
{
    /// <summary>Returns the declaration line range of every declaration of every symbol matching the name.</summary>
    public async Task<IReadOnlyList<DeclarationRange>> GetRangesAsync(
        Solution solution,
        string symbolName,
        CancellationToken cancellationToken = default)
    {
        HashSet<string> handwrittenPaths = LocationDescriptor.HandwrittenPaths(solution);
        IReadOnlyList<ISymbol> symbols =
            await SymbolResolver.ResolveAsync(solution, symbolName, cancellationToken).ConfigureAwait(false);

        List<DeclarationRange> results = [];
        foreach (ISymbol symbol in symbols)
        {
            foreach (DeclarationSpan span in DeclarationSpans(symbol, cancellationToken))
            {
                Placement placement = Describe(span, handwrittenPaths);
                results.Add(new DeclarationRange(
                    symbol.ToDisplayString(),
                    placement.FilePath,
                    placement.StartLine,
                    placement.EndLine,
                    placement.Origin,
                    placement.GeneratedFilePath));
            }
        }

        return results;
    }

    /// <summary>The resolved location of a declaration span, shared by the range and source results.</summary>
    private readonly record struct Placement(
        string FilePath,
        int StartLine,
        int EndLine,
        ReferenceOrigin Origin,
        string? GeneratedFilePath);

    private static Placement Describe(DeclarationSpan span, HashSet<string> handwrittenPaths)
    {
        FileLinePositionSpan mapped = span.Tree.GetMappedLineSpan(span.Span);
        ReferenceOrigin origin = LocationDescriptor.OriginOf(span.Tree, handwrittenPaths);
        return new Placement(
            mapped.Path,
            mapped.StartLinePosition.Line + 1,
            mapped.EndLinePosition.Line + 1,
            origin,
            origin == ReferenceOrigin.Generated ? span.Tree.FilePath : null);
    }

    /// <summary>A single declaration's syntax tree and the text span covering doc comment + declaration.</summary>
    private readonly record struct DeclarationSpan(SyntaxTree Tree, TextSpan Span);

    /// <summary>
    /// Yields, for each declaring syntax reference of a symbol, the span covering the full
    /// declaration. Attributes are part of the declaration node's span; a leading XML-documentation
    /// comment is included by extending the start up to it (only the doc comment, not arbitrary
    /// preceding comments or blank lines).
    /// </summary>
    private static IEnumerable<DeclarationSpan> DeclarationSpans(
        ISymbol symbol,
        CancellationToken cancellationToken)
    {
        foreach (SyntaxReference reference in symbol.DeclaringSyntaxReferences)
        {
            SyntaxNode node = DeclarationNode(reference.GetSyntax(cancellationToken));
            int start = node.Span.Start;

            foreach (SyntaxTrivia trivia in node.GetLeadingTrivia())
            {
                if (trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia)
                    || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
                {
                    start = trivia.FullSpan.Start;
                    break;
                }
            }

            yield return new DeclarationSpan(node.SyntaxTree, TextSpan.FromBounds(start, node.Span.End));
        }
    }

    /// <summary>
    /// Maps a declaring node to the node whose span is the meaningful declaration. Field and
    /// event-field symbols declare a single variable, but the declaration the caller wants is the
    /// whole field statement (modifiers, type, and any attributes).
    /// </summary>
    private static SyntaxNode DeclarationNode(SyntaxNode node) =>
        node is VariableDeclaratorSyntax
            ? node.FirstAncestorOrSelf<BaseFieldDeclarationSyntax>() ?? node
            : node;
}
