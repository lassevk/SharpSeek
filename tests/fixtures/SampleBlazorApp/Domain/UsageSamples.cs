namespace SampleBlazorApp.Domain;

// Fixtures for the read/write usage metadata on find_references (#10). Each member is used in a
// known mix of read, write and read-write positions so the usage classifier can be asserted
// directly. The member names are deliberately unique so a name-based reference search resolves to
// exactly one symbol.
public sealed class UsageSamples
{
    private int _usageField;

    public int UsageValue { get; set; }

    public void Exercise()
    {
        UsageValue = 1;             // write
        UsageValue += 2;            // readwrite
        _usageField = UsageValue;   // _usageField: write, UsageValue: read

        _usageField++;              // readwrite
        Consume(_usageField);       // read
        Assign(out _usageField);    // write
        Bump(ref _usageField);      // readwrite
    }

    private static void Consume(int value)
    {
    }

    private static void Assign(out int value) => value = 0;

    private static void Bump(ref int value) => value++;
}
