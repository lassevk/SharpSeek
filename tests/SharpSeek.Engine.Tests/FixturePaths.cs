using System.Runtime.CompilerServices;

namespace SharpSeek.Engine.Tests;

/// <summary>
/// Resolves on-disk paths to the test fixture projects, independent of the current working
/// directory, by anchoring to this source file's compile-time location.
/// </summary>
internal static class FixturePaths
{
    /// <summary>The sample Blazor (Razor Class Library) fixture's project file.</summary>
    public static string SampleBlazorAppProject { get; } = Path.Combine(
        RepoRoot(), "tests", "fixtures", "SampleBlazorApp", "SampleBlazorApp.csproj");

    private static string RepoRoot([CallerFilePath] string thisFile = "")
    {
        // thisFile = <repo>/tests/SharpSeek.Engine.Tests/FixturePaths.cs
        string testProjectDir = Path.GetDirectoryName(thisFile)!;
        return Path.GetFullPath(Path.Combine(testProjectDir, "..", ".."));
    }
}
