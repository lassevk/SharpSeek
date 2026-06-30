using SharpSeek.Engine;

using Xunit;

namespace SharpSeek.Engine.Tests;

[Collection(SampleBlazorAppCollection.Name)]
public class FindReferencesUsageTests
{
    private readonly SampleBlazorAppFixture _fixture;

    public FindReferencesUsageTests(SampleBlazorAppFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task FindReferences_ClassifiesPropertyReadsAndWrites()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        ReferenceFinder finder = new();

        IReadOnlyList<SymbolReferences> results =
            await finder.FindReferencesAsync(_fixture.Solution, "UsageValue", cancellationToken);

        SymbolReferences symbol = Assert.Single(results);
        Assert.Equal("Property", symbol.SymbolKind);

        // UsageValue = 1 (write); UsageValue += 2 (readwrite); _usageField = UsageValue (read).
        Assert.Equal(1, symbol.References.Count(r => r.Usage == SymbolUsage.Write));
        Assert.Equal(1, symbol.References.Count(r => r.Usage == SymbolUsage.ReadWrite));
        Assert.Equal(1, symbol.References.Count(r => r.Usage == SymbolUsage.Read));
    }

    [Fact]
    public async Task FindReferences_ClassifiesFieldAssignmentsAndRefOutArguments()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        ReferenceFinder finder = new();

        IReadOnlyList<SymbolReferences> results =
            await finder.FindReferencesAsync(_fixture.Solution, "_usageField", cancellationToken);

        SymbolReferences symbol = Assert.Single(results);
        Assert.Equal("Field", symbol.SymbolKind);

        // Writes: `_usageField = ...` and `Assign(out _usageField)`.
        Assert.Equal(2, symbol.References.Count(r => r.Usage == SymbolUsage.Write));
        // Read-writes: `_usageField++` and `Bump(ref _usageField)`.
        Assert.Equal(2, symbol.References.Count(r => r.Usage == SymbolUsage.ReadWrite));
        // Read: `Consume(_usageField)`.
        Assert.Equal(1, symbol.References.Count(r => r.Usage == SymbolUsage.Read));
    }
}
