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
    /// from the broader scope because they are reached indirectly. In both scopes, members the
    /// author has marked as implicitly used via JetBrains.Annotations
    /// (<c>[PublicAPI]</c>, <c>[UsedImplicitly]</c>, or an attribute marked <c>[MeansImplicitlyUsed]</c>,
    /// matched by simple attribute name) are not reported.
    /// </remarks>
    public async Task<IReadOnlyList<UnusedSymbol>> FindUnusedSymbolsAsync(
        Solution solution,
        DeadCodeScope scope = DeadCodeScope.Private,
        CancellationToken cancellationToken = default)
    {
        HashSet<string> handwrittenPaths = LocationDescriptor.HandwrittenPaths(solution);
        ImplicitUsage implicitUsage = await BuildImplicitUsageAsync(solution, cancellationToken)
            .ConfigureAwait(false);

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

            // The author may have declared, via JetBrains.Annotations, that a member is used in a
            // way no reference search can see (reflection, DI, serialization, public API surface).
            if (IsImplicitlyUsed(symbol, implicitUsage))
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

    // JetBrains.Annotations support. Attributes are matched by simple type name only (namespace- and
    // assembly-agnostic), because projects use both the NuGet package and internal/source copies in
    // their own namespace — Rider matches by name, and so do we.
    private const string PublicApiAttribute = "PublicAPIAttribute";
    private const string UsedImplicitlyAttribute = "UsedImplicitlyAttribute";
    private const string MeansImplicitlyUsedAttribute = "MeansImplicitlyUsedAttribute";
    private const string TargetFlagsEnum = "ImplicitUseTargetFlags";

    // ImplicitUseTargetFlags: Members = 2, WithInheritors = 4 (WithMembers = Itself | Members = 3).
    private const int MembersFlag = 2;
    private const int WithInheritorsFlag = 4;

    /// <summary>
    /// The annotation facts gathered once per analysis: the attribute names that mark a symbol as
    /// implicitly used (the built-in <c>UsedImplicitly</c> plus any attribute type marked
    /// <c>[MeansImplicitlyUsed]</c>), and the assemblies carrying an assembly-level <c>[PublicAPI]</c>.
    /// </summary>
    private sealed record ImplicitUsage(
        HashSet<string> UsedImplicitlyMarkers,
        HashSet<ISymbol> PublicApiAssemblies);

    private static async Task<ImplicitUsage> BuildImplicitUsageAsync(
        Solution solution,
        CancellationToken cancellationToken)
    {
        HashSet<string> markers = new(StringComparer.Ordinal) { UsedImplicitlyAttribute };

        // Pre-pass: any attribute type marked [MeansImplicitlyUsed] turns into a marker itself, so a
        // codebase's own frameworks (DI, serializers) opt in without SharpSeek hard-coding them.
        IEnumerable<ISymbol> types = await SymbolFinder
            .FindSourceDeclarationsAsync(solution, _ => true, SymbolFilter.Type, cancellationToken)
            .ConfigureAwait(false);
        foreach (INamedTypeSymbol type in types.OfType<INamedTypeSymbol>())
        {
            if (HasAttribute(type, MeansImplicitlyUsedAttribute))
            {
                markers.Add(type.Name);
            }
        }

        HashSet<ISymbol> publicApiAssemblies = new(SymbolEqualityComparer.Default);
        foreach (Project project in solution.Projects)
        {
            Compilation? compilation = await project.GetCompilationAsync(cancellationToken)
                .ConfigureAwait(false);
            if (compilation is not null && HasAttribute(compilation.Assembly, PublicApiAttribute))
            {
                publicApiAssemblies.Add(compilation.Assembly);
            }
        }

        return new ImplicitUsage(markers, publicApiAssemblies);
    }

    private static bool IsImplicitlyUsed(ISymbol symbol, ImplicitUsage usage)
    {
        // A [PublicAPI] or [UsedImplicitly] (or a [MeansImplicitlyUsed]-derived) attribute directly
        // on the member declares it as never dead.
        if (HasAttribute(symbol, PublicApiAttribute) || HasAnyAttribute(symbol, usage.UsedImplicitlyMarkers))
        {
            return true;
        }

        // Assembly-level [PublicAPI] covers the public surface of that assembly (but not internal
        // members - internal is not public API).
        if (IsEffectivelyPublic(symbol)
            && symbol.ContainingAssembly is { } assembly
            && usage.PublicApiAssemblies.Contains(assembly))
        {
            return true;
        }

        if (symbol.ContainingType is not { } containingType)
        {
            return false;
        }

        // The containing type carries [UsedImplicitly(WithMembers)] -> its members are used too.
        if (HasUsedImplicitlyTarget(containingType, usage, MembersFlag))
        {
            return true;
        }

        // A base type or implemented interface carries [UsedImplicitly(WithInheritors)] -> the
        // inheritor/implementor (and so its members) are treated as used.
        foreach (INamedTypeSymbol ancestor in BaseTypesAndInterfaces(containingType))
        {
            if (HasUsedImplicitlyTarget(ancestor, usage, WithInheritorsFlag))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True if the type carries a <c>UsedImplicitly</c>-style marker whose target flags include <paramref name="flag"/>.</summary>
    private static bool HasUsedImplicitlyTarget(INamedTypeSymbol type, ImplicitUsage usage, int flag)
    {
        foreach (AttributeData attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass?.Name is { } name
                && usage.UsedImplicitlyMarkers.Contains(name)
                && (TargetFlags(attribute) & flag) != 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Reads the <c>ImplicitUseTargetFlags</c> value of a marker attribute; defaults to <c>Itself</c>.</summary>
    private static int TargetFlags(AttributeData attribute)
    {
        foreach (TypedConstant argument in attribute.ConstructorArguments)
        {
            if (IsTargetFlags(argument))
            {
                return ToInt32(argument);
            }
        }

        foreach (KeyValuePair<string, TypedConstant> named in attribute.NamedArguments)
        {
            if (IsTargetFlags(named.Value))
            {
                return ToInt32(named.Value);
            }
        }

        return 1; // Itself
    }

    private static bool IsTargetFlags(TypedConstant constant) =>
        constant.Kind == TypedConstantKind.Enum
        && string.Equals(constant.Type?.Name, TargetFlagsEnum, StringComparison.Ordinal);

    private static int ToInt32(TypedConstant constant) =>
        constant.Value is null ? 0 : Convert.ToInt32(constant.Value);

    private static bool HasAttribute(ISymbol symbol, string attributeName) =>
        symbol.GetAttributes().Any(attribute =>
            string.Equals(attribute.AttributeClass?.Name, attributeName, StringComparison.Ordinal));

    private static bool HasAnyAttribute(ISymbol symbol, HashSet<string> attributeNames) =>
        symbol.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.Name is { } name && attributeNames.Contains(name));

    private static bool IsEffectivelyPublic(ISymbol symbol)
    {
        for (ISymbol? current = symbol; current is not null and not INamespaceSymbol; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility != Accessibility.Public)
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<INamedTypeSymbol> BaseTypesAndInterfaces(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            yield return baseType;
        }

        foreach (INamedTypeSymbol @interface in type.AllInterfaces)
        {
            yield return @interface;
        }
    }
}
