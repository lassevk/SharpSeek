using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace SharpSeek.Engine;

/// <summary>A private member that appears to be unused.</summary>
public sealed record UnusedSymbol(string Display, string Kind, ReferenceLocationInfo Location);

/// <summary>
/// Finds private members with no references anywhere in the project. Because the underlying
/// reference search traverses source-generated code, a private member used only from generated
/// code (such as a Blazor <c>@onclick</c> handler) is correctly treated as used — the false
/// positive that a generic, generated-code-blind tool produces.
/// </summary>
public sealed class DeadCodeFinder
{
    /// <summary>
    /// Returns private members (methods, properties, fields, events) that have no references.
    /// </summary>
    /// <remarks>
    /// Limited to <c>private</c> members on purpose: anything more visible could be used from
    /// outside this project, so its usage cannot be judged here. Members reached only via
    /// reflection or serialization can still be reported (they have no compile-time references);
    /// that is an inherent limit of static analysis.
    /// </remarks>
    public async Task<IReadOnlyList<UnusedSymbol>> FindUnusedPrivateSymbolsAsync(
        Project project,
        CancellationToken cancellationToken = default)
    {
        HashSet<string> handwrittenPaths = LocationDescriptor.HandwrittenPaths(project);
        Solution solution = project.Solution;

        IEnumerable<ISymbol> members = await SymbolFinder
            .FindSourceDeclarationsAsync(project, _ => true, SymbolFilter.Member, cancellationToken)
            .ConfigureAwait(false);

        List<UnusedSymbol> results = [];
        foreach (ISymbol symbol in members)
        {
            if (!IsCandidate(symbol))
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

    private static bool IsCandidate(ISymbol symbol)
    {
        if (symbol.DeclaredAccessibility != Accessibility.Private || symbol.IsImplicitlyDeclared)
        {
            return false;
        }

        switch (symbol)
        {
            case IMethodSymbol method:
                // Only ordinary methods; skip constructors, operators, accessors, etc. Explicit
                // interface implementations are reachable through the interface, so exclude them.
                return method.MethodKind == MethodKind.Ordinary
                    && method.ExplicitInterfaceImplementations.IsDefaultOrEmpty;

            case IPropertySymbol property:
                return property.ExplicitInterfaceImplementations.IsDefaultOrEmpty;

            case IEventSymbol @event:
                return @event.ExplicitInterfaceImplementations.IsDefaultOrEmpty;

            case IFieldSymbol:
                return true;

            default:
                return false;
        }
    }
}
