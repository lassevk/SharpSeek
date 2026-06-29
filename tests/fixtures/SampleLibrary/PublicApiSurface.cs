using System;

// Assembly-level [PublicAPI] declares the whole assembly's public surface as API, so its public
// members must not be reported as unused even when nothing in the solution references them. The
// attribute is a source copy in this project's namespace to exercise by-name matching. See #6.
[assembly: SampleLibrary.PublicAPI]

namespace SampleLibrary;

[AttributeUsage(AttributeTargets.All, Inherited = false)]
public sealed class PublicAPIAttribute : Attribute
{
}

// Public and referenced by no project in the solution; only the assembly-level [PublicAPI] keeps it
// from being reported as unused in solution scope.
public static class PublicApiSurface
{
    public static int UnusedButPublicApi => 0;
}
