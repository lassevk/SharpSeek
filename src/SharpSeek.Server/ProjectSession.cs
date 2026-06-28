using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;

using SharpSeek.Engine;

namespace SharpSeek.Server;

/// <summary>
/// Holds the project the server operates on, loaded once and kept warm for the server's
/// lifetime. Incremental invalidation on file changes is tracked separately (issue #4).
/// </summary>
internal sealed class ProjectSession
{
    private readonly string _projectPath;
    private readonly ILogger<ProjectSession> _logger;
    private readonly WorkspaceLoader _loader = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    private Project? _project;
    private string? _generatorProblem;
    private bool _loaded;

    public ProjectSession(string projectPath, ILogger<ProjectSession> logger)
    {
        _projectPath = projectPath;
        _logger = logger;
    }

    /// <summary>
    /// Returns the loaded project, loading it on first use. Concurrent callers share a single
    /// load. Throws if the loaded project's Razor generator failed to run (a Roslyn-vs-SDK
    /// version skew), so callers get a loud, actionable error instead of silently incomplete
    /// results.
    /// </summary>
    public async Task<Project> GetProjectAsync(CancellationToken cancellationToken)
    {
        if (!_loaded)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_loaded)
                {
                    ProjectLoadResult result = await _loader
                        .LoadProjectAsync(_projectPath, cancellationToken)
                        .ConfigureAwait(false);

                    _project = result.Project;
                    _generatorProblem = GeneratorHealthCheck.DetectRazorGeneratorSkew(_project);
                    if (_generatorProblem is not null)
                    {
                        _logger.LogError("{Problem}", _generatorProblem);
                    }

                    _loaded = true;
                }
            }
            finally
            {
                _gate.Release();
            }
        }

        if (_generatorProblem is not null)
        {
            throw new InvalidOperationException(_generatorProblem);
        }

        return _project!;
    }
}
