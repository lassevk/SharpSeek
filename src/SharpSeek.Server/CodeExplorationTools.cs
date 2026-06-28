using System.ComponentModel;

using Microsoft.CodeAnalysis;

using ModelContextProtocol.Server;

using SharpSeek.Engine;

namespace SharpSeek.Server;

/// <summary>
/// Read-only MCP tools for exploring the loaded .NET project.
/// </summary>
[McpServerToolType]
internal sealed class CodeExplorationTools
{
    [McpServerTool(Name = "search_symbols")]
    [Description(
        "Search the project's symbols by name pattern (supports substring and camel-case " +
        "matching), e.g. \"Greeter\" or \"FRA\" for FindReferencesAsync. Returns up to a limited " +
        "number of matches with their kind and primary location.")]
    public static async Task<IReadOnlyList<SymbolMatchDto>> SearchSymbolsAsync(
        ProjectSession session,
        SymbolExplorer explorer,
        [Description("The name or pattern to search for.")] string query,
        [Description("Maximum number of results to return (default 50).")] int max = 50,
        CancellationToken cancellationToken = default)
    {
        Project project = await session.GetProjectAsync(cancellationToken);
        IReadOnlyList<SymbolMatch> results = await explorer.SearchSymbolsAsync(
            project, query, max <= 0 ? 50 : max, cancellationToken);

        return [.. results.Select(SymbolMatchDto.From)];
    }

    [McpServerTool(Name = "find_symbol_at_position")]
    [Description(
        "Resolve the symbol at a position in a C# source file (1-based line and column) and return " +
        "its details. Useful when you have an editor location rather than a name. Works on C# (.cs) " +
        "files.")]
    public static async Task<IReadOnlyList<SymbolInfoDto>> FindSymbolAtPositionAsync(
        ProjectSession session,
        SymbolExplorer explorer,
        [Description("Path to the C# file (absolute or path suffix).")] string filePath,
        [Description("1-based line number.")] int line,
        [Description("1-based column number.")] int column,
        CancellationToken cancellationToken)
    {
        Project project = await session.GetProjectAsync(cancellationToken);
        IReadOnlyList<SymbolDetails> results =
            await explorer.ResolveSymbolAtAsync(project, filePath, line, column, cancellationToken);

        return [.. results.Select(SymbolInfoDto.From)];
    }

    [McpServerTool(Name = "get_symbol_info")]
    [Description(
        "Get details about a symbol (by name): its kind, accessibility, containing type, XML " +
        "documentation comment, and declaration location(s).")]
    public static async Task<IReadOnlyList<SymbolInfoDto>> GetSymbolInfoAsync(
        ProjectSession session,
        SymbolExplorer explorer,
        [Description("The simple (unqualified) name of the symbol.")] string symbolName,
        CancellationToken cancellationToken)
    {
        Project project = await session.GetProjectAsync(cancellationToken);
        IReadOnlyList<SymbolDetails> results =
            await explorer.GetSymbolInfoAsync(project, symbolName, cancellationToken);

        return [.. results.Select(SymbolInfoDto.From)];
    }

    [McpServerTool(Name = "find_literal_usages")]
    [Description(
        "Find where a literal value (string, number, or char) appears in the project, including " +
        "literals baked into source-generated code. Locations are mapped back to source where the " +
        "generator provides mapping (static Razor markup stays in the generated file).")]
    public static async Task<IReadOnlyList<LocationDto>> FindLiteralUsagesAsync(
        ProjectSession session,
        SymbolExplorer explorer,
        [Description("The literal value to find, e.g. \"Hello\" or \"42\".")] string value,
        CancellationToken cancellationToken)
    {
        Project project = await session.GetProjectAsync(cancellationToken);
        IReadOnlyList<ReferenceLocationInfo> results =
            await explorer.FindLiteralUsagesAsync(project, value, cancellationToken);

        return [.. results.Select(LocationDto.From)];
    }

    [McpServerTool(Name = "document_outline")]
    [Description(
        "List the types and members declared in a single document, with their kind and line " +
        "number. The file is identified by its path (absolute, or any suffix of the path).")]
    public static async Task<IReadOnlyList<OutlineItemDto>> DocumentOutlineAsync(
        ProjectSession session,
        SymbolExplorer explorer,
        [Description("Path to the source file, e.g. \"Components/Pages/Calendar.razor.cs\".")]
        string filePath,
        CancellationToken cancellationToken)
    {
        Project project = await session.GetProjectAsync(cancellationToken);
        IReadOnlyList<OutlineItem> results =
            await explorer.DocumentOutlineAsync(project, filePath, cancellationToken);

        return [.. results.Select(OutlineItemDto.From)];
    }
}
