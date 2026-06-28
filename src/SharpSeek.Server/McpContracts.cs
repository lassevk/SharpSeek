using SharpSeek.Engine;

namespace SharpSeek.Server;

/// <summary>
/// The MCP-facing result for a single resolved symbol. Kept separate from the engine's types so
/// the wire contract is explicit and stable.
/// </summary>
internal sealed record FindReferencesResult(
    string Symbol,
    IReadOnlyList<LocationDto> Definitions,
    IReadOnlyList<LocationDto> References)
{
    public static FindReferencesResult From(SymbolReferences symbol) => new(
        symbol.SymbolDisplay,
        [.. symbol.Definitions.Select(LocationDto.From)],
        [.. symbol.References.Select(LocationDto.From)]);
}

/// <summary>The MCP-facing result for a symbol and a set of locations relevant to a query.</summary>
internal sealed record SymbolLocationsResult(string Symbol, IReadOnlyList<LocationDto> Locations)
{
    public static SymbolLocationsResult From(SymbolLocations symbol) => new(
        symbol.SymbolDisplay,
        [.. symbol.Locations.Select(LocationDto.From)]);
}

/// <summary>A type reference with its declaration location (null for metadata-only types).</summary>
internal sealed record TypeReferenceDto(string Type, LocationDto? Location)
{
    public static TypeReferenceDto From(TypeReference type) => new(
        type.Display,
        type.Location is null ? null : LocationDto.From(type.Location));
}

/// <summary>The MCP-facing override hierarchy of a member.</summary>
internal sealed record OverrideHierarchyResult(
    string Symbol,
    IReadOnlyList<SymbolLocationsResult> OverriddenBy,
    IReadOnlyList<SymbolLocationsResult> Overrides)
{
    public static OverrideHierarchyResult From(OverrideHierarchy hierarchy) => new(
        hierarchy.SymbolDisplay,
        [.. hierarchy.OverriddenBy.Select(SymbolLocationsResult.From)],
        [.. hierarchy.Overrides.Select(SymbolLocationsResult.From)]);
}

/// <summary>The MCP-facing base/derived type hierarchy for a type.</summary>
internal sealed record TypeHierarchyResult(
    string Type,
    IReadOnlyList<TypeReferenceDto> BaseTypes,
    IReadOnlyList<TypeReferenceDto> DerivedTypes)
{
    public static TypeHierarchyResult From(TypeHierarchy hierarchy) => new(
        hierarchy.TypeDisplay,
        [.. hierarchy.BaseTypes.Select(TypeReferenceDto.From)],
        [.. hierarchy.DerivedTypes.Select(TypeReferenceDto.From)]);
}

/// <summary>The MCP-facing result for a symbol search hit.</summary>
internal sealed record SymbolMatchDto(string Symbol, string Kind, LocationDto? Location)
{
    public static SymbolMatchDto From(SymbolMatch match) => new(
        match.Display,
        match.Kind,
        match.Location is null ? null : LocationDto.From(match.Location));
}

/// <summary>The MCP-facing details about a symbol.</summary>
internal sealed record SymbolInfoDto(
    string Symbol,
    string Kind,
    string Accessibility,
    string? ContainingType,
    string? Documentation,
    IReadOnlyList<LocationDto> Locations)
{
    public static SymbolInfoDto From(SymbolDetails info) => new(
        info.Display,
        info.Kind,
        info.Accessibility,
        info.ContainingType,
        info.Documentation,
        [.. info.Locations.Select(LocationDto.From)]);
}

/// <summary>The MCP-facing entry in a document outline.</summary>
internal sealed record OutlineItemDto(string Symbol, string Kind, int Line)
{
    public static OutlineItemDto From(OutlineItem item) => new(item.Display, item.Kind, item.Line);
}

/// <summary>The MCP-facing source-generated document.</summary>
internal sealed record GeneratedDocumentDto(string Name, string? FilePath, string Text)
{
    public static GeneratedDocumentDto From(GeneratedDocumentInfo document) =>
        new(document.Name, document.FilePath, document.Text);
}

/// <summary>The MCP-facing source-generator summary.</summary>
internal sealed record GeneratorDto(string Assembly, int Generators, int OutputDocuments)
{
    public static GeneratorDto From(GeneratorInfo generator) =>
        new(generator.Assembly, generator.GeneratorCount, generator.OutputDocuments);
}

