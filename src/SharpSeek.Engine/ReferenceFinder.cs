using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;

namespace SharpSeek.Engine;

/// <summary>
/// Whether a location lives in a hand-written source file or in source-generated code.
/// </summary>
public enum ReferenceOrigin
{
    /// <summary>The location is in a hand-written document.</summary>
    Handwritten,

    /// <summary>The location is in a source-generated document.</summary>
    Generated,
}

/// <summary>
/// A single resolved location, already mapped back to its original file via any <c>#line</c>
/// information the generator emitted.
/// </summary>
/// <param name="FilePath">
/// The mapped file path. For generated locations carrying <c>#line</c> mapping (such as a Razor
/// <c>BuildRenderTree</c>) this is the original file, for example the <c>.razor</c> file.
/// </param>
/// <param name="Line">1-based line number in <paramref name="FilePath"/>.</param>
/// <param name="Column">1-based column number in <paramref name="FilePath"/>.</param>
/// <param name="Origin">Whether the underlying location is hand-written or generated.</param>
/// <param name="GeneratedFilePath">
/// When <paramref name="Origin"/> is <see cref="ReferenceOrigin.Generated"/>, the path of the
/// generated document the location physically lives in; otherwise <c>null</c>.
/// </param>
public sealed record ReferenceLocationInfo(
    string FilePath,
    int Line,
    int Column,
    ReferenceOrigin Origin,
    string? GeneratedFilePath);

/// <summary>
/// How a data symbol (field, property, local, parameter, event) is used at a reference.
/// </summary>
public enum SymbolUsage
{
    /// <summary>The value is read but not modified.</summary>
    Read,

    /// <summary>The value is assigned but its prior value is not read (e.g. <c>x = 1</c>, <c>out</c>).</summary>
    Write,

    /// <summary>The value is both read and written (e.g. <c>x += 1</c>, <c>x++</c>, <c>ref</c>).</summary>
    ReadWrite,
}

/// <summary>
/// A single reference to a symbol, with the metadata Roslyn already derived about how it was used.
/// </summary>
/// <param name="Location">Where the reference is, mapped back to original source.</param>
/// <param name="Usage">
/// Read/write classification for data symbols; <c>null</c> when it does not apply (method calls,
/// type references, <c>nameof</c>/<c>typeof</c>, etc.).
/// </param>
/// <param name="IsImplicit">
/// Whether the reference is implicit (no explicit mention in source, e.g. a <c>foreach</c> binding
/// to <c>GetEnumerator</c> or an implicit <c>Deconstruct</c>).
/// </param>
/// <param name="Alias">The alias name when the symbol was referenced through a <c>using X = ...</c> alias; otherwise <c>null</c>.</param>
/// <param name="CandidateReason">
/// When the reference is a candidate (not an exact bind), the reason Roslyn reported; otherwise
/// <c>null</c>.
/// </param>
public sealed record ReferenceInfo(
    ReferenceLocationInfo Location,
    SymbolUsage? Usage,
    bool IsImplicit,
    string? Alias,
    string? CandidateReason);

/// <summary>
/// The references found for a single resolved symbol.
/// </summary>
/// <param name="SymbolDisplay">A human-readable description of the symbol.</param>
/// <param name="SymbolKind">The kind of symbol (e.g. <c>Method</c>, <c>Field</c>, <c>Property</c>).</param>
/// <param name="Definitions">Where the symbol is declared.</param>
/// <param name="References">Where the symbol is referenced.</param>
public sealed record SymbolReferences(
    string SymbolDisplay,
    string SymbolKind,
    IReadOnlyList<ReferenceLocationInfo> Definitions,
    IReadOnlyList<ReferenceInfo> References);

/// <summary>
/// Finds references to a symbol across a loaded project, including references that live inside
/// source-generated code, and maps each hit back to its original location.
/// </summary>
public sealed class ReferenceFinder
{
    /// <summary>
    /// Resolves every source-declared symbol matching <paramref name="symbolName"/> across the
    /// solution and finds its references, including references inside generated documents.
    /// </summary>
    /// <param name="solution">A solution previously loaded via <see cref="LiveWorkspace"/>.</param>
    /// <param name="symbolName">The simple (unqualified) symbol name to resolve.</param>
    /// <param name="cancellationToken">Token used to cancel the search.</param>
    public async Task<IReadOnlyList<SymbolReferences>> FindReferencesAsync(
        Solution solution,
        string symbolName,
        CancellationToken cancellationToken = default)
    {
        HashSet<string> handwrittenPaths = LocationDescriptor.HandwrittenPaths(solution);

        IEnumerable<ISymbol> declarations = await SymbolFinder
            .FindSourceDeclarationsAsync(solution, symbolName, ignoreCase: false, cancellationToken)
            .ConfigureAwait(false);

        List<SymbolReferences> results = [];

        foreach (ISymbol symbol in declarations)
        {
            List<ReferenceLocationInfo> definitions =
                LocationDescriptor.Definitions(symbol, handwrittenPaths);

            IEnumerable<ReferencedSymbol> referenced = await SymbolFinder
                .FindReferencesAsync(symbol, solution, cancellationToken)
                .ConfigureAwait(false);

            // Semantic models are reused across references in the same document; the compilation is
            // already built by the reference search above, so classifying usage is incremental.
            Dictionary<DocumentId, (SemanticModel Model, SyntaxNode Root)?> modelCache = [];

            List<ReferenceInfo> references = [];
            foreach (ReferencedSymbol referencedSymbol in referenced)
            {
                foreach (ReferenceLocation reference in referencedSymbol.Locations)
                {
                    Location location = reference.Location;
                    if (location is not { IsInSource: true, SourceTree: { } tree })
                    {
                        continue;
                    }

                    ReferenceLocationInfo info =
                        LocationDescriptor.Describe(tree, location.SourceSpan, handwrittenPaths);

                    SymbolUsage? usage = await ClassifyUsageAsync(
                        reference.Document, tree, location.SourceSpan, modelCache, cancellationToken)
                        .ConfigureAwait(false);

                    references.Add(new ReferenceInfo(
                        info,
                        usage,
                        reference.IsImplicit,
                        reference.Alias?.Name,
                        reference.CandidateReason == CandidateReason.None
                            ? null
                            : reference.CandidateReason.ToString()));
                }
            }

            results.Add(new SymbolReferences(
                symbol.ToDisplayString(), symbol.Kind.ToString(), definitions, references));
        }

        return results;
    }

    private static async Task<SymbolUsage?> ClassifyUsageAsync(
        Document? document,
        SyntaxTree tree,
        TextSpan span,
        Dictionary<DocumentId, (SemanticModel Model, SyntaxNode Root)?> cache,
        CancellationToken cancellationToken)
    {
        if (document is null)
        {
            return null;
        }

        if (!cache.TryGetValue(document.Id, out (SemanticModel Model, SyntaxNode Root)? entry))
        {
            SemanticModel? model =
                await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            SyntaxNode? root = await tree.GetRootAsync(cancellationToken).ConfigureAwait(false);
            entry = model is not null && root is not null ? (model, root) : null;
            cache[document.Id] = entry;
        }

        return entry is { } value
            ? UsageClassifier.Classify(value.Model, value.Root, span, cancellationToken)
            : null;
    }
}
