using SharpSeek.Engine;

using Xunit;

namespace SharpSeek.Engine.Tests;

[Collection(SampleBlazorAppCollection.Name)]
public class DependencyAnalyzerTests
{
    private readonly SampleBlazorAppFixture _fixture;

    public DependencyAnalyzerTests(SampleBlazorAppFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Analyze_DistinguishesUsedFromDeclaredButUnusedReferences()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        DependencyAnalyzer analyzer = new();

        IReadOnlyList<ProjectDependencies> all =
            await analyzer.AnalyzeAsync(_fixture.Solution, cancellationToken);

        ProjectDependencies app = Assert.Single(all, p => p.Project == "SampleBlazorApp");

        // Both libraries are declared references.
        Assert.Contains("SampleLibrary", app.DeclaredReferences);
        Assert.Contains("SampleUnused", app.DeclaredReferences);

        // SampleLibrary is actually used; SampleUnused is referenced but never used.
        Assert.Contains("SampleLibrary", app.UsedReferences);
        Assert.DoesNotContain("SampleUnused", app.UsedReferences);
        Assert.Contains("SampleUnused", app.UnusedReferences);
        Assert.DoesNotContain("SampleLibrary", app.UnusedReferences);

        // SampleLibrary's dependents include SampleBlazorApp (which uses it).
        ProjectDependencies library = Assert.Single(all, p => p.Project == "SampleLibrary");
        Assert.Contains("SampleBlazorApp", library.Dependents);
    }
}
