// EvidenceSubmittedMessage — published to "evidence-submitted" RabbitMQ queue
// when a user uploads evidence via EvidenceController.
//
// Consumer: EvidenceNotificationFunction (Azure Functions)
//   → Notifies responders (if Active phase)
//   → Enqueues EvidenceProcessMessage for background processing
//
// Example:
//   channel.BasicPublish("", "evidence-submitted", null,
//       JsonSerializer.SerializeToUtf8Bytes(new EvidenceSubmittedMessage(...)));

using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Domain.Messages;

public record EvidenceSubmittedMessage(
    string SubmissionId,
    string? RequestId,
    string UserId,
    SubmissionPhase Phase,
    SubmissionType Type,
    string? BlobReference,
    string? MimeType,
    double Latitude,
    double Longitude,
    DateTime Timestamp
);
