using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace SharpSeek.Engine;

/// <summary>
/// Shared helpers for turning Roslyn locations into <see cref="ReferenceLocationInfo"/>, including
/// the hand-written-vs-generated classification and the <c>#line</c> mapping back to original
/// source. Used by every navigation feature so they report locations consistently.
/// </summary>
internal static class LocationDescriptor
{
    /// <summary>Builds the set of hand-written document paths for a project.</summary>
    public static HashSet<string> HandwrittenPaths(Project project)
    {
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        foreach (Document document in project.Documents)
        {
            if (document.FilePath is not null)
            {
                paths.Add(document.FilePath);
            }
        }

        return paths;
    }

    /// <summary>Describes a span within a syntax tree, mapping it back to its original location.</summary>
    public static ReferenceLocationInfo Describe(
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

    /// <summary>Returns the in-source declaration locations of a symbol.</summary>
    public static List<ReferenceLocationInfo> Definitions(ISymbol symbol, HashSet<string> handwrittenPaths)
    {
        List<ReferenceLocationInfo> definitions = [];
        foreach (Location location in symbol.Locations)
        {
            if (location is { IsInSource: true, SourceTree: { } tree })
            {
                definitions.Add(Describe(tree, location.SourceSpan, handwrittenPaths));
            }
        }

        return definitions;
    }
}
