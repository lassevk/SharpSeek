using SharpSeek.Engine;

using Xunit;

namespace SharpSeek.Engine.Tests;

[Collection(SampleBlazorAppCollection.Name)]
public class NavigationTests
{
    private readonly SampleBlazorAppFixture _fixture;

    public NavigationTests(SampleBlazorAppFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GoToDefinition_ResolvesTypeToItsDeclaration()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        SymbolNavigator navigator = new();

        IReadOnlyList<SymbolLocations> results =
            await navigator.GoToDefinitionAsync(_fixture.Solution, "EnglishGreeter", cancellationToken);

        SymbolLocations symbol = Assert.Single(results);
        ReferenceLocationInfo location = Assert.Single(symbol.Locations);
        Assert.Equal(ReferenceOrigin.Handwritten, location.Origin);
        Assert.EndsWith("Greeting.cs", location.FilePath);
    }

    [Fact]
    public async Task FindImplementations_OnInterface_ReturnsImplementingType()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        SymbolNavigator navigator = new();

        IReadOnlyList<SymbolLocations> results =
            await navigator.FindImplementationsAsync(_fixture.Solution, "IGreeter", cancellationToken);

        Assert.Contains(results, result => result.SymbolDisplay.Contains("EnglishGreeter"));
    }

    [Fact]
    public async Task TypeHierarchy_OnClass_ReportsBaseAndDerivedTypes()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        SymbolNavigator navigator = new();

        IReadOnlyList<TypeHierarchy> results =
            await navigator.TypeHierarchyAsync(_fixture.Solution, "Dog", cancellationToken);

        TypeHierarchy hierarchy = Assert.Single(results);
        Assert.Contains(hierarchy.BaseTypes, baseType => baseType.Display.Contains("Animal"));
        Assert.Contains(hierarchy.DerivedTypes, derived => derived.Display.Contains("Puppy"));
    }
}
