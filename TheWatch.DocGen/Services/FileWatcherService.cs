// =============================================================================
// FileWatcherService.cs — Monitors .cs files and publishes change events to RabbitMQ.
// =============================================================================
// Uses FileSystemWatcher to detect Created, Changed, Renamed events on .cs files
// in the solution directory tree. Each event is published to the "docgen-file-changed"
// RabbitMQ exchange as a JSON message.
//
// Message Format (published to RabbitMQ):
//   {
//     "FilePath": "C:\\src\\TheWatch-Aspire\\TheWatch.Data\\Models\\WorkItem.cs",
//     "ChangeType": "Changed",
//     "Timestamp": "2026-03-24T10:30:00Z"
//   }
//
// Debouncing:
//   FileSystemWatcher can fire multiple events for a single save (editor temp files,
//   atomic write patterns). Events for the same file within DebounceMs are coalesced.
//
// Example:
//   // Service starts automatically as IHostedService
//   // Watches DocGenOptions.SolutionRoot for .cs file changes
//   // Publishes to RabbitMQ exchange "docgen-file-changed"
//
// WAL: Every published message is logged with file path and change type.
// =============================================================================

using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using TheWatch.DocGen.Configuration;

namespace TheWatch.DocGen.Services;

/// <summary>
/// Background service that watches the solution directory for .cs file changes
/// and publishes events to RabbitMQ for processing by the doc generator.
/// </summary>
public class FileWatcherService : BackgroundService
{
    private readonly ILogger<FileWatcherService> _logger;
    private readonly DocGenOptions _options;
    private readonly IConnection _rabbitConnection;
    private readonly ConcurrentDictionary<string, DateTime> _debounceTracker = new();
    private FileSystemWatcher? _watcher;

    // RabbitMQ constants
    public const string ExchangeName = "docgen-file-changed";
    public const string QueueName = "docgen-file-changed-queue";
    public const string RoutingKey = "file.changed";

    public FileWatcherService(
        ILogger<FileWatcherService> logger,
        IOptions<DocGenOptions> options,
        IConnection rabbitConnection)
    {
        _logger = logger;
        _options = options.Value;
        _rabbitConnection = rabbitConnection;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.WatchEnabled)
        {
            _logger.LogInformation("[WAL-DOC] File watching disabled via configuration");
            return;
        }

        var solutionRoot = _options.SolutionRoot;
        if (string.IsNullOrEmpty(solutionRoot) || !Directory.Exists(solutionRoot))
        {
            _logger.LogWarning("[WAL-DOC] SolutionRoot not set or does not exist: {Path}", solutionRoot);
            return;
        }

        // Declare RabbitMQ exchange and queue (RabbitMQ.Client v6 sync API)
        var channel = _rabbitConnection.CreateModel();
        channel.ExchangeDeclare(ExchangeName, "direct", durable: true);
        channel.QueueDeclare(QueueName, durable: true, exclusive: false, autoDelete: false);
        channel.QueueBind(QueueName, ExchangeName, RoutingKey);

        _logger.LogInformation("[WAL-DOC] RabbitMQ exchange/queue declared: {Exchange} → {Queue}", ExchangeName, QueueName);

        // Create FileSystemWatcher
        _watcher = new FileSystemWatcher(solutionRoot)
        {
            Filter = "*.cs",
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        _watcher.Changed += (_, e) => OnFileChanged(e.FullPath, "Changed", channel);
        _watcher.Created += (_, e) => OnFileChanged(e.FullPath, "Created", channel);
        _watcher.Renamed += (_, e) => OnFileChanged(e.FullPath, "Renamed", channel);

        _logger.LogInformation("[WAL-DOC] FileSystemWatcher started on {Root}", solutionRoot);

        // Keep alive until cancellation
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[WAL-DOC] FileSystemWatcher stopping");
        }
    }

    private void OnFileChanged(string filePath, string changeType, IModel channel)
    {
        // Exclude paths
        var normalized = filePath.Replace('\\', '/');
        if (_options.ExcludedPaths.Any(ex => normalized.Contains($"/{ex}/", StringComparison.OrdinalIgnoreCase)))
            return;
        if (_options.ExcludedFiles.Any(ex => Path.GetFileName(filePath).Equals(ex, StringComparison.OrdinalIgnoreCase)))
            return;

        // Debounce: skip if we published for this file within DebounceMs
        var now = DateTime.UtcNow;
        if (_debounceTracker.TryGetValue(filePath, out var lastPublish)
            && (now - lastPublish).TotalMilliseconds < _options.DebounceMs)
        {
            return;
        }
        _debounceTracker[filePath] = now;

        // Publish to RabbitMQ
        var message = new FileChangedMessage
        {
            FilePath = filePath,
            ChangeType = changeType,
            Timestamp = now
        };

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));

        try
        {
            var props = channel.CreateBasicProperties();
            props.ContentType = "application/json";
            props.DeliveryMode = 2; // Persistent
            props.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            // Sync publish (v6 API, called from event handler)
            channel.BasicPublish(ExchangeName, RoutingKey, false, props, body);

            _logger.LogDebug("[WAL-DOC] Published file change: {ChangeType} {Path}", changeType, filePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[WAL-DOC] Failed to publish file change: {Path}", filePath);
        }
    }

    public override void Dispose()
    {
        _watcher?.Dispose();
        base.Dispose();
    }
}

/// <summary>
/// RabbitMQ message payload for file change events.
/// </summary>
public class FileChangedMessage
{
    public string FilePath { get; set; } = string.Empty;
    public string ChangeType { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}
