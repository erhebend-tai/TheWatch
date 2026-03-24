// SubmissionStatus — tracks the lifecycle of an evidence submission through the pipeline.
// Pending → Uploading → Processing → Available (or Rejected/Archived/Expired).
// Example: if (submission.Status == SubmissionStatus.Available) ShowThumbnail();

namespace TheWatch.Shared.Enums;

public enum SubmissionStatus
{
    /// <summary>Created locally, not yet uploaded.</summary>
    Pending = 0,

    /// <summary>Binary data is being uploaded to blob storage.</summary>
    Uploading = 1,

    /// <summary>Worker is generating thumbnail / extracting metadata / transcribing.</summary>
    Processing = 2,

    /// <summary>Fully processed and available for viewing.</summary>
    Available = 3,

    /// <summary>Rejected by content moderation or integrity check.</summary>
    Rejected = 4,

    /// <summary>Soft-deleted or moved to archive tier.</summary>
    Archived = 5,

    /// <summary>Past retention period — marked for cleanup.</summary>
    Expired = 6
}
