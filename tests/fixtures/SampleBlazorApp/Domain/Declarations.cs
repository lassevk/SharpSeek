using System;

namespace SampleBlazorApp.Domain;

// Fixtures for the get_symbol_range / get_symbol_source tools (#7, #8): a documented and
// attributed multi-line method, an overload set, and a partial type split into two declarations.
public partial class DeclarationSamples
{
    /// <summary>Adds two numbers together.</summary>
    /// <remarks>Carries an XML-doc comment and an attribute so the range/source includes both.</remarks>
    [Obsolete("Documented and attributed for the declaration range/source fixtures.")]
    public int Add(int first, int second)
    {
        int sum = first + second;
        return sum;
    }

    public string Describe(int value) => value.ToString();

    public string Describe(string value) => value;
}

public partial class DeclarationSamples
{
    public int SecondPartValue => 42;
}
