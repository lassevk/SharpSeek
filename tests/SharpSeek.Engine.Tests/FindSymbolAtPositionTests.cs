using SharpSeek.Engine;

using Xunit;

namespace SharpSeek.Engine.Tests;

[Collection(SampleBlazorAppCollection.Name)]
public class FindSymbolAtPositionTests
{
    private readonly SampleBlazorAppFixture _fixture;

    public FindSymbolAtPositionTests(SampleBlazorAppFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ResolveSymbolAt_ReturnsTheSymbolAtTheGivenLocation()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        SymbolExplorer explorer = new();

        // Greeting.cs line 9: "public class EnglishGreeter : IGreeter" — column 16 is inside the
        // type name.
        IReadOnlyList<SymbolDetails> results = await explorer.ResolveSymbolAtAsync(
            _fixture.Solution, "Domain/Greeting.cs", line: 9, column: 16, cancellationToken);

        SymbolDetails info = Assert.Single(results);
        Assert.Contains("EnglishGreeter", info.Display);
        Assert.Equal("NamedType", info.Kind);
    }
}
