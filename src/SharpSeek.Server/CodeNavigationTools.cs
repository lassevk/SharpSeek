using System.ComponentModel;

using Microsoft.CodeAnalysis;

using ModelContextProtocol.Server;

using SharpSeek.Engine;

namespace SharpSeek.Server;

/// <summary>
/// MCP tools for navigating the loaded .NET project.
/// </summary>
[McpServerToolType]
internal sealed class CodeNavigationTools
{
    [McpServerTool(Name = "find_references")]
    [Description(
        "Find all references to a C# symbol by its name across the loaded project, including " +
        "references that live inside source-generated code (for example a Blazor @onclick " +
        "handler wired up in generated BuildRenderTree code). Each reference is mapped back to " +
        "its original source location and tagged as handwritten or generated.")]
    public static async Task<IReadOnlyList<FindReferencesResult>> FindReferencesAsync(
        ProjectSession session,
        ReferenceFinder finder,
        [Description("The simple (unqualified) name of the symbol, e.g. \"ShowPreviousYearAsync\".")]
        string symbolName,
        CancellationToken cancellationToken)
    {
        Project project = await session.GetProjectAsync(cancellationToken);
        IReadOnlyList<SymbolReferences> results =
            await finder.FindReferencesAsync(project, symbolName, cancellationToken);

        return [.. results.Select(FindReferencesResult.From)];
    }
}
