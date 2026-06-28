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

        SolutionOverview overview = await inspector.GetOverviewAsync(_fixture.Solution, cancellationToken);

        // The fixture is a multi-project solution (SampleBlazorApp + the libraries it references).
        ProjectOverview project = Assert.Single(overview.Projects, p => p.Name == "SampleBlazorApp");
        Assert.Equal("C#", project.Language);
        Assert.True(project.DocumentCount > 0);
        // The Razor generator produces at least the Calendar component.
        Assert.True(project.GeneratedDocumentCount > 0);
        Assert.Contains("SampleLibrary", project.ProjectReferences);
    }
}
