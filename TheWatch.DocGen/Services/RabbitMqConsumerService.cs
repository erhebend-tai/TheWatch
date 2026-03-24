// =============================================================================
// RabbitMqConsumerService.cs — Consumes file-change events from RabbitMQ and
// enqueues Hangfire jobs for documentation generation.
// =============================================================================
// Listens to the "docgen-file-changed-queue" RabbitMQ queue.
// On each message, enqueues a Hangfire fire-and-forget job that:
//   1. Analyzes the changed file with Roslyn
//   2. Generates XML doc stubs for undocumented members
//   3. Writes the stubs back to the source file
//
// Hangfire handles retry, failure logging, and job deduplication.
//
// Message Flow:
//   FileSystemWatcher → RabbitMQ → This Consumer → Hangfire → DocGenJobService
//
// Example:
//   // Runs as IHostedService — starts consuming on application start
//   // Each consumed message becomes a Hangfire job on "docgen-default" queue
//
// WAL: Every consumed message and enqueued job is logged.
// =============================================================================

using System.Text;
using System.Text.Json;
using Hangfire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TheWatch.DocGen.Configuration;

namespace TheWatch.DocGen.Services;

/// <summary>
/// Consumes file-change messages from RabbitMQ and dispatches Hangfire jobs
/// for per-file documentation analysis and generation.
/// </summary>
public class RabbitMqConsumerService : BackgroundService
{
    private readonly ILogger<RabbitMqConsumerService> _logger;
    private readonly DocGenOptions _options;
    private readonly IConnection _rabbitConnection;
    private readonly IBackgroundJobClient _jobClient;

    public RabbitMqConsumerService(
        ILogger<RabbitMqConsumerService> logger,
        IOptions<DocGenOptions> options,
        IConnection rabbitConnection,
        IBackgroundJobClient jobClient)
    {
        _logger = logger;
        _options = options.Value;
        _rabbitConnection = rabbitConnection;
        _jobClient = jobClient;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[WAL-DOC] RabbitMQ consumer starting on queue: {Queue}", FileWatcherService.QueueName);

        var channel = _rabbitConnection.CreateModel();

        // Prefetch 5 messages at a time to avoid overwhelming the doc generator
        channel.BasicQos(prefetchSize: 0, prefetchCount: 5, global: false);

        var consumer = new EventingBasicConsumer(channel);
        consumer.Received += (_, ea) =>
        {
            try
            {
                var body = Encoding.UTF8.GetString(ea.Body.Span);
                var message = JsonSerializer.Deserialize<FileChangedMessage>(body);

                if (message is null || string.IsNullOrEmpty(message.FilePath))
                {
                    _logger.LogWarning("[WAL-DOC] Received invalid message, skipping");
                    channel.BasicAck(ea.DeliveryTag, false);
                    return;
                }

                // Verify file still exists (it might have been deleted between event and processing)
                if (!File.Exists(message.FilePath))
                {
                    _logger.LogDebug("[WAL-DOC] File no longer exists, skipping: {Path}", message.FilePath);
                    channel.BasicAck(ea.DeliveryTag, false);
                    return;
                }

                // Enqueue Hangfire job for this file
                var jobId = _jobClient.Enqueue<DocGenJobService>(
                    job => job.ProcessSingleFileAsync(message.FilePath, CancellationToken.None));

                _logger.LogInformation(
                    "[WAL-DOC] Enqueued Hangfire job {JobId} for {ChangeType}: {Path}",
                    jobId, message.ChangeType, message.FilePath);

                channel.BasicAck(ea.DeliveryTag, false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[WAL-DOC] Error processing RabbitMQ message");
                // Nack and requeue for retry
                channel.BasicNack(ea.DeliveryTag, false, requeue: true);
            }
        };

        channel.BasicConsume(
            queue: FileWatcherService.QueueName,
            autoAck: false,
            consumer: consumer);

        _logger.LogInformation("[WAL-DOC] RabbitMQ consumer started, waiting for messages");

        // Keep alive until cancellation
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[WAL-DOC] RabbitMQ consumer stopping");
        }
        finally
        {
            channel.Close();
        }
    }
}
