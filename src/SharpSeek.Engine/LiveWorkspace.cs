using System.Collections.Immutable;
using System.Runtime.CompilerServices;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;

namespace SharpSeek.Engine;

/// <summary>
/// A long-lived, mutable view of a loaded solution (or a single project, loaded into a solution).
/// Unlike <see cref="WorkspaceLoader"/> (which returns a one-shot snapshot and disposes its
/// workspace), this keeps the workspace alive so source edits can be applied in-memory — Roslyn
/// then recomputes incrementally, re-running source generators as needed.
/// </summary>
/// <remarks>
/// Edits are applied by forking the <see cref="Solution"/> in memory, never via
/// <c>Workspace.TryApplyChanges</c> — that method writes changes back to disk, which would both
/// be wrong here (the change already came from disk) and cause a file-watcher feedback loop.
/// </remarks>
public sealed class LiveWorkspace : IDisposable
{
    private readonly string _path;
    private readonly bool _isSolution;
    private MSBuildWorkspace _workspace;
    private Solution _solution;

    private LiveWorkspace(string path, bool isSolution, MSBuildWorkspace workspace, Solution solution)
    {
        _path = path;
        _isSolution = isSolution;
        _workspace = workspace;
        _solution = solution;
    }

    /// <summary>The current solution snapshot (including any in-memory edits applied so far).</summary>
    public Solution Solution => _solution;

    /// <summary>
    /// Opens a solution (<c>.sln</c>/<c>.slnx</c>) or a single project (<c>.csproj</c>) and keeps
    /// its workspace alive for incremental updates.
    /// </summary>
    public static Task<LiveWorkspace> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        // Register the SDK toolset before any MSBuild/workspace type is touched (see WorkspaceLoader).
        MSBuildRegistration.EnsureRegistered();
        return LoadCoreAsync(Path.GetFullPath(path), cancellationToken);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<LiveWorkspace> LoadCoreAsync(string path, CancellationToken cancellationToken)
    {
        bool isSolution = IsSolutionPath(path);
        MSBuildWorkspace workspace = MSBuildWorkspace.Create();
        Solution solution = await OpenAsync(workspace, path, isSolution, cancellationToken)
            .ConfigureAwait(false);
        return new LiveWorkspace(path, isSolution, workspace, solution);
    }

    private static async Task<Solution> OpenAsync(
        MSBuildWorkspace workspace, string path, bool isSolution, CancellationToken cancellationToken)
    {
        if (isSolution)
        {
            return await workspace.OpenSolutionAsync(path, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        Project project = await workspace.OpenProjectAsync(path, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return project.Solution;
    }

    private static bool IsSolutionPath(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Updates the in-memory text of a file already known to the workspace (a document or a
    /// Razor-style additional document). Returns <c>false</c> if the file is not part of the
    /// solution (the caller should reload). Does not touch disk.
    /// </summary>
    public bool TryApplyTextChange(string filePath, string text)
    {
        ImmutableArray<DocumentId> ids = _solution.GetDocumentIdsWithFilePath(filePath);
        if (ids.IsEmpty)
        {
            DocumentId? additional = FindAdditionalDocumentId(_solution, filePath);
            if (additional is null)
            {
                return false;
            }

            ids = [additional];
        }

        SourceText sourceText = SourceText.From(text);
        Solution updated = _solution;
        foreach (DocumentId id in ids)
        {
            if (updated.GetDocument(id) is not null)
            {
                updated = updated.WithDocumentText(id, sourceText);
            }
            else if (updated.GetAdditionalDocument(id) is not null)
            {
                updated = updated.WithAdditionalDocumentText(id, sourceText);
            }
        }

        _solution = updated;
        return true;
    }

    /// <summary>Re-opens the solution/project from disk, replacing the in-memory state.</summary>
    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        MSBuildWorkspace fresh = MSBuildWorkspace.Create();
        Solution solution = await OpenAsync(fresh, _path, _isSolution, cancellationToken)
            .ConfigureAwait(false);

        MSBuildWorkspace previous = _workspace;
        _workspace = fresh;
        _solution = solution;
        previous.Dispose();
    }

    /// <summary>Returns a problem description if a Razor generator failed to load, else null.</summary>
    public string? DetectGeneratorProblem() => GeneratorHealthCheck.DetectRazorGeneratorSkew(_solution);

    public void Dispose() => _workspace.Dispose();

    private static DocumentId? FindAdditionalDocumentId(Solution solution, string filePath)
    {
        string normalized = filePath.Replace('\\', '/');
        foreach (Project project in solution.Projects)
        {
            foreach (TextDocument document in project.AdditionalDocuments)
            {
                if (document.FilePath is { } path
                    && string.Equals(path.Replace('\\', '/'), normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return document.Id;
                }
            }
        }

        return null;
    }
}
