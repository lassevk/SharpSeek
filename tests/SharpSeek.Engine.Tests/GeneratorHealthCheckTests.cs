using SharpSeek.Engine;

using Xunit;

namespace SharpSeek.Engine.Tests;

[Collection(SampleBlazorAppCollection.Name)]
public class GeneratorHealthCheckTests
{
    private readonly SampleBlazorAppFixture _fixture;

    public GeneratorHealthCheckTests(SampleBlazorAppFixture fixture) => _fixture = fixture;

    [Fact]
    public void DetectRazorGeneratorSkew_OnHealthyRazorProject_ReturnsNull()
    {
        // The fixture is a real Razor project whose generator runs, so the skew guard must not
        // fire — proving it does not false-positive on a working setup.
        string? problem = GeneratorHealthCheck.DetectRazorGeneratorSkew(_fixture.Solution);

        Assert.Null(problem);
    }
}
