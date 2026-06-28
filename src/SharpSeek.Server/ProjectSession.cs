using Microsoft.CodeAnalysis;

using SharpSeek.Engine;

namespace SharpSeek.Server;

/// <summary>
/// Holds the project the server operates on, loaded once and kept warm for the server's
/// lifetime. Incremental invalidation on file changes is tracked separately (issue #4).
/// </summary>
internal sealed class ProjectSession
{
    private readonly string _projectPath;
    private readonly WorkspaceLoader _loader = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Project? _project;

    public ProjectSession(string projectPath) => _projectPath = projectPath;

    /// <summary>
    /// Returns the loaded project, loading it on first use. Concurrent callers share a single
    /// load.
    /// </summary>
    public async Task<Project> GetProjectAsync(CancellationToken cancellationToken)
    {
        if (_project is not null)
        {
            return _project;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _project ??= (await _loader
                .LoadProjectAsync(_projectPath, cancellationToken)
                .ConfigureAwait(false)).Project;
            return _project;
        }
        finally
        {
            _gate.Release();
        }
    }
}
