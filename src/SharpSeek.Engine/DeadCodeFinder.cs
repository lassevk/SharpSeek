using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace SharpSeek.Engine;

/// <summary>How widely to look for unused members.</summary>
public enum DeadCodeScope
{
    /// <summary>Only <c>private</c> members (safe: their usage is fully visible in their type).</summary>
    Private,

    /// <summary>
    /// Any member unused anywhere in the solution, including internal and public ones. Useful for
    /// application solutions, but results need verifying: a public member may be part of a library's
    /// external API, or used via reflection/DI/serialization.
    /// </summary>
    Solution,
}

/// <summary>A member that appears to be unused.</summary>
public sealed record UnusedSymbol(string Display, string Kind, ReferenceLocationInfo Location);

/// <summary>
/// Finds members with no references anywhere in the solution. Because the underlying reference
/// search traverses source-generated code, a member used only from generated code (such as a
/// Blazor <c>@onclick</c> handler) is correctly treated as used — the false positive that a
/// generated-code-blind tool produces.
/// </summary>
public sealed class DeadCodeFinder
{
    /// <summary>
    /// Returns members (methods, properties, fields, events) that have no references.
    /// </summary>
    /// <remarks>
    /// With <see cref="DeadCodeScope.Private"/> (the default) only <c>private</c> members are
    /// considered, which is safe. With <see cref="DeadCodeScope.Solution"/> any accessibility is
    /// considered (members unused across the whole solution); those results are hints to verify,
    /// since a member may be a library's public API or reached via reflection/serialization. In
    /// both modes, virtual/abstract/override members and interface implementations are excluded
    /// from the broader scope because they are reached indirectly.
    /// </remarks>
    public async Task<IReadOnlyList<UnusedSymbol>> FindUnusedSymbolsAsync(
        Solution solution,
        DeadCodeScope scope = DeadCodeScope.Private,
        CancellationToken cancellationToken = default)
    {
        HashSet<string> handwrittenPaths = LocationDescriptor.HandwrittenPaths(solution);

        IEnumerable<ISymbol> members = await SymbolFinder
            .FindSourceDeclarationsAsync(solution, _ => true, SymbolFilter.Member, cancellationToken)
            .ConfigureAwait(false);

        List<UnusedSymbol> results = [];
        foreach (ISymbol symbol in members)
        {
            if (!IsCandidate(symbol, scope))
            {
                continue;
            }

            IEnumerable<ReferencedSymbol> references = await SymbolFinder
                .FindReferencesAsync(symbol, solution, cancellationToken)
                .ConfigureAwait(false);

            if (references.Any(referenced => referenced.Locations.Any()))
            {
                continue;
            }

            List<ReferenceLocationInfo> definitions =
                LocationDescriptor.Definitions(symbol, handwrittenPaths);
            if (definitions.Count == 0)
            {
                continue;
            }

            results.Add(new UnusedSymbol(symbol.ToDisplayString(), symbol.Kind.ToString(), definitions[0]));
        }

        return results;
    }

    private static bool IsCandidate(ISymbol symbol, DeadCodeScope scope)
    {
        if (symbol.IsImplicitlyDeclared)
        {
            return false;
        }

        if (scope == DeadCodeScope.Private && symbol.DeclaredAccessibility != Accessibility.Private)
        {
            return false;
        }

        bool kindOk = symbol switch
        {
            // Only ordinary methods; skip constructors, operators, accessors, etc. Explicit
            // interface implementations are reachable through the interface, so exclude them.
            IMethodSymbol method => method.MethodKind == MethodKind.Ordinary
                && method.ExplicitInterfaceImplementations.IsDefaultOrEmpty,
            IPropertySymbol property => property.ExplicitInterfaceImplementations.IsDefaultOrEmpty,
            IEventSymbol @event => @event.ExplicitInterfaceImplementations.IsDefaultOrEmpty,
            IFieldSymbol => true,
            _ => false,
        };
        if (!kindOk)
        {
            return false;
        }

        if (scope == DeadCodeScope.Solution)
        {
            // These are reached indirectly (polymorphism, interfaces, the program entry point), so
            // a direct-reference search can't prove they are dead.
            if (symbol.IsVirtual || symbol.IsAbstract || symbol.IsOverride
                || symbol is IMethodSymbol { IsStatic: true, Name: "Main" }
                || ImplementsInterfaceMember(symbol))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ImplementsInterfaceMember(ISymbol symbol)
    {
        if (symbol.ContainingType is not { } type)
        {
            return false;
        }

        foreach (INamedTypeSymbol @interface in type.AllInterfaces)
        {
            foreach (ISymbol member in @interface.GetMembers())
            {
                ISymbol? implementation = type.FindImplementationForInterfaceMember(member);
                if (implementation is not null
                    && SymbolEqualityComparer.Default.Equals(implementation, symbol))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
