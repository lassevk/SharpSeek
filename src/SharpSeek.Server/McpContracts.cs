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
