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
/// The references found for a single resolved symbol.
/// </summary>
/// <param name="SymbolDisplay">A human-readable description of the symbol.</param>
/// <param name="Definitions">Where the symbol is declared.</param>
/// <param name="References">Where the symbol is referenced.</param>
public sealed record SymbolReferences(
    string SymbolDisplay,
    IReadOnlyList<ReferenceLocationInfo> Definitions,
    IReadOnlyList<ReferenceLocationInfo> References);

/// <summary>
/// Finds references to a symbol across a loaded project, including references that live inside
/// source-generated code, and maps each hit back to its original location.
/// </summary>
public sealed class ReferenceFinder
{
    /// <summary>
    /// Resolves every source-declared symbol matching <paramref name="symbolName"/> in the
    /// project and finds its references across the whole solution, including generated documents.
    /// </summary>
    /// <param name="project">A project previously loaded via <see cref="WorkspaceLoader"/>.</param>
    /// <param name="symbolName">The simple (unqualified) symbol name to resolve.</param>
    /// <param name="cancellationToken">Token used to cancel the search.</param>
    public async Task<IReadOnlyList<SymbolReferences>> FindReferencesAsync(
        Project project,
        string symbolName,
        CancellationToken cancellationToken = default)
    {
        // Hand-written documents have their file paths registered on the project; anything else
        // a location resolves to (a generated tree) is therefore source-generated.
        HashSet<string> handwrittenPaths = new(StringComparer.OrdinalIgnoreCase);
        foreach (Document document in project.Documents)
        {
            if (document.FilePath is not null)
            {
                handwrittenPaths.Add(document.FilePath);
            }
        }

        IEnumerable<ISymbol> declarations = await SymbolFinder
            .FindSourceDeclarationsAsync(project, symbolName, ignoreCase: false, cancellationToken)
            .ConfigureAwait(false);

        Solution solution = project.Solution;
        List<SymbolReferences> results = [];

        foreach (ISymbol symbol in declarations)
        {
            List<ReferenceLocationInfo> definitions = [];
            foreach (Location location in symbol.Locations)
            {
                if (location is { IsInSource: true, SourceTree: { } tree })
                {
                    definitions.Add(Describe(tree, location.SourceSpan, handwrittenPaths));
                }
            }

            IEnumerable<ReferencedSymbol> referenced = await SymbolFinder
                .FindReferencesAsync(symbol, solution, cancellationToken)
                .ConfigureAwait(false);

            List<ReferenceLocationInfo> references = [];
            foreach (ReferencedSymbol referencedSymbol in referenced)
            {
                foreach (ReferenceLocation reference in referencedSymbol.Locations)
                {
                    Location location = reference.Location;
                    if (location is { IsInSource: true, SourceTree: { } tree })
                    {
                        references.Add(Describe(tree, location.SourceSpan, handwrittenPaths));
                    }
                }
            }

            results.Add(new SymbolReferences(symbol.ToDisplayString(), definitions, references));
        }

        return results;
    }

    private static ReferenceLocationInfo Describe(
        SyntaxTree tree,
        TextSpan span,
        HashSet<string> handwrittenPaths)
    {
        ReferenceOrigin origin = tree.FilePath is { } path && handwrittenPaths.Contains(path)
            ? ReferenceOrigin.Handwritten
            : ReferenceOrigin.Generated;

        // GetMappedLineSpan honours #line directives, so a hit inside generated code is reported
        // at its original location (for example the .razor line that declared the @onclick).
        FileLinePositionSpan mapped = tree.GetMappedLineSpan(span);

        return new ReferenceLocationInfo(
            mapped.Path,
            mapped.StartLinePosition.Line + 1,
            mapped.StartLinePosition.Character + 1,
            origin,
            origin == ReferenceOrigin.Generated ? tree.FilePath : null);
    }
}
