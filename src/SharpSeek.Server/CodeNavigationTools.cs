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

    [McpServerTool(Name = "go_to_definition")]
    [Description(
        "Find where a C# symbol (by name) is declared. Returns the declaration location(s); a " +
        "name can resolve to more than one symbol (e.g. overloads or partial declarations).")]
    public static async Task<IReadOnlyList<SymbolLocationsResult>> GoToDefinitionAsync(
        ProjectSession session,
        SymbolNavigator navigator,
        [Description("The simple (unqualified) name of the symbol, e.g. \"Calendar\".")]
        string symbolName,
        CancellationToken cancellationToken)
    {
        Project project = await session.GetProjectAsync(cancellationToken);
        IReadOnlyList<SymbolLocations> results =
            await navigator.GoToDefinitionAsync(project, symbolName, cancellationToken);

        return [.. results.Select(SymbolLocationsResult.From)];
    }

    [McpServerTool(Name = "find_implementations")]
    [Description(
        "Find the implementations of an interface or abstract member (by name). For an interface " +
        "type this returns the implementing types; for an interface member, the implementing " +
        "members. Each result includes the implementor's declaration location(s).")]
    public static async Task<IReadOnlyList<SymbolLocationsResult>> FindImplementationsAsync(
        ProjectSession session,
        SymbolNavigator navigator,
        [Description("The simple (unqualified) name of the interface/abstract symbol, e.g. \"IGreeter\".")]
        string symbolName,
        CancellationToken cancellationToken)
    {
        Project project = await session.GetProjectAsync(cancellationToken);
        IReadOnlyList<SymbolLocations> results =
            await navigator.FindImplementationsAsync(project, symbolName, cancellationToken);

        return [.. results.Select(SymbolLocationsResult.From)];
    }

    [McpServerTool(Name = "type_hierarchy")]
    [Description(
        "Show the base types and derived types of a type (by name). Base types include the base " +
        "class chain and directly implemented interfaces; derived types include subclasses (or, " +
        "for an interface, implementors and sub-interfaces).")]
    public static async Task<IReadOnlyList<TypeHierarchyResult>> TypeHierarchyAsync(
        ProjectSession session,
        SymbolNavigator navigator,
        [Description("The simple (unqualified) name of the type, e.g. \"Dog\".")]
        string typeName,
        CancellationToken cancellationToken)
    {
        Project project = await session.GetProjectAsync(cancellationToken);
        IReadOnlyList<TypeHierarchy> results =
            await navigator.TypeHierarchyAsync(project, typeName, cancellationToken);

        return [.. results.Select(TypeHierarchyResult.From)];
    }
}
