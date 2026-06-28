using SharpSeek.Engine;

using Xunit;

namespace SharpSeek.Engine.Tests;

[Collection(SampleBlazorAppCollection.Name)]
public class FindOverridesTests
{
    private readonly SampleBlazorAppFixture _fixture;

    public FindOverridesTests(SampleBlazorAppFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task FindOverrides_ReportsOverridersAndOverriddenChain()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        SymbolNavigator navigator = new();

        IReadOnlyList<OverrideHierarchy> results =
            await navigator.FindOverridesAsync(_fixture.Solution, "Speak", cancellationToken);

        // The abstract Animal.Speak is overridden by Dog and Puppy.
        OverrideHierarchy animal = Assert.Single(results, result => result.SymbolDisplay.Contains("Animal"));
        Assert.Contains(animal.OverriddenBy, member => member.SymbolDisplay.Contains("Dog"));
        Assert.Contains(animal.OverriddenBy, member => member.SymbolDisplay.Contains("Puppy"));

        // Dog.Speak overrides Animal.Speak.
        OverrideHierarchy dog = Assert.Single(results, result => result.SymbolDisplay.Contains("Dog"));
        Assert.Contains(dog.Overrides, member => member.SymbolDisplay.Contains("Animal"));
    }
}
