using System.Collections.Concurrent;

using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;

using SharpSeek.Engine;

namespace SharpSeek.Server;

/// <summary>
/// Holds the project the server operates on, loaded once and kept warm for the server's lifetime.
/// A file-system watcher records changes; the project is then refreshed lazily on the next
/// request — applying source edits in-memory where possible, or reloading on structural changes.
/// </summary>
internal sealed class ProjectSession : IDisposable
{
    private static readonly string[] RelevantExtensions = [".cs", ".razor", ".cshtml", ".csproj"];

    private readonly string _projectPath;
    private readonly ILogger<ProjectSession> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, byte> _changedFiles =
        new(StringComparer.OrdinalIgnoreCase);

    private LiveWorkspace? _workspace;
    private FileSystemWatcher? _watcher;
    private string? _generatorProblem;
    private volatile bool _dirty;
    private volatile bool _structuralChange;

    public ProjectSession(string projectPath, ILogger<ProjectSession> logger)
    {
        _projectPath = projectPath;
        _logger = logger;
    }

    /// <summary>
    /// Returns the current project, loading it on first use and refreshing it if files have
    /// changed since the last request. Throws if the Razor generator failed to load (version
    /// skew), so callers get a loud error rather than silently incomplete results.
    /// </summary>
    public async Task<Project> GetProjectAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_workspace is null)
            {
                await InitializeAsync(cancellationToken).ConfigureAwait(false);
            }
            else if (_dirty)
            {
                await RefreshAsync(cancellationToken).ConfigureAwait(false);
            }

            if (_generatorProblem is not null)
            {
                throw new InvalidOperationException(_generatorProblem);
            }

            return _workspace!.Project;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _workspace = await LiveWorkspace.LoadAsync(_projectPath, cancellationToken).ConfigureAwait(false);
        CheckGeneratorHealth();
        StartWatching();
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        // Snapshot and clear the pending changes before clearing the dirty flag. A change that
        // races in here is best-effort: it re-sets the flag and is picked up next request.
        bool structural = _structuralChange;
        string[] changed = [.. _changedFiles.Keys];
        _structuralChange = false;
        _changedFiles.Clear();
        _dirty = false;

        if (structural)
        {
            await ReloadAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        bool reloadNeeded = false;
        foreach (string file in changed)
        {
            string? text = TryReadAllText(file);
            if (text is null || !_workspace!.TryApplyTextChange(file, text))
            {
                reloadNeeded = true;
                break;
            }
        }

        if (reloadNeeded)
        {
            await ReloadAsync(cancellationToken).ConfigureAwait(false);
        }
        else if (changed.Length > 0)
        {
            _logger.LogInformation("Applied {Count} in-memory file change(s).", changed.Length);
        }
    }

    private async Task ReloadAsync(CancellationToken cancellationToken)
    {
        await _workspace!.ReloadAsync(cancellationToken).ConfigureAwait(false);
        CheckGeneratorHealth();
        _logger.LogInformation("Reloaded project from disk.");
    }

    private void CheckGeneratorHealth()
    {
        _generatorProblem = _workspace!.DetectGeneratorProblem();
        if (_generatorProblem is not null)
        {
            _logger.LogError("{Problem}", _generatorProblem);
        }
    }

    private void StartWatching()
    {
        string? directory = Path.GetDirectoryName(_projectPath);
        if (directory is null)
        {
            return;
        }

        _watcher = new FileSystemWatcher(directory)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
            // A larger buffer reduces dropped events during bursty changes (e.g. a git checkout).
            InternalBufferSize = 64 * 1024,
        };
        _watcher.Changed += OnContentChanged;
        _watcher.Created += OnStructuralChange;
        _watcher.Deleted += OnStructuralChange;
        _watcher.Renamed += OnStructuralChange;
        _watcher.Error += OnWatcherError;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        // The buffer overflowed (too many changes at once) and events were dropped. Force a full
        // reload on the next request rather than risk serving stale results.
        _structuralChange = true;
        _dirty = true;
        _logger.LogWarning(e.GetException(), "File watcher error; the project will be reloaded.");
    }

    private void OnContentChanged(object sender, FileSystemEventArgs e)
    {
        if (!IsRelevant(e.FullPath))
        {
            return;
        }

        if (string.Equals(Path.GetExtension(e.FullPath), ".csproj", StringComparison.OrdinalIgnoreCase))
        {
            _structuralChange = true;
        }
        else
        {
            _changedFiles[e.FullPath] = 0;
        }

        _dirty = true;
    }

    private void OnStructuralChange(object sender, FileSystemEventArgs e)
    {
        if (!IsRelevant(e.FullPath))
        {
            return;
        }

        _structuralChange = true;
        _dirty = true;
    }

    private static bool IsRelevant(string path)
    {
        string normalized = path.Replace('\\', '/');
        if (normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/.git/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/.vs/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return RelevantExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }

    private static string? TryReadAllText(string path)
    {
        // The editor may still hold the file briefly after a save; retry a couple of times.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (IOException)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException)
            {
                Thread.Sleep(50);
            }
        }

        return null;
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _workspace?.Dispose();
        _gate.Dispose();
    }
}
