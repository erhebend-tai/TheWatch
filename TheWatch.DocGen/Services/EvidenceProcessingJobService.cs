// =============================================================================
// EvidenceProcessingJobService.cs — Hangfire job methods for evidence processing.
// =============================================================================
// Contains the background processing pipeline for submitted evidence:
//   - ThumbnailGenerationJob: Generate 200x200 thumbnail from image/video
//   - MetadataExtractionJob: Extract EXIF, duration, dimensions, codec info
//   - ContentHashVerificationJob: Re-hash blob and compare with stored hash
//   - ArchivalSweepJob (recurring): Move expired evidence to archive tier
//
// These methods are enqueued by the EvidenceRabbitMqConsumerService when it
// receives an EvidenceProcessMessage from the "evidence-process" queue.
//
// Hangfire Integration:
//   - Fire-and-forget: _jobClient.Enqueue<EvidenceProcessingJobService>(
//       j => j.ProcessEvidenceAsync(submissionId, tasks, ct));
//   - Recurring: RecurringJob.AddOrUpdate<EvidenceProcessingJobService>(
//       "evidence-archival-sweep", j => j.ArchivalSweepAsync(ct), "0 3 * * *");
//
// WAL: Job start/complete/fail events are logged with submission IDs and timing.
// =============================================================================

using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using TheWatch.Shared.Domain.Messages;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Enums;

namespace TheWatch.DocGen.Services;

/// <summary>
/// Hangfire job service for evidence processing pipeline.
/// Each public method is a Hangfire job entry point.
/// </summary>
public class EvidenceProcessingJobService
{
    private readonly ILogger<EvidenceProcessingJobService> _logger;
    private readonly IEvidencePort _evidencePort;
    private readonly IBlobStoragePort _blobStorage;

    public EvidenceProcessingJobService(
        ILogger<EvidenceProcessingJobService> logger,
        IEvidencePort evidencePort,
        IBlobStoragePort blobStorage)
    {
        _logger = logger;
        _evidencePort = evidencePort;
        _blobStorage = blobStorage;
    }

    /// <summary>
    /// Main entry point: processes evidence submission through the pipeline.
    /// Called by EvidenceRabbitMqConsumerService via Hangfire Enqueue.
    /// </summary>
    public async Task ProcessEvidenceAsync(string submissionId, ProcessingTask[] tasks, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("[WAL-EVIDENCE] Processing started: SubmissionId={Id}, Tasks=[{Tasks}]",
            submissionId, string.Join(", ", tasks.Select(t => t.ToString())));

        var result = new EvidenceProcessingResult
        {
            SubmissionId = submissionId
        };

        try
        {
            // Update status to Processing
            await _evidencePort.UpdateStatusAsync(submissionId, SubmissionStatus.Processing, ct);

            // Get the submission for context
            var subResult = await _evidencePort.GetByIdAsync(submissionId, ct);
            if (!subResult.Success || subResult.Data is null)
            {
                _logger.LogWarning("[WAL-EVIDENCE] Submission {Id} not found, skipping", submissionId);
                return;
            }

            var submission = subResult.Data;

            foreach (var task in tasks)
            {
                switch (task)
                {
                    case ProcessingTask.Thumbnail:
                        await GenerateThumbnailAsync(submission, result, ct);
                        break;
                    case ProcessingTask.Metadata:
                        await ExtractMetadataAsync(submission, result, ct);
                        break;
                    case ProcessingTask.Transcription:
                        await TranscribeAsync(submission, result, ct);
                        break;
                    case ProcessingTask.Moderation:
                        await ModeratContentAsync(submission, result, ct);
                        break;
                }
            }

            sw.Stop();
            result.ProcessedAt = DateTime.UtcNow;
            result.ProcessingDurationMs = sw.ElapsedMilliseconds;

            // Attach results and update status
            await _evidencePort.AttachProcessingResultAsync(submissionId, result, ct);

            _logger.LogInformation(
                "[WAL-EVIDENCE] Processing complete: SubmissionId={Id}, Duration={Ms}ms, " +
                "Thumbnail={Thumb}, Metadata={Meta}, Moderation=[{Flags}]",
                submissionId, sw.ElapsedMilliseconds,
                result.ThumbnailGenerated, result.MetadataExtracted,
                string.Join(", ", result.ModerationFlags));

            // In production: publish EvidenceProcessedMessage to "evidence-processed" queue
            // await _rabbitPublisher.PublishAsync("evidence-processed",
            //     new EvidenceProcessedMessage(submissionId, result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WAL-EVIDENCE] Processing failed: SubmissionId={Id}", submissionId);
            await _evidencePort.UpdateStatusAsync(submissionId, SubmissionStatus.Rejected, ct);
            throw; // Let Hangfire retry
        }
    }

