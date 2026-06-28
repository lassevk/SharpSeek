using SharpSeek.Engine;

using Xunit;

namespace SharpSeek.Engine.Tests;

[Collection(SampleBlazorAppCollection.Name)]
public class GeneratorIntrospectionTests
{
    private readonly SampleBlazorAppFixture _fixture;

    public GeneratorIntrospectionTests(SampleBlazorAppFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetGeneratedDocuments_ReturnsRazorOutputForSourceFile()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        ProjectInspector inspector = new();

        IReadOnlyList<GeneratedDocumentInfo> documents =
            await inspector.GetGeneratedDocumentsAsync(_fixture.Project, "Calendar.razor", cancellationToken);

        GeneratedDocumentInfo document = Assert.Single(documents);
        Assert.Contains("BuildRenderTree", document.Text);
        Assert.Contains("ShowPreviousYearAsync", document.Text);
    }

    [Fact]
    public async Task ListGenerators_IncludesRazorGenerator()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        ProjectInspector inspector = new();

        IReadOnlyList<GeneratorInfo> generators =
            await inspector.ListGeneratorsAsync(_fixture.Project, cancellationToken);

        Assert.Contains(generators, generator =>
            generator.Assembly.Contains("Razor") && generator.GeneratorCount > 0);
    }
}
