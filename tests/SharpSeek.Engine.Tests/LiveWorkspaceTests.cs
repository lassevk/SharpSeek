using SharpSeek.Engine;

using Xunit;

namespace SharpSeek.Engine.Tests;

public class LiveWorkspaceTests
{
    [Fact]
    public async Task TryApplyTextChange_ReflectsEditInSubsequentQueries()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        // A dedicated live workspace (the shared fixture's workspace is disposed and read-only).
        using LiveWorkspace workspace =
            await LiveWorkspace.LoadAsync(FixturePaths.SampleBlazorAppProject, cancellationToken);

        string greetingPath = Path.Combine(
            Path.GetDirectoryName(FixturePaths.SampleBlazorAppProject)!, "Domain", "Greeting.cs");
        string original = await File.ReadAllTextAsync(greetingPath, cancellationToken);

        SymbolExplorer explorer = new();

        // The type does not exist yet.
        IReadOnlyList<SymbolMatch> before =
            await explorer.SearchSymbolsAsync(workspace.Project, "FreshlyAddedType", 10, cancellationToken);
        Assert.Empty(before);

        // Apply an in-memory edit (disk is not modified) and confirm the workspace reflects it.
        string edited = original.Replace(
            "public interface IGreeter",
            "public class FreshlyAddedType;\n\npublic interface IGreeter");
        Assert.True(workspace.TryApplyTextChange(greetingPath, edited));

        IReadOnlyList<SymbolMatch> after =
            await explorer.SearchSymbolsAsync(workspace.Project, "FreshlyAddedType", 10, cancellationToken);
        Assert.Contains(after, match => match.Display.Contains("FreshlyAddedType"));
    }
}
