using SharpSeek.Engine;

using Xunit;

namespace SharpSeek.Engine.Tests;

[Collection(SampleBlazorAppCollection.Name)]
public class FindLiteralUsagesTests
{
    private readonly SampleBlazorAppFixture _fixture;

    public FindLiteralUsagesTests(SampleBlazorAppFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task FindLiteralUsages_FindsHandwrittenStringLiteral()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        SymbolExplorer explorer = new();

        IReadOnlyList<ReferenceLocationInfo> results =
            await explorer.FindLiteralUsagesAsync(_fixture.Solution, "Hello", cancellationToken);

        Assert.Contains(results, location =>
            location.Origin == ReferenceOrigin.Handwritten && location.FilePath.EndsWith("Greeting.cs"));
    }

    [Fact]
    public async Task FindLiteralUsages_FindsLiteralEmittedIntoGeneratedCode()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        SymbolExplorer explorer = new();

        // The button text in Calendar.razor is emitted as a literal in the generated component.
        // (Razor emits static markup under #line hidden, so it stays in the generated file rather
        // than mapping back to the .razor — but it is still found, which a source-only search
        // would miss.)
        IReadOnlyList<ReferenceLocationInfo> results =
            await explorer.FindLiteralUsagesAsync(_fixture.Solution, "Forrige år", cancellationToken);

        Assert.Contains(results, location =>
            location.Origin == ReferenceOrigin.Generated && location.FilePath.Contains("Calendar_razor"));
    }
}
