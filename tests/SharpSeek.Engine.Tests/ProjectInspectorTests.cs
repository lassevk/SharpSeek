using SharpSeek.Engine;

using Xunit;

namespace SharpSeek.Engine.Tests;

[Collection(SampleBlazorAppCollection.Name)]
public class ProjectInspectorTests
{
    private readonly SampleBlazorAppFixture _fixture;

    public ProjectInspectorTests(SampleBlazorAppFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetOverview_ReportsProjectStructure()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        ProjectInspector inspector = new();

        ProjectOverview overview = await inspector.GetOverviewAsync(_fixture.Project, cancellationToken);

        Assert.Equal("SampleBlazorApp", overview.Name);
        Assert.Equal("C#", overview.Language);
        Assert.True(overview.DocumentCount > 0);
        // The Razor generator produces at least the Calendar component.
        Assert.True(overview.GeneratedDocumentCount > 0);
    }
}