/// <summary>The MCP-facing project dependency analysis.</summary>
internal sealed record ProjectDependenciesDto(
    string Project,
    IReadOnlyList<string> DeclaredReferences,
    IReadOnlyList<string> UsedReferences,
    IReadOnlyList<string> UnusedReferences,
    IReadOnlyList<string> Dependents)
{
    public static ProjectDependenciesDto From(ProjectDependencies dependencies) => new(
        dependencies.Project,
        dependencies.DeclaredReferences,
        dependencies.UsedReferences,
        dependencies.UnusedReferences,
        dependencies.Dependents);
}

/// <summary>The MCP-facing solution overview.</summary>
internal sealed record SolutionOverviewDto(string? FilePath, IReadOnlyList<ProjectOverviewDto> Projects)
{
    public static SolutionOverviewDto From(SolutionOverview overview) => new(
        overview.FilePath,
        [.. overview.Projects.Select(ProjectOverviewDto.From)]);
}

/// <summary>The MCP-facing project overview.</summary>
internal sealed record ProjectOverviewDto(
    string Name,
    string? AssemblyName,
    string Language,
    string? FilePath,
    int Documents,
    int AdditionalDocuments,
    int GeneratedDocuments,
    int MetadataReferences,
    IReadOnlyList<string> ProjectReferences)
{
    public static ProjectOverviewDto From(ProjectOverview overview) => new(
        overview.Name,
        overview.AssemblyName,
        overview.Language,
        overview.FilePath,
        overview.DocumentCount,
        overview.AdditionalDocumentCount,
        overview.GeneratedDocumentCount,
        overview.MetadataReferenceCount,
        overview.ProjectReferences);
}

/// <summary>The MCP-facing call hierarchy of a method.</summary>
internal sealed record CallHierarchyResult(
    string Method,
    IReadOnlyList<IncomingCallDto> Incoming,
    IReadOnlyList<OutgoingCallDto> Outgoing)
{
    public static CallHierarchyResult From(CallHierarchy hierarchy) => new(
        hierarchy.Method,
        [.. hierarchy.Incoming.Select(IncomingCallDto.From)],
        [.. hierarchy.Outgoing.Select(OutgoingCallDto.From)]);
}

/// <summary>An incoming caller and its call site(s).</summary>
internal sealed record IncomingCallDto(string Caller, IReadOnlyList<LocationDto> CallSites)
{
    public static IncomingCallDto From(IncomingCall call) => new(
        call.Caller,
        [.. call.CallSites.Select(LocationDto.From)]);
}

/// <summary>An outgoing callee and its call site.</summary>
internal sealed record OutgoingCallDto(string Callee, LocationDto CallSite)
{
    public static OutgoingCallDto From(OutgoingCall call) => new(
        call.Callee,
        LocationDto.From(call.CallSite));
}

/// <summary>The MCP-facing compiler diagnostic.</summary>
internal sealed record DiagnosticDto(string Id, string Severity, string Message, LocationDto? Location)
{
    public static DiagnosticDto From(DiagnosticInfo diagnostic) => new(
        diagnostic.Id,
        diagnostic.Severity,
        diagnostic.Message,
        diagnostic.Location is null ? null : LocationDto.From(diagnostic.Location));
}

/// <summary>The MCP-facing result for an unused (dead-code) symbol.</summary>
internal sealed record UnusedSymbolDto(string Symbol, string Kind, LocationDto Location)
{
    public static UnusedSymbolDto From(UnusedSymbol symbol) => new(
        symbol.Display,
        symbol.Kind,
        LocationDto.From(symbol.Location));
}

/// <summary>A single location in the MCP result.</summary>
/// <param name="File">The original file path (generated hits are mapped back to their source).</param>
/// <param name="Line">1-based line number.</param>
/// <param name="Column">1-based column number.</param>
/// <param name="Origin"><c>"handwritten"</c> or <c>"generated"</c>.</param>
/// <param name="GeneratedFile">For generated origins, the generated document the hit lives in.</param>
internal sealed record LocationDto(
    string File,
    int Line,
    int Column,
    string Origin,
    string? GeneratedFile)
{
    public static LocationDto From(ReferenceLocationInfo location) => new(
        location.FilePath,
        location.Line,
        location.Column,
        location.Origin == ReferenceOrigin.Generated ? "generated" : "handwritten",
        location.GeneratedFilePath);
}
