using Microsoft.CodeAnalysis;

using SharpSeek.Engine;

using Xunit;

namespace SharpSeek.Engine.Tests;

/// <summary>
/// Loads the sample Blazor fixture once and shares it across the whole test collection. Loading a
/// project through MSBuildWorkspace is expensive, so paying it per test would make the suite slow.
/// </summary>
public sealed class SampleBlazorAppFixture : IAsyncLifetime
{
    public Project Project { get; private set; } = null!;

    public Solution Solution => Project.Solution;

    public async ValueTask InitializeAsync()
    {
        WorkspaceLoader loader = new();
        ProjectLoadResult load = await loader.LoadProjectAsync(FixturePaths.SampleBlazorAppProject);
        Project = load.Project;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

[CollectionDefinition(Name)]
public sealed class SampleBlazorAppCollection : ICollectionFixture<SampleBlazorAppFixture>
{
    public const string Name = "SampleBlazorApp";
}
