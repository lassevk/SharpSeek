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
