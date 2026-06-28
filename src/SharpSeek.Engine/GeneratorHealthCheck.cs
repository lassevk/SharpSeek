using Microsoft.CodeAnalysis;

namespace SharpSeek.Engine;

/// <summary>
/// Detects the silent failure mode where the SDK's Razor source generator cannot be instantiated
/// because the Roslyn loaded by this process is older than the one the SDK's generator was built
/// against. When that happens the generator quietly produces nothing, so references inside
/// <c>.razor</c> markup go missing with no build error — exactly the situation SharpSeek exists to
/// surface, not hide.
/// </summary>
public static class GeneratorHealthCheck
{
    private const string RazorGeneratorAssembly = "Microsoft.CodeAnalysis.Razor.Compiler";

    /// <summary>
    /// Returns a human-readable problem description when a project's Razor generator is referenced
    /// but exposes no generators (the Roslyn-vs-SDK version skew). Returns <c>null</c> when all
    /// projects are healthy, or when no project has a Razor generator (for example a non-Blazor
    /// solution).
    /// </summary>
    public static string? DetectRazorGeneratorSkew(Solution solution)
    {
        foreach (Project project in solution.Projects)
        {
            string? problem = DetectRazorGeneratorSkew(project);
            if (problem is not null)
            {
                return $"[{project.Name}] {problem}";
            }
        }

        return null;
    }

    private static string? DetectRazorGeneratorSkew(Project project)
    {
        foreach (var reference in project.AnalyzerReferences)
        {
            string name = Path.GetFileNameWithoutExtension(
                reference.FullPath ?? reference.Display ?? string.Empty);

            if (!string.Equals(name, RazorGeneratorAssembly, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // GetGenerators swallows assembly/type load failures and returns an empty result, so
            // an empty result here is the tell-tale sign of the version skew.
            if (reference.GetGenerators(LanguageNames.CSharp).Length == 0)
            {
                return
                    $"The Razor source generator ('{name}') is referenced by the project but " +
                    "exposes no generators. This almost always means the Roslyn version loaded by " +
                    "SharpSeek is older than the one the installed .NET SDK's Razor generator was " +
                    "built against, so the generator silently produced nothing and references " +
                    "inside .razor markup will be missing. Fix: bump the pinned " +
                    "Microsoft.CodeAnalysis.* package versions (Engine project + nuget.config) to " +
                    "match the SDK's Roslyn build.";
            }

            return null;
        }

        return null;
    }
}
