namespace SampleBlazorApp.Domain;

// Used by the find_unused_symbols tests: one private method that is referenced and one that is
// not. (The razor-only handler ShowPreviousYearAsync in Calendar.razor.cs is the other key case
// — it must NOT be reported as unused, because its only use is in generated code.)
public class WithDeadCode
{
    public int Used() => Helper();

    private int Helper() => 42;

    private int NeverCalled() => 0;
}
