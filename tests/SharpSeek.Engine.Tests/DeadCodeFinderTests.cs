using SharpSeek.Engine;

using Xunit;

namespace SharpSeek.Engine.Tests;

[Collection(SampleBlazorAppCollection.Name)]
public class DeadCodeFinderTests
{
    private readonly SampleBlazorAppFixture _fixture;

    public DeadCodeFinderTests(SampleBlazorAppFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task FindUnusedSymbols_Private_FlagsTrulyUnused_AndRespectsGeneratedUsage()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        DeadCodeFinder finder = new();

        IReadOnlyList<UnusedSymbol> unused =
            await finder.FindUnusedSymbolsAsync(_fixture.Solution, DeadCodeScope.Private, cancellationToken);

        // The genuinely unused private method is reported.
        Assert.Contains(unused, symbol => symbol.Display.Contains("NeverCalled"));

        // A private method that IS referenced is not reported.
        Assert.DoesNotContain(unused, symbol => symbol.Display.Contains("Helper"));

        // A private handler used only from .razor markup (its sole reference lives in generated
        // code) must NOT be reported as unused.
        Assert.DoesNotContain(unused, symbol => symbol.Display.Contains("ShowPreviousYearAsync"));

        // Private scope must not report public members.
        Assert.DoesNotContain(unused, symbol => symbol.Display.Contains("UnusedThing"));
    }

    [Fact]
    public async Task FindUnusedSymbols_Solution_FlagsPublicMemberUnusedAcrossSolution()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        DeadCodeFinder finder = new();

        IReadOnlyList<UnusedSymbol> unused =
            await finder.FindUnusedSymbolsAsync(_fixture.Solution, DeadCodeScope.Solution, cancellationToken);

        // SampleUnused.UnusedThing.Value is public but used by no project in the solution.
        Assert.Contains(unused, symbol => symbol.Display.Contains("UnusedThing"));

        // SampleLibrary.LibraryGreeting.Text is public and IS used (by SampleBlazorApp), so it is
        // not reported even in the broader scope.
        Assert.DoesNotContain(unused, symbol => symbol.Display.Contains("LibraryGreeting"));
    }
}
