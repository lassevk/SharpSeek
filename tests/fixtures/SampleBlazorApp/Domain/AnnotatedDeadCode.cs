namespace SampleBlazorApp.Domain;

// Members that have zero references but are declared implicitly used through annotations, so
// find_unused_symbols must NOT report them. See issue #6.
public class AnnotatedSamples
{
    // Directly annotated: never dead, even with no references (private scope).
    [UsedImplicitly]
    private void ImplicitlyUsedDirectly()
    {
    }

    // Decorated with a custom attribute that is itself marked [MeansImplicitlyUsed].
    [Injected]
    private void UsedThroughCustomMarker()
    {
    }
}

// [UsedImplicitly(WithMembers)] on the type implies its members are used too.
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public class ImplicitlyUsedContainer
{
    public void MemberCoveredByWithMembers()
    {
    }
}

// [UsedImplicitly(WithInheritors)] on the interface implies implementors (and their members) are used.
[UsedImplicitly(ImplicitUseTargetFlags.WithInheritors)]
public interface IImplicitlyUsedContract
{
}

public class ImplicitlyUsedImplementor : IImplicitlyUsedContract
{
    public void MemberCoveredByWithInheritors()
    {
    }
}
