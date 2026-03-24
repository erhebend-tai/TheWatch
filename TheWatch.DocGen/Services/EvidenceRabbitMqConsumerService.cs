// =============================================================================
// EvidenceRabbitMqConsumerService.cs — Consumes evidence-process messages from
// RabbitMQ and enqueues Hangfire jobs for background evidence processing.
// =============================================================================
// Listens to the "evidence-process" RabbitMQ queue.
// On each message, enqueues a Hangfire fire-and-forget job that runs the
// EvidenceProcessingJobService pipeline (thumbnail, metadata, transcription, moderation).
//
// Message Flow:
//   EvidenceNotificationFunction → RabbitMQ("evidence-process") → This Consumer
//     → Hangfire → EvidenceProcessingJobService.ProcessEvidenceAsync
//
// Example:
//   // Runs as IHostedService alongside the DocGen consumers
//   // Each consumed message becomes a Hangfire job on "evidence-processing" queue
//
// WAL: Every consumed message and enqueued job is logged.
// =============================================================================

using System.Text;
using System.Text.Json;
using Hangfire;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TheWatch.Shared.Domain.Messages;

namespace TheWatch.DocGen.Services;

public class EvidenceRabbitMqConsumerService : BackgroundService
{
    public const string QueueName = "evidence-process";

    private readonly ILogger<EvidenceRabbitMqConsumerService> _logger;
    private readonly IConnection _rabbitConnection;
    private readonly IBackgroundJobClient _jobClient;

    public EvidenceRabbitMqConsumerService(
        ILogger<EvidenceRabbitMqConsumerService> logger,
        IConnection rabbitConnection,
        IBackgroundJobClient jobClient)
    {
        _logger = logger;
        _rabbitConnection = rabbitConnection;
        _jobClient = jobClient;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[WAL-EVIDENCE] RabbitMQ consumer starting on queue: {Queue}", QueueName);

        var channel = _rabbitConnection.CreateModel();

        // Declare the queue (idempotent)
        channel.QueueDeclare(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        // Prefetch 3 at a time — evidence processing is heavier than doc gen
        channel.BasicQos(prefetchSize: 0, prefetchCount: 3, global: false);

        var consumer = new EventingBasicConsumer(channel);
        consumer.Received += (_, ea) =>
        {
            try
            {
                var body = Encoding.UTF8.GetString(ea.Body.Span);
                var message = JsonSerializer.Deserialize<EvidenceProcessMessage>(body);

                if (message is null || string.IsNullOrEmpty(message.SubmissionId))
                {
                    _logger.LogWarning("[WAL-EVIDENCE] Received invalid evidence-process message, skipping");
                    channel.BasicAck(ea.DeliveryTag, false);
                    return;
                }

                // Enqueue Hangfire job for evidence processing
                var jobId = _jobClient.Enqueue<EvidenceProcessingJobService>(
                    job => job.ProcessEvidenceAsync(
                        message.SubmissionId,
                        message.ProcessingTasks,
                        CancellationToken.None));

                _logger.LogInformation(
                    "[WAL-EVIDENCE] Enqueued Hangfire job {JobId} for submission {SubmissionId}, tasks=[{Tasks}]",
                    jobId, message.SubmissionId,
                    string.Join(", ", message.ProcessingTasks.Select(t => t.ToString())));

                channel.BasicAck(ea.DeliveryTag, false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[WAL-EVIDENCE] Error processing evidence-process message");
                channel.BasicNack(ea.DeliveryTag, false, requeue: true);
            }
        };

        channel.BasicConsume(
            queue: QueueName,
            autoAck: false,
            consumer: consumer);

        _logger.LogInformation("[WAL-EVIDENCE] RabbitMQ evidence consumer started, waiting for messages");

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[WAL-EVIDENCE] RabbitMQ evidence consumer stopping");
        }
        finally
        {
            channel.Close();
        }
    }
}
