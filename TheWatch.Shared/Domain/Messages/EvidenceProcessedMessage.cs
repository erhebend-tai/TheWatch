// EvidenceProcessedMessage — published to "evidence-processed" RabbitMQ queue
// by the Worker Service after completing background processing.
//
// Consumer: Dashboard.Api (Hangfire consumer)
//   → Updates EvidenceSubmission metadata via IEvidencePort.AttachProcessingResultAsync
//   → Broadcasts "EvidenceProcessed" via SignalR to connected dashboard clients
//
// Example:
//   new EvidenceProcessedMessage(
//       SubmissionId: "sub-789",
//       Result: new EvidenceProcessingResult { ThumbnailGenerated = true, ... }
//   );

using TheWatch.Shared.Domain.Models;

namespace TheWatch.Shared.Domain.Messages;

public record EvidenceProcessedMessage(
    string SubmissionId,
    EvidenceProcessingResult Result
);
