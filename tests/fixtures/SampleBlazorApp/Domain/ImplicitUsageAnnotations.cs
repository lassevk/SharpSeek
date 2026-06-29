using System;

namespace SampleBlazorApp.Domain;

// Source copies of the JetBrains.Annotations attributes, deliberately in the project's own
// namespace (not JetBrains.Annotations) to exercise the namespace-agnostic, by-name matching that
// find_unused_symbols uses. See issue #6.

[AttributeUsage(AttributeTargets.All)]
public sealed class UsedImplicitlyAttribute : Attribute
{
    public UsedImplicitlyAttribute()
    {
    }

    public UsedImplicitlyAttribute(ImplicitUseTargetFlags targetFlags) => TargetFlags = targetFlags;

    public ImplicitUseTargetFlags TargetFlags { get; }
}

[Flags]
public enum ImplicitUseTargetFlags
{
    Default = Itself,
    Itself = 1,
    Members = 2,
    WithInheritors = 4,
    WithMembers = Itself | Members,
}

// A meta-attribute: any attribute type marked with it makes the things it decorates implicitly used.
[AttributeUsage(AttributeTargets.Class)]
public sealed class MeansImplicitlyUsedAttribute : Attribute
{
}

// A project-defined marker that opts in through [MeansImplicitlyUsed] (e.g. a DI/serialization tag).
[MeansImplicitlyUsed]
[AttributeUsage(AttributeTargets.All)]
public sealed class InjectedAttribute : Attribute
{
}
