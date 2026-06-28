namespace SampleUnused;

// SampleBlazorApp references SampleUnused in its .csproj but never uses any of its types, so it
// is a *declared-but-unused* project reference (what project_dependencies should flag).
public static class UnusedThing
{
    public static int Value => 42;
}
