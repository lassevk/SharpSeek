using SharpSeek.Engine;

using Xunit;

namespace SharpSeek.Engine.Tests;

[Collection(SampleBlazorAppCollection.Name)]
public class DiagnosticReaderTests
{
    private readonly SampleBlazorAppFixture _fixture;

    public DiagnosticReaderTests(SampleBlazorAppFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetDiagnostics_ReportsCompilerWarning()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        DiagnosticReader reader = new();

        IReadOnlyList<DiagnosticInfo> diagnostics =
            await reader.GetDiagnosticsAsync(_fixture.Solution, cancellationToken: cancellationToken);

        // The fixture intentionally contains a CS0219 (assigned-but-never-used) warning.
        DiagnosticInfo warning = Assert.Single(diagnostics, diagnostic => diagnostic.Id == "CS0219");
        Assert.Equal("Warning", warning.Severity);
        Assert.NotNull(warning.Location);
        Assert.EndsWith("DiagnosticsSample.cs", warning.Location!.FilePath);
    }

    [Fact]
    public async Task GetDiagnostics_FilteredByFile_OnlyReturnsThatFile()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        DiagnosticReader reader = new();

        IReadOnlyList<DiagnosticInfo> diagnostics = await reader.GetDiagnosticsAsync(
            _fixture.Solution, "Domain/DiagnosticsSample.cs", "warning", cancellationToken);

        Assert.NotEmpty(diagnostics);
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.NotNull(diagnostic.Location);
            Assert.EndsWith("DiagnosticsSample.cs", diagnostic.Location!.FilePath);
        });
    }
}
