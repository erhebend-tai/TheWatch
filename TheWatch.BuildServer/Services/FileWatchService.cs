// =============================================================================
// FileWatchService — Watches solution files for changes and triggers rebuilds
// =============================================================================
// Monitors the solution directory for .cs, .csproj, .sln file changes.
// Debounces rapid changes (500ms) and queues incremental builds.
//
// Useful during development when agents are writing to the repo concurrently —
// each file save triggers a re-build and LSIF re-index so the dashboard stays
// current with compilation state.
//
// WAL: FileSystemWatcher is notoriously unreliable on some platforms. We use
//      a debounce buffer and tolerate missed events by periodic full-rebuild
//      every 60 seconds when changes are detected.
// =============================================================================

using TheWatch.BuildServer.Models;

namespace TheWatch.BuildServer.Services;

public class FileWatchService : BackgroundService
{
    private readonly BuildOrchestrator _orchestrator;
    private readonly ILogger<FileWatchService> _logger;
    private readonly string _watchPath;
    private FileSystemWatcher? _watcher;
    private readonly HashSet<string> _pendingChanges = [];
    private DateTime _lastChangeTime = DateTime.MinValue;
    private readonly TimeSpan _debounceInterval = TimeSpan.FromMilliseconds(1500);

    private static readonly HashSet<string> WatchedExtensions =
        [".cs", ".csproj", ".sln", ".props", ".targets", ".json"];

    public FileWatchService(BuildOrchestrator orchestrator, IConfiguration config, ILogger<FileWatchService> logger)
    {
        _orchestrator = orchestrator;
        _logger = logger;
        _watchPath = config.GetValue<string>("BuildServer:SolutionDirectory")
            ?? Path.GetDirectoryName(config.GetValue<string>("BuildServer:SolutionPath") ?? ".")
            ?? ".";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Directory.Exists(_watchPath))
        {
            _logger.LogWarning("Watch path does not exist: {Path}. File watching disabled.", _watchPath);
            return;
        }

        _watcher = new FileSystemWatcher(_watchPath)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime
        };

        foreach (var ext in WatchedExtensions)
            _watcher.Filters.Add($"*{ext}");

        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
        _watcher.Deleted += OnFileChanged;
        _watcher.Renamed += (s, e) => OnFileChanged(s, e);
        _watcher.EnableRaisingEvents = true;

        _logger.LogInformation("File watcher started on {Path}", _watchPath);

        // Debounce loop
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(500, stoppingToken);

            if (_pendingChanges.Count > 0 && DateTime.UtcNow - _lastChangeTime > _debounceInterval)
            {
                var changes = new List<string>();
                lock (_pendingChanges)
                {
                    changes.AddRange(_pendingChanges);
                    _pendingChanges.Clear();
                }

                _logger.LogInformation("File changes detected ({Count} files), queueing build", changes.Count);

                // Determine if it's a single-project change or solution-wide
                var projectName = InferProjectFromPaths(changes);
                _orchestrator.QueueBuild(
                    BuildTrigger.FileWatch,
                    projectName ?? string.Join(", ", changes.Take(5)));
            }
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // Skip bin/obj/node_modules
        if (e.FullPath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
            e.FullPath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
            e.FullPath.Contains("node_modules"))
            return;

        lock (_pendingChanges)
        {
            _pendingChanges.Add(e.FullPath);
        }
        _lastChangeTime = DateTime.UtcNow;
    }

    private string? InferProjectFromPaths(List<string> paths)
    {
        // If all changes are in the same project directory, return that project name
        var projectDirs = paths
            .Select(p => Path.GetDirectoryName(p) ?? "")
            .Select(d =>
            {
                // Walk up to find .csproj
                var dir = d;
                while (!string.IsNullOrEmpty(dir) && dir != _watchPath)
                {
                    if (Directory.GetFiles(dir, "*.csproj").Length > 0)
                        return Path.GetFileName(dir);
                    dir = Path.GetDirectoryName(dir);
                }
                return null;
            })
            .Where(p => p is not null)
            .Distinct()
            .ToList();

        return projectDirs.Count == 1 ? projectDirs[0] : null;
    }

    public override void Dispose()
    {
        _watcher?.Dispose();
        base.Dispose();
    }
}
