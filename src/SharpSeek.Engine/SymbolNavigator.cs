using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace SharpSeek.Engine;

/// <summary>A resolved symbol together with a set of locations relevant to a query.</summary>
public sealed record SymbolLocations(
    string SymbolDisplay,
    IReadOnlyList<ReferenceLocationInfo> Locations);

/// <summary>A type reference with its declaration location, if available in source.</summary>
public sealed record TypeReference(string Display, ReferenceLocationInfo? Location);

/// <summary>The base and derived types of a single type.</summary>
public sealed record TypeHierarchy(
    string TypeDisplay,
    IReadOnlyList<TypeReference> BaseTypes,
    IReadOnlyList<TypeReference> DerivedTypes);

/// <summary>The override relationships of a member.</summary>
/// <param name="SymbolDisplay">The member.</param>
/// <param name="OverriddenBy">Members that override it (down the hierarchy).</param>
/// <param name="Overrides">Members it overrides (up the hierarchy).</param>
public sealed record OverrideHierarchy(
    string SymbolDisplay,
    IReadOnlyList<SymbolLocations> OverriddenBy,
    IReadOnlyList<SymbolLocations> Overrides);

/// <summary>
/// Navigation queries over a loaded project: go-to-definition, find-implementations, and type
/// hierarchy. All resolve their target by symbol name and report locations consistently with
/// <see cref="ReferenceFinder"/>.
/// </summary>
public sealed class SymbolNavigator
{
    /// <summary>Returns the declaration location(s) of every symbol matching the name.</summary>
    public async Task<IReadOnlyList<SymbolLocations>> GoToDefinitionAsync(
        Project project,
        string symbolName,
        CancellationToken cancellationToken = default)
    {
        HashSet<string> handwrittenPaths = LocationDescriptor.HandwrittenPaths(project);
        IEnumerable<ISymbol> symbols = await ResolveAsync(project, symbolName, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. symbols.Select(symbol => new SymbolLocations(
                symbol.ToDisplayString(),
                LocationDescriptor.Definitions(symbol, handwrittenPaths)))
        ];
    }

    /// <summary>
    /// Returns the implementations of every interface/abstract symbol matching the name (the
    /// implementing types for an interface, or the implementing members for an interface member).
    /// </summary>
    public async Task<IReadOnlyList<SymbolLocations>> FindImplementationsAsync(
        Project project,
        string symbolName,
        CancellationToken cancellationToken = default)
    {
        HashSet<string> handwrittenPaths = LocationDescriptor.HandwrittenPaths(project);
        Solution solution = project.Solution;
        IEnumerable<ISymbol> symbols = await ResolveAsync(project, symbolName, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<string, SymbolLocations> byDisplay = new(StringComparer.Ordinal);
        foreach (ISymbol symbol in symbols)
        {
            IEnumerable<ISymbol> implementations = await SymbolFinder
                .FindImplementationsAsync(symbol, solution, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            foreach (ISymbol implementation in implementations)
            {
                string display = implementation.ToDisplayString();
                byDisplay.TryAdd(display, new SymbolLocations(
                    display,
                    LocationDescriptor.Definitions(implementation, handwrittenPaths)));
            }
        }

        return [.. byDisplay.Values];
    }

    /// <summary>Returns the base types and derived types of every type matching the name.</summary>
    public async Task<IReadOnlyList<TypeHierarchy>> TypeHierarchyAsync(
        Project project,
        string typeName,
        CancellationToken cancellationToken = default)
    {
        HashSet<string> handwrittenPaths = LocationDescriptor.HandwrittenPaths(project);
        Solution solution = project.Solution;
        IEnumerable<ISymbol> symbols = await ResolveAsync(project, typeName, cancellationToken)
            .ConfigureAwait(false);

        List<TypeHierarchy> results = [];
        foreach (INamedTypeSymbol type in symbols.OfType<INamedTypeSymbol>())
        {
            List<TypeReference> baseTypes = [];
            for (INamedTypeSymbol? baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
            {
                baseTypes.Add(ToTypeReference(baseType, handwrittenPaths));
            }

            foreach (INamedTypeSymbol implemented in type.Interfaces)
            {
                baseTypes.Add(ToTypeReference(implemented, handwrittenPaths));
            }

            IEnumerable<INamedTypeSymbol> derivedSymbols;
            if (type.TypeKind == TypeKind.Interface)
            {
                IEnumerable<ISymbol> implementations = await SymbolFinder
                    .FindImplementationsAsync(type, solution, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                IEnumerable<INamedTypeSymbol> subInterfaces = await SymbolFinder
                    .FindDerivedInterfacesAsync(type, solution, transitive: true, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                derivedSymbols = implementations.OfType<INamedTypeSymbol>().Concat(subInterfaces);
            }
            else
            {
                derivedSymbols = await SymbolFinder
                    .FindDerivedClassesAsync(type, solution, transitive: true, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            List<TypeReference> derivedTypes =
                [.. derivedSymbols.Select(derived => ToTypeReference(derived, handwrittenPaths))];

            results.Add(new TypeHierarchy(type.ToDisplayString(), baseTypes, derivedTypes));
        }

        return results;
    }

    /// <summary>
    /// Returns, for each member matching the name, the members that override it and the members it
    /// overrides (the override chain).
    /// </summary>
    public async Task<IReadOnlyList<OverrideHierarchy>> FindOverridesAsync(
        Project project,
        string symbolName,
        CancellationToken cancellationToken = default)
    {
        HashSet<string> handwrittenPaths = LocationDescriptor.HandwrittenPaths(project);
        Solution solution = project.Solution;
        IEnumerable<ISymbol> symbols = await ResolveAsync(project, symbolName, cancellationToken)
            .ConfigureAwait(false);

        List<OverrideHierarchy> results = [];
        foreach (ISymbol symbol in symbols)
        {
            IEnumerable<ISymbol> overriders = await SymbolFinder
                .FindOverridesAsync(symbol, solution, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            List<SymbolLocations> overriddenBy =
            [
                .. overriders.Select(overrider => new SymbolLocations(
                    overrider.ToDisplayString(),
                    LocationDescriptor.Definitions(overrider, handwrittenPaths)))
            ];

            List<SymbolLocations> overrides = [];
            for (ISymbol? current = OverriddenMember(symbol);
                current is not null;
                current = OverriddenMember(current))
            {
                overrides.Add(new SymbolLocations(
                    current.ToDisplayString(),
                    LocationDescriptor.Definitions(current, handwrittenPaths)));
            }

            results.Add(new OverrideHierarchy(symbol.ToDisplayString(), overriddenBy, overrides));
        }

        return results;
    }

    private static ISymbol? OverriddenMember(ISymbol symbol) => symbol switch
    {
        IMethodSymbol method => method.OverriddenMethod,
        IPropertySymbol property => property.OverriddenProperty,
        IEventSymbol @event => @event.OverriddenEvent,
        _ => null,
    };

    private static Task<IEnumerable<ISymbol>> ResolveAsync(
        Project project,
        string symbolName,
        CancellationToken cancellationToken) =>
        SymbolFinder.FindSourceDeclarationsAsync(project, symbolName, ignoreCase: false, cancellationToken);

    private static TypeReference ToTypeReference(INamedTypeSymbol type, HashSet<string> handwrittenPaths)
    {
        List<ReferenceLocationInfo> locations = LocationDescriptor.Definitions(type, handwrittenPaths);
        return new TypeReference(type.ToDisplayString(), locations.Count > 0 ? locations[0] : null);
    }
}
