namespace SampleBlazorApp.Domain;

// Intentionally triggers a compiler warning (CS0219: variable assigned but never used) so the
// get_diagnostics tests have a deterministic diagnostic to find. The unused local is not a
// member, so it does not affect the find_unused_symbols tests.
public static class DiagnosticsSample
{
    public static int Compute()
    {
        int unused = 1;
        return 2;
    }
}
