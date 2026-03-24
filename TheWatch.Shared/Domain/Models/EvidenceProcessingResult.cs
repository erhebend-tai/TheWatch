// EvidenceProcessingResult — output of the background processing pipeline (Worker Service).
// After an evidence submission is uploaded, the worker generates thumbnails, extracts
// metadata (EXIF, duration, dimensions), transcribes audio/video, and runs content moderation.
//
// Example:
//   var result = new EvidenceProcessingResult
//   {
//       SubmissionId = "sub-789",
//       ThumbnailGenerated = true,
//       MetadataExtracted = true,
//       Width = 1920, Height = 1080,
//       ExifData = new() { ["Make"] = "Apple", ["Model"] = "iPhone 15 Pro" },
//       ModerationFlags = new() { },   // empty = clean
//       ProcessedAt = DateTime.UtcNow,
//       ProcessingDurationMs = 1250
//   };

namespace TheWatch.Shared.Domain.Models;

public class EvidenceProcessingResult
{
    public string SubmissionId { get; set; } = string.Empty;

    // ── Processing outputs ─────────────────────────
    public bool ThumbnailGenerated { get; set; }
    public bool MetadataExtracted { get; set; }

    /// <summary>EXIF data extracted from images (camera model, GPS, exposure, etc.).</summary>
    public Dictionary<string, string>? ExifData { get; set; }

    /// <summary>Duration in seconds for audio/video content.</summary>
    public double? MediaDurationSeconds { get; set; }

    /// <summary>Image/video width in pixels.</summary>
    public int? Width { get; set; }
    /// <summary>Image/video height in pixels.</summary>
    public int? Height { get; set; }

    /// <summary>Transcribed text from audio/video content (speech-to-text).</summary>
    public string? TranscriptionText { get; set; }

    /// <summary>
    /// Content moderation flags. Empty list = clean content.
    /// Possible flags: "NSFW", "VIOLENCE_GRAPHIC", "PII_DETECTED", "MANIPULATED_MEDIA"
    /// </summary>
    public List<string> ModerationFlags { get; set; } = new();

    // ── Timing ─────────────────────────────────────
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
    public long ProcessingDurationMs { get; set; }
}
