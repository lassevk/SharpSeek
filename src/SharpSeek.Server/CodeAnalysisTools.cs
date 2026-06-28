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

    [McpServerTool(Name = "project_overview")]
    [Description(
        "Get high-level information about the loaded project: name, assembly name, language, " +
        "file path, document counts (hand-written, additional, generated), metadata reference " +
        "count, and project references.")]
    public static async Task<ProjectOverviewDto> ProjectOverviewAsync(
        ProjectSession session,
        ProjectInspector inspector,
        CancellationToken cancellationToken)
    {
        Project project = await session.GetProjectAsync(cancellationToken);
        ProjectOverview overview = await inspector.GetOverviewAsync(project, cancellationToken);

        return ProjectOverviewDto.From(overview);
    }

    [McpServerTool(Name = "get_diagnostics")]
    [Description(
        "Get compiler diagnostics (errors, warnings) for the project, or for a single file when " +
        "filePath is given. minimumSeverity is one of error, warning (default), info, hidden. " +
        "Diagnostics in generated code are mapped back to their original location.")]
    public static async Task<IReadOnlyList<DiagnosticDto>> GetDiagnosticsAsync(
        ProjectSession session,
        DiagnosticReader reader,
        [Description("Optional path (absolute or path suffix) to restrict diagnostics to one file.")]
        string? filePath = null,
        [Description("Minimum severity: error, warning (default), info, or hidden.")]
        string? minimumSeverity = null,
        CancellationToken cancellationToken = default)
    {
        Project project = await session.GetProjectAsync(cancellationToken);
        IReadOnlyList<DiagnosticInfo> results =
            await reader.GetDiagnosticsAsync(project, filePath, minimumSeverity, cancellationToken);

        return [.. results.Select(DiagnosticDto.From)];
    }
}
