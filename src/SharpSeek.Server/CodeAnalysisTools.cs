using System.ComponentModel;

using Microsoft.CodeAnalysis;

using ModelContextProtocol.Server;

using SharpSeek.Engine;

namespace SharpSeek.Server;

/// <summary>
/// MCP tools that analyse the loaded project as a whole.
/// </summary>
[McpServerToolType]
internal sealed class CodeAnalysisTools
{
    [McpServerTool(Name = "find_unused_symbols")]
    [Description(
        "Find private members (methods, properties, fields, events) with no references anywhere " +
        "in the project — i.e. dead code. References inside source-generated code are counted, so " +
        "a private member used only from generated code (e.g. a Blazor @onclick handler) is NOT " +
        "reported, unlike generic tools. Scans the whole project, so it can be slow on large " +
        "projects. Note: members reached only via reflection or serialization may be reported.")]
    public static async Task<IReadOnlyList<UnusedSymbolDto>> FindUnusedSymbolsAsync(
        ProjectSession session,
        DeadCodeFinder finder,
        CancellationToken cancellationToken)
    {
        Project project = await session.GetProjectAsync(cancellationToken);
        IReadOnlyList<UnusedSymbol> results =
            await finder.FindUnusedPrivateSymbolsAsync(project, cancellationToken);

        return [.. results.Select(UnusedSymbolDto.From)];
    }
}
