// EvidenceProcessMessage — published to "evidence-process" RabbitMQ queue
// by EvidenceNotificationFunction after receiving an EvidenceSubmittedMessage.
//
// Consumer: TheWatch.DocGen Worker Service (Hangfire job)
//   → ThumbnailGenerationJob, MetadataExtractionJob, ContentHashVerificationJob
//
// ProcessingTasks specifies which operations the worker should perform.
// Not all tasks apply to all media types (e.g., Transcription only for audio/video).
//
// Example:
//   new EvidenceProcessMessage(
//       SubmissionId: "sub-789",
//       BlobReference: "evidence/req-123/img-001.jpg",
//       MimeType: "image/jpeg",
//       ProcessingTasks: new[] { ProcessingTask.Thumbnail, ProcessingTask.Metadata, ProcessingTask.Moderation }
//   );

namespace TheWatch.Shared.Domain.Messages;

public record EvidenceProcessMessage(
    string SubmissionId,
    string? BlobReference,
    string? MimeType,
    ProcessingTask[] ProcessingTasks
);

/// <summary>
/// Individual processing operations that can be requested for evidence.
/// Worker inspects this array to decide which jobs to run.
/// </summary>
public enum ProcessingTask
{
    /// <summary>Generate a 200x200 thumbnail image.</summary>
    Thumbnail,

    /// <summary>Extract EXIF data (images) or codec/duration info (audio/video).</summary>
    Metadata,

    /// <summary>Speech-to-text transcription for audio/video content.</summary>
    Transcription,

    /// <summary>Content moderation scan (NSFW, violence, PII, manipulation).</summary>
    Moderation
}
