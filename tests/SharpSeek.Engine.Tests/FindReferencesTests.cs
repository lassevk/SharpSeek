using SharpSeek.Engine;

using Xunit;

namespace SharpSeek.Engine.Tests;

public class FindReferencesTests
{
    [Fact]
    public async Task FindReferences_OnRazorOnlyHandler_MapsBackToRazorLine()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        WorkspaceLoader loader = new();
        ProjectLoadResult load = await loader.LoadProjectAsync(
            FixturePaths.SampleBlazorAppProject, cancellationToken);

        ReferenceFinder finder = new();
        IReadOnlyList<SymbolReferences> results =
            await finder.FindReferencesAsync(load.Project, "ShowPreviousYearAsync", cancellationToken);

        SymbolReferences symbol = Assert.Single(results);

        // The definition is in the hand-written code-behind.
        ReferenceLocationInfo definition = Assert.Single(symbol.Definitions);
        Assert.Equal(ReferenceOrigin.Handwritten, definition.Origin);
        Assert.EndsWith("Calendar.razor.cs", definition.FilePath);

        // The only usage is the @onclick, which lives in generated BuildRenderTree code and must
        // be mapped back to the .razor markup line (Calendar.razor line 3). This is the case a
        // generic LSP misses.
        ReferenceLocationInfo reference = Assert.Single(symbol.References);
        Assert.Equal(ReferenceOrigin.Generated, reference.Origin);
        Assert.EndsWith("Calendar.razor", reference.FilePath);
        Assert.Equal(3, reference.Line);
    }
}
