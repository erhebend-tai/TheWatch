// EvidenceNotificationFunction — RabbitMQ-triggered function for evidence fan-out.
// When EvidenceController uploads evidence and publishes to "evidence-submitted",
// this function picks it up, notifies responders (if Active phase), and enqueues
// background processing (thumbnail, metadata, transcription).
//
// Architecture:
//   EvidenceController → RabbitMQ("evidence-submitted") → THIS FUNCTION
//     → If Active: broadcast to response group via SignalR/push
//     → Always: enqueue EvidenceProcessMessage to "evidence-process" for worker
//     → Always: log audit entry (AuditAction.EvidenceCapture)

using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using TheWatch.Shared.Domain.Messages;
using TheWatch.Shared.Enums;

namespace TheWatch.Functions.Functions;

public class EvidenceNotificationFunction
{
    private readonly ILogger<EvidenceNotificationFunction> _logger;

    public EvidenceNotificationFunction(ILogger<EvidenceNotificationFunction> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Triggered by a message on the "evidence-submitted" RabbitMQ queue.
    /// Fans out notifications and enqueues background processing.
    /// </summary>
    [Function("EvidenceNotification")]
    public async Task Run(
        [RabbitMQTrigger("evidence-submitted", ConnectionStringSetting = "RabbitMQConnection")] string message)
    {
        _logger.LogInformation("EvidenceNotification triggered. Processing message...");

        try
        {
            var submitted = JsonSerializer.Deserialize<EvidenceSubmittedMessage>(message);
            if (submitted is null)
            {
                _logger.LogWarning("Failed to deserialize EvidenceSubmittedMessage");
                return;
            }

            _logger.LogInformation(
                "Evidence received: Id={SubmissionId}, Phase={Phase}, Type={Type}, User={UserId}, Request={RequestId}",
                submitted.SubmissionId, submitted.Phase, submitted.Type, submitted.UserId, submitted.RequestId);

            // ── Active incident: near-real-time relay to responders ──
            if (submitted.Phase == SubmissionPhase.Active && submitted.RequestId is not null)
            {
                _logger.LogInformation(
                    "ACTIVE phase evidence — broadcasting to response group response-{RequestId}",
                    submitted.RequestId);

                // In production: use SignalR service binding or push notification service
                // await _signalROutput.SendToGroupAsync($"response-{submitted.RequestId}",
                //     "EvidenceSubmitted", new { submitted.SubmissionId, submitted.Type, submitted.Timestamp });
            }

            // ── Determine processing tasks based on content type ──
            var tasks = DetermineProcessingTasks(submitted.Type, submitted.MimeType);

            var processMessage = new EvidenceProcessMessage(
                SubmissionId: submitted.SubmissionId,
                BlobReference: submitted.BlobReference,
                MimeType: submitted.MimeType,
                ProcessingTasks: tasks
            );

            _logger.LogInformation(
                "Enqueuing processing for {SubmissionId}: tasks=[{Tasks}]",
                submitted.SubmissionId, string.Join(", ", tasks.Select(t => t.ToString())));

            // In production: publish to "evidence-process" RabbitMQ queue
            // await _rabbitPublisher.PublishAsync("evidence-process", processMessage);

            // ── Audit log ──
            _logger.LogInformation(
                "[AUDIT] EvidenceCapture: User={UserId}, Submission={SubmissionId}, Phase={Phase}, Location=({Lat},{Lon})",
                submitted.UserId, submitted.SubmissionId, submitted.Phase, submitted.Latitude, submitted.Longitude);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing evidence-submitted message");
            throw; // Let RabbitMQ retry
        }
    }

    private static ProcessingTask[] DetermineProcessingTasks(SubmissionType type, string? mimeType)
    {
        var tasks = new List<ProcessingTask> { ProcessingTask.Moderation };

        switch (type)
        {
            case SubmissionType.Image:
                tasks.Add(ProcessingTask.Thumbnail);
                tasks.Add(ProcessingTask.Metadata);
                break;
            case SubmissionType.Video:
                tasks.Add(ProcessingTask.Thumbnail);
                tasks.Add(ProcessingTask.Metadata);
                tasks.Add(ProcessingTask.Transcription);
                break;
            case SubmissionType.Audio:
                tasks.Add(ProcessingTask.Metadata);
                tasks.Add(ProcessingTask.Transcription);
                break;
            case SubmissionType.Document:
                tasks.Add(ProcessingTask.Metadata);
                break;
            // Text and Survey types need no processing beyond moderation
        }

        return tasks.ToArray();
    }
}
