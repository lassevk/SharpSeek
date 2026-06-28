using SharpSeek.Engine;

using Xunit;

namespace SharpSeek.Engine.Tests;

[Collection(SampleBlazorAppCollection.Name)]
public class ExplorationTests
{
    private readonly SampleBlazorAppFixture _fixture;

    public ExplorationTests(SampleBlazorAppFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task SearchSymbols_MatchesByPattern()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        SymbolExplorer explorer = new();

        IReadOnlyList<SymbolMatch> results =
            await explorer.SearchSymbolsAsync(_fixture.Project, "Greeter", 50, cancellationToken);

        Assert.Contains(results, match => match.Display.Contains("EnglishGreeter"));
    }

    [Fact]
    public async Task GetSymbolInfo_ReportsKindAccessibilityAndLocation()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        SymbolExplorer explorer = new();

        IReadOnlyList<SymbolDetails> results =
            await explorer.GetSymbolInfoAsync(_fixture.Project, "EnglishGreeter", cancellationToken);

        SymbolDetails info = Assert.Single(results);
        Assert.Equal("NamedType", info.Kind);
        Assert.Equal("Public", info.Accessibility);
        ReferenceLocationInfo location = Assert.Single(info.Locations);
        Assert.EndsWith("Greeting.cs", location.FilePath);
    }

    [Fact]
    public async Task DocumentOutline_ListsDeclaredTypesAndMembers()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        SymbolExplorer explorer = new();

        IReadOnlyList<OutlineItem> items =
            await explorer.DocumentOutlineAsync(_fixture.Project, "Domain/Greeting.cs", cancellationToken);

        Assert.Contains(items, item => item.Display.Contains("IGreeter"));
        Assert.Contains(items, item => item.Display.Contains("EnglishGreeter"));
        Assert.Contains(items, item => item.Display.Contains("Greet"));
    }
}
