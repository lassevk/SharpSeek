using System.ComponentModel;

using Microsoft.CodeAnalysis;

using ModelContextProtocol.Server;

using SharpSeek.Engine;

namespace SharpSeek.Server;

/// <summary>
/// MCP tools that analyse the loaded .NET solution as a whole, plus project activation.
/// </summary>
[McpServerToolType]
internal sealed class CodeAnalysisTools
{
    [McpServerTool(Name = "find_unused_symbols")]
    [Description(
        "Find members (methods, properties, fields, events) with no references anywhere - i.e. " +
        "dead code. References inside source-generated code are counted, so a member used only " +
        "from generated code (e.g. a Blazor @onclick handler) is NOT reported, unlike generic " +
        "tools. scope='private' (default) reports only private members and is safe. " +
        "scope='solution' also reports internal/public members unused anywhere in the solution - " +
        "useful for application solutions, but VERIFY each result: it may be part of a library's " +
        "public API or used via reflection/DI/serialization. Scans the whole solution, so it can " +
        "be slow on large solutions.")]
    public static async Task<IReadOnlyList<UnusedSymbolDto>> FindUnusedSymbolsAsync(
        ProjectSession session,
        DeadCodeFinder finder,
        [Description("'private' (default, safe) or 'solution' (broader, needs verification).")]
        string scope = "private",
        CancellationToken cancellationToken = default)
    {
        DeadCodeScope deadCodeScope = string.Equals(scope, "solution", StringComparison.OrdinalIgnoreCase)
            ? DeadCodeScope.Solution
            : DeadCodeScope.Private;

        Solution solution = await session.GetSolutionAsync(cancellationToken);
        IReadOnlyList<UnusedSymbol> results =
            await finder.FindUnusedSymbolsAsync(solution, deadCodeScope, cancellationToken);

        return [.. results.Select(UnusedSymbolDto.From)];
    }

    [McpServerTool(Name = "get_generated_document")]
    [Description(
        "Show the C# produced by source generators for a file. Matches by generated file name/path " +
        "or by the originating source file (e.g. \"Calendar.razor\" returns the generated " +
        "Calendar_razor.g.cs). Useful for inspecting Blazor BuildRenderTree output.")]
    public static async Task<IReadOnlyList<GeneratedDocumentDto>> GetGeneratedDocumentAsync(
        ProjectSession session,
        ProjectInspector inspector,
        [Description("A generated file name, path fragment, or originating source file name.")]
        string query,
        CancellationToken cancellationToken)
    {
        Solution solution = await session.GetSolutionAsync(cancellationToken);
        IReadOnlyList<GeneratedDocumentInfo> results =
            await inspector.GetGeneratedDocumentsAsync(solution, query, cancellationToken);

        return [.. results.Select(GeneratedDocumentDto.From)];
    }

    [McpServerTool(Name = "list_generators")]
    [Description(
        "List the source generators that ran across the solution and how many documents each " +
        "produced. Useful for confirming the Razor generator is active.")]
    public static async Task<IReadOnlyList<GeneratorDto>> ListGeneratorsAsync(
        ProjectSession session,
        ProjectInspector inspector,
        CancellationToken cancellationToken)
    {
        Solution solution = await session.GetSolutionAsync(cancellationToken);
        IReadOnlyList<GeneratorInfo> results =
            await inspector.ListGeneratorsAsync(solution, cancellationToken);

        return [.. results.Select(GeneratorDto.From)];
    }

    [McpServerTool(Name = "solution_overview")]
    [Description(
        "Get high-level information about the loaded solution: its file path and, for each " +
        "project, name, assembly name, language, document counts (hand-written, additional, " +
        "generated), metadata reference count, and project references.")]
    public static async Task<SolutionOverviewDto> SolutionOverviewAsync(
        ProjectSession session,
        ProjectInspector inspector,
        CancellationToken cancellationToken)
    {
        Solution solution = await session.GetSolutionAsync(cancellationToken);
        SolutionOverview overview = await inspector.GetOverviewAsync(solution, cancellationToken);

        return SolutionOverviewDto.From(overview);
    }

    [McpServerTool(Name = "project_dependencies")]
    [Description(
        "Analyse project-to-project dependencies across the solution by ACTUAL usage. For each " +
        "project, returns its declared <ProjectReference>s, the references it actually uses (a " +
        "symbol from the other project is referenced, including in generated code), and declared " +
        "references that are never used (dead references worth removing). Also lists each " +
        "project's dependents (the projects that use it) for impact analysis. Scans every " +
        "project's semantic model, so it can be slow on large solutions.")]
    public static async Task<IReadOnlyList<ProjectDependenciesDto>> ProjectDependenciesAsync(
        ProjectSession session,
        DependencyAnalyzer analyzer,
        CancellationToken cancellationToken)
    {
        Solution solution = await session.GetSolutionAsync(cancellationToken);
        IReadOnlyList<ProjectDependencies> results =
            await analyzer.AnalyzeAsync(solution, cancellationToken);

        return [.. results.Select(ProjectDependenciesDto.From)];
    }

    [McpServerTool(Name = "get_diagnostics")]
    [Description(
        "Get compiler diagnostics (errors, warnings) for the solution, or for a single file when " +
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
        Solution solution = await session.GetSolutionAsync(cancellationToken);
        IReadOnlyList<DiagnosticInfo> results =
            await reader.GetDiagnosticsAsync(solution, filePath, minimumSeverity, cancellationToken);

        return [.. results.Select(DiagnosticDto.From)];
    }

    [McpServerTool(Name = "activate_project")]
    [Description(
        "Switch the .NET solution or project this server analyses. Pass a path to a .sln/.slnx/" +
        ".csproj file, or a directory (a solution or project will be discovered by walking up). " +
        "Returns the loaded path.")]
    public static async Task<string> ActivateProjectAsync(
        ProjectSession session,
        [Description("Path to a .sln/.slnx/.csproj file or a directory.")] string path,
        CancellationToken cancellationToken)
    {
        string loaded = await session.ActivateAsync(path, cancellationToken);
        return $"Activated: {loaded}";
    }
}