    /// <summary>
    /// Generate a 200x200 thumbnail image. In production, use SkiaSharp/ImageMagick.
    /// Mock implementation creates a placeholder thumbnail blob.
    /// </summary>
    public async Task GenerateThumbnailAsync(EvidenceSubmission submission, EvidenceProcessingResult result, CancellationToken ct)
    {
        _logger.LogDebug("[WAL-EVIDENCE] Generating thumbnail for {Id}", submission.Id);

        if (submission.BlobReference is null)
        {
            _logger.LogWarning("[WAL-EVIDENCE] No blob reference for thumbnail generation: {Id}", submission.Id);
            return;
        }

        // In production: download blob → resize to 200x200 → upload thumbnail
        // For mock: create a placeholder
        var thumbnailName = $"thumbnails/{submission.Id}_thumb.jpg";
        var placeholderBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }; // JPEG magic bytes stub
        using var thumbStream = new MemoryStream(placeholderBytes);
        await _blobStorage.UploadAsync("thumbnails", thumbnailName, thumbStream, "image/jpeg", ct);

        result.ThumbnailGenerated = true;
        result.Width = 200;
        result.Height = 200;

        _logger.LogDebug("[WAL-EVIDENCE] Thumbnail generated: {Path}", thumbnailName);
    }

    /// <summary>
    /// Extract metadata (EXIF for images, duration/codec for audio/video).
    /// In production: use MetadataExtractor, FFProbe, or similar libraries.
    /// </summary>
    public Task ExtractMetadataAsync(EvidenceSubmission submission, EvidenceProcessingResult result, CancellationToken ct)
    {
        _logger.LogDebug("[WAL-EVIDENCE] Extracting metadata for {Id}", submission.Id);

        // Mock metadata extraction based on content type
        result.MetadataExtracted = true;
        result.ExifData = new Dictionary<string, string>
        {
            ["Software"] = $"TheWatch/{submission.AppVersion ?? "1.0.0"}",
            ["DeviceModel"] = submission.DeviceModel ?? "Unknown",
            ["CaptureTime"] = (submission.OfflineCapturedAt ?? submission.SubmittedAt).ToString("O"),
            ["GPS.Latitude"] = submission.Latitude.ToString("F6"),
            ["GPS.Longitude"] = submission.Longitude.ToString("F6")
        };

        if (submission.SubmissionType is SubmissionType.Audio or SubmissionType.Video)
        {
            // Mock duration — in production, extract via FFProbe
            result.MediaDurationSeconds = submission.FileSizeBytes / 16000.0; // rough estimate
        }

        if (submission.SubmissionType is SubmissionType.Image or SubmissionType.Video)
        {
            // Mock dimensions — in production, extract from file headers
            result.Width = result.Width ?? 1920;
            result.Height = result.Height ?? 1080;
        }

        _logger.LogDebug("[WAL-EVIDENCE] Metadata extracted for {Id}: {Count} EXIF entries",
            submission.Id, result.ExifData.Count);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Transcribe audio/video content using speech-to-text.
    /// In production: use Azure Cognitive Services, Google Cloud Speech, or Whisper API.
    /// </summary>
    public Task TranscribeAsync(EvidenceSubmission submission, EvidenceProcessingResult result, CancellationToken ct)
    {
        _logger.LogDebug("[WAL-EVIDENCE] Transcribing audio/video for {Id}", submission.Id);

        // Mock transcription — in production, send to speech-to-text API
        result.TranscriptionText = $"[Transcription pending: {submission.SubmissionType} content from {submission.SubmittedAt:u}]";

        _logger.LogDebug("[WAL-EVIDENCE] Transcription placeholder set for {Id}", submission.Id);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Content moderation scan. In production: use Azure Content Safety, AWS Rekognition,
    /// or Google Cloud Vision for NSFW/violence detection, PII scanning, manipulation detection.
    /// </summary>
    public Task ModeratContentAsync(EvidenceSubmission submission, EvidenceProcessingResult result, CancellationToken ct)
    {
        _logger.LogDebug("[WAL-EVIDENCE] Running content moderation for {Id}", submission.Id);

        // Mock moderation — in production, call moderation API
        // For now: always pass (empty flags = clean)
        result.ModerationFlags = new List<string>();

        _logger.LogDebug("[WAL-EVIDENCE] Moderation passed for {Id}: no flags", submission.Id);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Verify content integrity by re-hashing the blob and comparing with stored hash.
    /// Scheduled as a periodic check or on-demand for chain-of-custody verification.
    /// </summary>
    public async Task ContentHashVerificationJobAsync(string submissionId, CancellationToken ct)
    {
        _logger.LogInformation("[WAL-EVIDENCE] Hash verification for {Id}", submissionId);

        var subResult = await _evidencePort.GetByIdAsync(submissionId, ct);
        if (!subResult.Success || subResult.Data?.BlobReference is null)
        {
            _logger.LogWarning("[WAL-EVIDENCE] Submission {Id} not found for hash verification", submissionId);
            return;
        }

        var submission = subResult.Data;
        var blobResult = await _blobStorage.DownloadAsync("evidence", submission.BlobReference, ct);
        if (!blobResult.Success || blobResult.Data is null)
        {
            _logger.LogWarning("[WAL-EVIDENCE] Blob not found for hash verification: {Ref}", submission.BlobReference);
            return;
        }

        using var stream = blobResult.Data;
        var hashBytes = await SHA256.HashDataAsync(stream, ct);
        var computedHash = Convert.ToHexStringLower(hashBytes);

        if (computedHash == submission.ContentHash)
        {
            _logger.LogInformation("[WAL-EVIDENCE] Hash verification PASSED for {Id}", submissionId);
        }
        else
        {
            _logger.LogWarning(
                "[WAL-EVIDENCE] Hash verification FAILED for {Id}: stored={Stored}, computed={Computed}",
                submissionId, submission.ContentHash, computedHash);

            // Flag as potentially tampered
            await _evidencePort.UpdateStatusAsync(submissionId, SubmissionStatus.Rejected, ct);
        }
    }

    /// <summary>
    /// Recurring job: sweep expired evidence to archive tier.
    /// Runs daily at 3 AM (configured via Hangfire RecurringJob).
    /// Moves submissions past ExpiresAt to Archived status and optionally
    /// moves blobs to cool/archive storage tier.
    /// </summary>
    public async Task ArchivalSweepAsync(CancellationToken ct)
    {
        _logger.LogInformation("[WAL-EVIDENCE] Archival sweep started at {Now}", DateTime.UtcNow);

        var query = new EvidenceQuery
        {
            Status = SubmissionStatus.Available,
            MaxSubmittedAt = DateTime.UtcNow.AddDays(-90), // Archive anything older than 90 days
            Take = 100
        };

        var result = await _evidencePort.QueryAsync(query, ct);
        if (!result.Success || result.Data is null)
        {
            _logger.LogInformation("[WAL-EVIDENCE] Archival sweep: no eligible submissions");
            return;
        }

        var archived = 0;
        foreach (var submission in result.Data)
        {
            if (submission.ExpiresAt.HasValue && submission.ExpiresAt.Value > DateTime.UtcNow)
                continue; // Not yet expired

            await _evidencePort.UpdateStatusAsync(submission.Id, SubmissionStatus.Archived, ct);
            archived++;

            // In production: move blob to archive storage tier
            // await _blobStorage.MoveTierAsync("evidence", submission.BlobReference, StorageTier.Archive, ct);
        }

        _logger.LogInformation("[WAL-EVIDENCE] Archival sweep complete: {Count} submissions archived", archived);
    }
}
