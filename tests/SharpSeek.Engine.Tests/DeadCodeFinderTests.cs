using SharpSeek.Engine;

using Xunit;

namespace SharpSeek.Engine.Tests;

[Collection(SampleBlazorAppCollection.Name)]
public class DeadCodeFinderTests
{
    private readonly SampleBlazorAppFixture _fixture;

    public DeadCodeFinderTests(SampleBlazorAppFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task FindUnusedPrivateSymbols_FlagsTrulyUnused_AndRespectsGeneratedUsage()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        DeadCodeFinder finder = new();

        IReadOnlyList<UnusedSymbol> unused =
            await finder.FindUnusedPrivateSymbolsAsync(_fixture.Project, cancellationToken);

        // The genuinely unused private method is reported.
        Assert.Contains(unused, symbol => symbol.Display.Contains("NeverCalled"));

        // A private method that IS referenced is not reported.
        Assert.DoesNotContain(unused, symbol => symbol.Display.Contains("Helper"));

        // The key case: a private handler used only from .razor markup (its sole reference lives
        // in generated code) must NOT be reported as unused.
        Assert.DoesNotContain(unused, symbol => symbol.Display.Contains("ShowPreviousYearAsync"));
    }
}
