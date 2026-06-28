using SharpSeek.Engine;

using Xunit;

namespace SharpSeek.Engine.Tests;

[Collection(SampleBlazorAppCollection.Name)]
public class CallHierarchyTests
{
    private readonly SampleBlazorAppFixture _fixture;

    public CallHierarchyTests(SampleBlazorAppFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task CallHierarchy_ReportsIncomingCallers()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        CallHierarchyAnalyzer analyzer = new();

        // WithDeadCode.Used() calls Helper(), so Helper's incoming callers include Used.
        IReadOnlyList<CallHierarchy> results =
            await analyzer.AnalyzeAsync(_fixture.Solution, "Helper", cancellationToken);

        CallHierarchy hierarchy = Assert.Single(results);
        Assert.Contains(hierarchy.Incoming, call => call.Caller.Contains("Used"));
    }

    [Fact]
    public async Task CallHierarchy_ReportsOutgoingCalls()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        CallHierarchyAnalyzer analyzer = new();

        IReadOnlyList<CallHierarchy> results =
            await analyzer.AnalyzeAsync(_fixture.Solution, "Used", cancellationToken);

        CallHierarchy hierarchy = Assert.Single(results);
        Assert.Contains(hierarchy.Outgoing, call => call.Callee.Contains("Helper"));
    }
}
