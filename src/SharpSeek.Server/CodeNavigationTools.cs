using System.ComponentModel;

using Microsoft.CodeAnalysis;

using ModelContextProtocol.Server;

using SharpSeek.Engine;

namespace SharpSeek.Server;

/// <summary>
/// MCP tools for navigating the loaded .NET solution.
/// </summary>
[McpServerToolType]
internal sealed class CodeNavigationTools
{
    [McpServerTool(Name = "find_references")]
    [Description(
        "Find all references to a C# symbol by its name across the loaded solution, including " +
        "references that live inside source-generated code (for example a Blazor @onclick " +
        "handler wired up in generated BuildRenderTree code). Each reference is mapped back to " +
        "its original source location and tagged as handwritten or generated. References also " +
        "carry how the symbol was used: 'usage' is read/write/readwrite for data symbols (fields, " +
        "properties, locals, parameters) and absent for method calls or type references. For a " +
        "write of a constant value, 'assignedConstant' holds that value (e.g. true, 42, or the " +
        "constant null) so a query like 'where is this set to null' is answered directly; it is " +
        "absent - never null - when the assigned value is not a constant. 'assignedType' gives the " +
        "static type assigned at a write (implicit conversions peeled): for value types 'int' vs " +
        "'int?' distinguishes a provably non-null write from a possibly-null one. 'role' names the " +
        "distinctive syntactic forms - nameof, typeof, construction (new X), attribute, invocation, " +
        "methodGroup - and is absent for an ordinary reference. 'implicit', 'alias', and " +
        "'candidateReason' appear only when applicable. The resolved symbol's 'kind' (Method, " +
        "Field, Property, ...) is included.")]
    public static async Task<IReadOnlyList<FindReferencesResult>> FindReferencesAsync(
        ProjectSession session,
        ReferenceFinder finder,
        [Description("The simple (unqualified) name of the symbol, e.g. \"ShowPreviousYearAsync\".")]
        string symbolName,
        CancellationToken cancellationToken)
    {
        Solution solution = await session.GetSolutionAsync(cancellationToken);
        IReadOnlyList<SymbolReferences> results =
            await finder.FindReferencesAsync(solution, symbolName, cancellationToken);

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
        Solution solution = await session.GetSolutionAsync(cancellationToken);
        IReadOnlyList<SymbolLocations> results =
            await navigator.GoToDefinitionAsync(solution, symbolName, cancellationToken);

        return [.. results.Select(SymbolLocationsResult.From)];
    }

    [McpServerTool(Name = "get_symbol_range")]
    [Description(
        "Get the full declaration line range of a C# symbol so you can read just that span " +
        "yourself. Returns, per declaration, the file and 1-based start/end line; read offset=" +
        "startLine, limit=endLine-startLine+1 to get exactly the member. The range covers the " +
        "leading XML-doc comment (when present), attributes, signature, and body. The name can be " +
        "a simple name, Type.Member, or a fully-qualified name to disambiguate; overloads and " +
        "partial declarations each return their own entry. Generated declarations are mapped back " +
        "to source where #line allows and tagged generated.")]
    public static async Task<IReadOnlyList<SymbolRangeDto>> GetSymbolRangeAsync(
        ProjectSession session,
        DeclarationReader reader,
        [Description("Symbol name: simple (\"Add\"), Type.Member (\"DeclarationSamples.Add\"), or fully-qualified.")]
        string symbolName,
        CancellationToken cancellationToken)
    {
        Solution solution = await session.GetSolutionAsync(cancellationToken);
        IReadOnlyList<DeclarationRange> results =
            await reader.GetRangesAsync(solution, symbolName, cancellationToken);

        return [.. results.Select(SymbolRangeDto.From)];
    }

    [McpServerTool(Name = "get_symbol_source")]
    [Description(
        "Get the source text of a C# symbol's declaration(s) in one call. Unlike get_symbol_range " +
        "(which returns only a line range for you to read the file yourself), this returns the " +
        "code directly - and it can return source-generated code (e.g. a Blazor BuildRenderTree " +
        "body) that your own file reader cannot open. Each declaration covers the leading XML-doc " +
        "comment (when present), attributes, signature, and body. The name can be a simple name, " +
        "Type.Member, or a fully-qualified name; overloads and partial declarations each return " +
        "their own entry. Prefer get_symbol_range when you intend to edit hand-written code.")]
    public static async Task<IReadOnlyList<SymbolSourceDto>> GetSymbolSourceAsync(
        ProjectSession session,
        DeclarationReader reader,
        [Description("Symbol name: simple (\"Add\"), Type.Member (\"DeclarationSamples.Add\"), or fully-qualified.")]
        string symbolName,
        [Description("Max lines of source per declaration (default 400; 0 or less means no cap).")]
        int maxLines = DeclarationReader.DefaultMaxLines,
        CancellationToken cancellationToken = default)
    {
        Solution solution = await session.GetSolutionAsync(cancellationToken);
        IReadOnlyList<DeclarationSource> results =
            await reader.GetSourceAsync(solution, symbolName, maxLines, cancellationToken);

        return [.. results.Select(SymbolSourceDto.From)];
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
        Solution solution = await session.GetSolutionAsync(cancellationToken);
        IReadOnlyList<SymbolLocations> results =
            await navigator.FindImplementationsAsync(solution, symbolName, cancellationToken);

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
        Solution solution = await session.GetSolutionAsync(cancellationToken);
        IReadOnlyList<TypeHierarchy> results =
            await navigator.TypeHierarchyAsync(solution, typeName, cancellationToken);

        return [.. results.Select(TypeHierarchyResult.From)];
    }

    [McpServerTool(Name = "find_overrides")]
    [Description(
        "Find the override relationships of a member (by name): the members that override it " +
        "(down the hierarchy) and the members it overrides (up the hierarchy). Works for virtual/" +
        "abstract/override methods, properties, and events.")]
    public static async Task<IReadOnlyList<OverrideHierarchyResult>> FindOverridesAsync(
        ProjectSession session,
        SymbolNavigator navigator,
        [Description("The simple (unqualified) name of the member, e.g. \"Speak\".")]
        string symbolName,
        CancellationToken cancellationToken)
    {
        Solution solution = await session.GetSolutionAsync(cancellationToken);
        IReadOnlyList<OverrideHierarchy> results =
            await navigator.FindOverridesAsync(solution, symbolName, cancellationToken);

        return [.. results.Select(OverrideHierarchyResult.From)];
    }

    [McpServerTool(Name = "call_hierarchy")]
    [Description(
        "Show the call hierarchy of a method (by name): incoming callers (who calls it, including " +
        "calls from generated code, mapped back to source) and outgoing calls (what it calls). " +
        "Each entry includes the call site location.")]
    public static async Task<IReadOnlyList<CallHierarchyResult>> CallHierarchyAsync(
        ProjectSession session,
        CallHierarchyAnalyzer analyzer,
        [Description("The simple (unqualified) name of the method, e.g. \"ShowMonthAsync\".")]
        string methodName,
        CancellationToken cancellationToken)
    {
        Solution solution = await session.GetSolutionAsync(cancellationToken);
        IReadOnlyList<CallHierarchy> results =
            await analyzer.AnalyzeAsync(solution, methodName, cancellationToken);

        return [.. results.Select(CallHierarchyResult.From)];
    }
}
