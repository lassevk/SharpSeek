using System.Collections.Concurrent;

using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;

using SharpSeek.Engine;

namespace SharpSeek.Server;

/// <summary>
/// Owns the solution the server operates on. The target is discovered from the session - an
/// explicit override, else the client's workspace roots, else the working directory - loaded once
/// and kept warm. A file-system watcher records changes; the solution is refreshed lazily on the
/// next request (incremental for source edits, reload for structural/.csproj changes).
/// </summary>
internal sealed class ProjectSession : IDisposable
{
    private static readonly string[] RelevantExtensions =
        [".cs", ".razor", ".cshtml", ".csproj", ".sln", ".slnx"];

    private readonly string? _explicitPath;
    private readonly ILogger<ProjectSession> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, byte> _changedFiles =
        new(StringComparer.OrdinalIgnoreCase);

    private LiveWorkspace? _workspace;
    private FileSystemWatcher? _watcher;
    private string? _loadedPath;
    private string? _generatorProblem;
    private volatile bool _dirty;
    private volatile bool _structuralChange;

    public ProjectSession(string? explicitPath, ILogger<ProjectSession> logger)
    {
        _explicitPath = explicitPath;
        _logger = logger;
    }

    /// <summary>
    /// Returns the current solution, discovering and loading it on first use and refreshing it if
    /// files have changed. Throws if the Razor generator failed to load (version skew).
    /// </summary>
    public async Task<Solution> GetSolutionAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_workspace is null)
            {
                // Discover from the session: an explicit override, else the working directory the
                // server was launched in (which a client such as Claude Code sets to the project
                // folder). activate_project can switch it at runtime.
                string target = _explicitPath
                    ?? ProjectDiscovery.Discover(Directory.GetCurrentDirectory())
                    ?? throw new InvalidOperationException(
                        "No .NET solution or project was found for this session. Pass --project, " +
                        "set SHARPSEEK_PROJECT, or call activate_project with a path.");
                await LoadTargetAsync(target, cancellationToken).ConfigureAwait(false);
            }
            else if (_dirty)
            {
                await RefreshAsync(cancellationToken).ConfigureAwait(false);
            }

            if (_generatorProblem is not null)
            {
                throw new InvalidOperationException(_generatorProblem);
            }

            return _workspace!.Solution;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Switches the analysed solution/project to the given path (file or directory).</summary>
    public async Task<string> ActivateAsync(string path, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string target = ResolveTarget(path)
                ?? throw new InvalidOperationException(
                    $"No .NET solution or project was found at '{path}'.");
            await LoadTargetAsync(target, cancellationToken).ConfigureAwait(false);
            return _loadedPath!;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task LoadTargetAsync(string target, CancellationToken cancellationToken)
    {
        _workspace?.Dispose();
        _workspace = await LiveWorkspace.LoadAsync(target, cancellationToken).ConfigureAwait(false);
        _loadedPath = Path.GetFullPath(target);

        _generatorProblem = _workspace.DetectGeneratorProblem();
        if (_generatorProblem is not null)
        {
            _logger.LogError("{Problem}", _generatorProblem);
        }

        _changedFiles.Clear();
        _dirty = false;
        _structuralChange = false;
        StartWatching(Path.GetDirectoryName(_loadedPath));

        _logger.LogInformation(
            "Loaded {Path} ({Count} project(s)).", _loadedPath, _workspace.Solution.Projects.Count());
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
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
        _generatorProblem = _workspace.DetectGeneratorProblem();
        if (_generatorProblem is not null)
        {
            _logger.LogError("{Problem}", _generatorProblem);
        }

        _logger.LogInformation("Reloaded {Path} from disk.", _loadedPath);
    }

    private static string? ResolveTarget(string path)
    {
        if (File.Exists(path))
        {
            return Path.GetFullPath(path);
        }

        return Directory.Exists(path) ? ProjectDiscovery.Discover(path) : null;
    }

    private void StartWatching(string? directory)
    {
        _watcher?.Dispose();
        if (directory is null || !Directory.Exists(directory))
        {
            return;
        }

        _watcher = new FileSystemWatcher(directory)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
            InternalBufferSize = 64 * 1024,
        };
        _watcher.Changed += OnContentChanged;
        _watcher.Created += OnStructuralChange;
        _watcher.Deleted += OnStructuralChange;
        _watcher.Renamed += OnStructuralChange;
        _watcher.Error += OnWatcherError;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnContentChanged(object sender, FileSystemEventArgs e)
    {
        if (!IsRelevant(e.FullPath))
        {
            return;
        }

        string extension = Path.GetExtension(e.FullPath);
        if (extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
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

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _structuralChange = true;
        _dirty = true;
        _logger.LogWarning(e.GetException(), "File watcher error; the project will be reloaded.");
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
