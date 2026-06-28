using SharpSeek.Engine;

using Xunit;

namespace SharpSeek.Engine.Tests;

public class GeneratorHealthCheckTests
{
    [Fact]
    public async Task DetectRazorGeneratorSkew_OnHealthyRazorProject_ReturnsNull()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        WorkspaceLoader loader = new();
        ProjectLoadResult load = await loader.LoadProjectAsync(
            FixturePaths.SampleBlazorAppProject, cancellationToken);

        // The fixture is a real Razor project whose generator runs, so the skew guard must not
        // fire — proving it does not false-positive on a working setup.
        string? problem = GeneratorHealthCheck.DetectRazorGeneratorSkew(load.Project);

        Assert.Null(problem);
    }
}
