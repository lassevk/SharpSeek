using System.Runtime.CompilerServices;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace SharpSeek.Engine;

/// <summary>
/// Loads a C# project into an in-memory Roslyn workspace using <see cref="MSBuildWorkspace"/>,
/// so that the resulting <see cref="Project"/> carries the project's analyzers and source
/// generators (for example the Razor source generator for Blazor components).
/// </summary>
public sealed class WorkspaceLoader
{
    /// <summary>
    /// Opens a single project file and returns the loaded project together with any diagnostics
    /// the workspace produced while loading it.
    /// </summary>
    /// <param name="projectPath">Absolute path to a <c>.csproj</c> file.</param>
    /// <param name="cancellationToken">Token used to cancel the load.</param>
    public Task<ProjectLoadResult> LoadProjectAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        // Register the SDK toolset before any MSBuild/workspace type is touched. The actual
        // work is in a separate method so that the runtime does not load those assemblies while
        // JIT-compiling this one, before registration has had a chance to run.
        MSBuildRegistration.EnsureRegistered();
        return LoadProjectCoreAsync(projectPath, cancellationToken);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<ProjectLoadResult> LoadProjectCoreAsync(
        string projectPath,
        CancellationToken cancellationToken)
    {
        List<WorkspaceDiagnostic> diagnostics = [];

        using MSBuildWorkspace workspace = MSBuildWorkspace.Create();
        using WorkspaceEventRegistration failureRegistration =
            workspace.RegisterWorkspaceFailedHandler(args => diagnostics.Add(args.Diagnostic));

        Project project = await workspace
            .OpenProjectAsync(projectPath, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return new ProjectLoadResult(project, diagnostics);
    }
}

/// <summary>
/// The outcome of loading a project: the loaded <see cref="Project"/> and any diagnostics the
/// workspace reported while opening it.
/// </summary>
/// <param name="Project">The loaded project.</param>
/// <param name="Diagnostics">Diagnostics reported by the workspace during the load.</param>
public sealed record ProjectLoadResult(
    Project Project,
    IReadOnlyList<WorkspaceDiagnostic> Diagnostics);
