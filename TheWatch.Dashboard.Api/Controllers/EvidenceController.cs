// EvidenceController — REST endpoints for evidence submission and retrieval.
// Mobile clients and the dashboard both drive this controller.
//
// Endpoints:
//   POST   /api/evidence/upload                  — Generic multipart upload (file + metadata JSON)
//   POST   /api/evidence/{requestId}/photo       — Photo upload, generates thumbnail
//   POST   /api/evidence/{requestId}/video       — Video upload, extracts keyframe
//   POST   /api/evidence/{requestId}/audio       — Audio upload, queues transcription
//   POST   /api/evidence/{requestId}/sitrep      — JSON text report (sitrep)
//   POST   /api/evidence/text                    — Text-only submission (sitreps, notes)
//   GET    /api/evidence/{id}                    — Get submission metadata
//   GET    /api/evidence/request/{reqId}         — All evidence for an incident
//   GET    /api/evidence/user/{userId}           — All evidence for a user
//   GET    /api/evidence/{id}/download           — Download blob (signed URL redirect)
//   DELETE /api/evidence/{id}                    — Soft delete (mark Archived)
//   POST   /api/evidence/{id}/offline-sync       — Sync an offline-captured submission
//
// WAL: Upload flow:
//   1. Client POSTs multipart form (file + metadata)
//   2. Controller hashes content (SHA-256), uploads to IBlobStoragePort
//   3. Creates EvidenceSubmission via IEvidencePort
//   4. Broadcasts via SignalR to response group (DashboardHub.NotifyEvidenceSubmitted)
//   5. Logs to IAuditTrail with chain-of-custody entry
//   6. Returns 202 Accepted with submission ID
//
// For typed endpoints (/photo, /video, /audio):
//   - Photo: schedules thumbnail generation (mock: immediate, prod: Worker Service)
//   - Video: schedules keyframe extraction (mock: immediate, prod: Worker Service)
//   - Audio: queues for transcription (mock: placeholder, prod: Whisper/Azure Speech)

using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using TheWatch.Dashboard.Api.Hubs;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Enums;

namespace TheWatch.Dashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EvidenceController : ControllerBase
{
    private readonly IEvidencePort _evidencePort;
    private readonly IBlobStoragePort _blobStorage;
    private readonly IAuditTrail _auditTrail;
    private readonly IHubContext<DashboardHub> _hubContext;
    private readonly ILogger<EvidenceController> _logger;

    public EvidenceController(
        IEvidencePort evidencePort,
        IBlobStoragePort blobStorage,
        IAuditTrail auditTrail,
        IHubContext<DashboardHub> hubContext,
        ILogger<EvidenceController> logger)
    {
        _evidencePort = evidencePort;
        _blobStorage = blobStorage;
        _auditTrail = auditTrail;
        _hubContext = hubContext;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────
    // Typed Upload: Photo
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Upload a photo for an incident. Generates a thumbnail.
    /// Client sends: form field "metadata" (JSON) + form file "file" (image binary).
    /// </summary>
    [HttpPost("{requestId}/photo")]
    [RequestSizeLimit(52_428_800)] // 50 MB max for photos
    public async Task<IActionResult> UploadPhoto(
        string requestId,
        [FromForm] string metadata,
        IFormFile file,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Photo file is required" });

        if (!file.ContentType.StartsWith("image/"))
            return BadRequest(new { error = "File must be an image (image/*)" });

        var meta = DeserializeMeta(metadata, requestId);
        if (meta is null)
            return BadRequest(new { error = "Invalid metadata JSON" });

        var (submission, errorResult) = await UploadCore(meta, file, SubmissionType.Image, ct);
        if (errorResult is not null) return errorResult;

        // Schedule thumbnail generation (mock: set placeholder immediately)
        var thumbnailBlobName = $"thumbnails/{submission!.Id}_thumb.jpg";
        submission.ThumbnailBlobReference = thumbnailBlobName;
        submission.Status = SubmissionStatus.Processing;
        await _evidencePort.SubmitAsync(submission, ct); // Update with thumbnail ref

        // In production: dispatch to Worker Service for real thumbnail generation
        // await _rabbitPublisher.PublishAsync("evidence-process-thumbnail", new { submission.Id, submission.BlobReference });

        // Broadcast to response group via SignalR
        await BroadcastEvidenceSubmitted(submission, ct);

        // Audit trail entry
        await LogEvidenceAudit(submission, ct);

        _logger.LogInformation(
            "Photo uploaded: Id={Id}, RequestId={RequestId}, Size={Size}, ThumbnailRef={Thumb}",
            submission.Id, requestId, submission.FileSizeBytes, thumbnailBlobName);

        return Accepted(BuildSubmissionResponse(submission));
    }

    // ─────────────────────────────────────────────────────────────
    // Typed Upload: Video
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Upload a video for an incident. Extracts a keyframe thumbnail.
    /// Client sends: form field "metadata" (JSON) + form file "file" (video binary).
    /// </summary>
    [HttpPost("{requestId}/video")]
    [RequestSizeLimit(209_715_200)] // 200 MB max for video
    public async Task<IActionResult> UploadVideo(
        string requestId,
        [FromForm] string metadata,
        IFormFile file,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Video file is required" });

        if (!file.ContentType.StartsWith("video/"))
            return BadRequest(new { error = "File must be a video (video/*)" });

        var meta = DeserializeMeta(metadata, requestId);
        if (meta is null)
            return BadRequest(new { error = "Invalid metadata JSON" });

        var (submission, errorResult) = await UploadCore(meta, file, SubmissionType.Video, ct);
        if (errorResult is not null) return errorResult;

        // Schedule keyframe extraction (mock: set placeholder)
        var keyframeBlobName = $"thumbnails/{submission!.Id}_keyframe.jpg";
        submission.ThumbnailBlobReference = keyframeBlobName;
        submission.Status = SubmissionStatus.Processing;
        await _evidencePort.SubmitAsync(submission, ct);

        // In production: dispatch to Worker Service for FFmpeg keyframe extraction
        // await _rabbitPublisher.PublishAsync("evidence-process-video", new { submission.Id, submission.BlobReference });

        await BroadcastEvidenceSubmitted(submission, ct);
        await LogEvidenceAudit(submission, ct);

        _logger.LogInformation(
            "Video uploaded: Id={Id}, RequestId={RequestId}, Size={Size}",
            submission.Id, requestId, submission.FileSizeBytes);

        return Accepted(BuildSubmissionResponse(submission));
    }

    // ─────────────────────────────────────────────────────────────
    // Typed Upload: Audio
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Upload audio for an incident. Queues transcription via speech-to-text.
    /// Client sends: form field "metadata" (JSON) + form file "file" (audio binary).
    /// </summary>
    [HttpPost("{requestId}/audio")]
    [RequestSizeLimit(104_857_600)] // 100 MB max for audio
    public async Task<IActionResult> UploadAudio(
        string requestId,
        [FromForm] string metadata,
        IFormFile file,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "Audio file is required" });

        if (!file.ContentType.StartsWith("audio/"))
            return BadRequest(new { error = "File must be audio (audio/*)" });

        var meta = DeserializeMeta(metadata, requestId);
        if (meta is null)
            return BadRequest(new { error = "Invalid metadata JSON" });

        var (submission, errorResult) = await UploadCore(meta, file, SubmissionType.Audio, ct);
        if (errorResult is not null) return errorResult;

        // Queue for transcription (mock: placeholder, prod: Azure Speech / OpenAI Whisper)
        submission!.Status = SubmissionStatus.Processing;
        await _evidencePort.SubmitAsync(submission, ct);

        // In production: dispatch to Worker Service for transcription
        // await _rabbitPublisher.PublishAsync("evidence-transcribe-audio", new { submission.Id, submission.BlobReference });

        await BroadcastEvidenceSubmitted(submission, ct);
        await LogEvidenceAudit(submission, ct);

        _logger.LogInformation(
            "Audio uploaded: Id={Id}, RequestId={RequestId}, Size={Size}, MimeType={Mime}",
            submission.Id, requestId, submission.FileSizeBytes, file.ContentType);

        return Accepted(BuildSubmissionResponse(submission));
    }

    // ─────────────────────────────────────────────────────────────
    // Typed Upload: Sitrep (JSON text report)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Submit a situation report (sitrep) for an incident.
    /// JSON body — no file upload. The text report is the content.
    /// </summary>
    [HttpPost("{requestId}/sitrep")]
    public async Task<IActionResult> SubmitSitrep(
        string requestId,
        [FromBody] SitrepRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
            return BadRequest(new { error = "UserId is required" });
        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { error = "Sitrep text is required" });

        var textBytes = System.Text.Encoding.UTF8.GetBytes(request.Text);
        var contentHash = Convert.ToHexStringLower(SHA256.HashData(textBytes));

        var submission = new EvidenceSubmission
        {
            Id = Guid.NewGuid().ToString(),
            RequestId = requestId,
            UserId = request.UserId,
            SubmitterId = request.SubmitterId ?? request.UserId,
            Phase = request.Phase,
            SubmissionType = SubmissionType.Text,
            Title = request.Title ?? "Situation Report",
            Description = request.Text,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            ContentHash = contentHash,
            MimeType = "text/plain",
            FileSizeBytes = textBytes.Length,
            Status = SubmissionStatus.Available, // Text needs no processing
            SubmittedAt = DateTime.UtcNow,
            DeviceId = request.DeviceId,
            AppVersion = request.AppVersion
        };

        var result = await _evidencePort.SubmitAsync(submission, ct);
        if (!result.Success)
            return StatusCode(500, new { error = result.ErrorMessage });

        await BroadcastEvidenceSubmitted(submission, ct);
        await LogEvidenceAudit(submission, ct);

        _logger.LogInformation("Sitrep submitted: Id={Id}, RequestId={RequestId}", submission.Id, requestId);

        return Accepted(BuildSubmissionResponse(submission));
    }

    // ─────────────────────────────────────────────────────────────
    // Generic Upload (multipart: file + metadata) — preserved from original
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Multipart upload: file binary + metadata JSON.
    /// Client sends: form field "metadata" (JSON) + form file "file" (binary).
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(104_857_600)] // 100 MB max
    public async Task<IActionResult> Upload(
        [FromForm] string metadata,
        IFormFile file,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "File is required" });

        EvidenceUploadMetadata? meta;
        try
        {
            meta = JsonSerializer.Deserialize<EvidenceUploadMetadata>(metadata);
        }
        catch
        {
            return BadRequest(new { error = "Invalid metadata JSON" });
        }

        if (meta is null || string.IsNullOrWhiteSpace(meta.UserId))
            return BadRequest(new { error = "UserId is required in metadata" });

        // Hash the content for tamper detection
        string contentHash;
        using (var hashStream = file.OpenReadStream())
        {
            var hashBytes = await SHA256.HashDataAsync(hashStream, ct);
            contentHash = Convert.ToHexStringLower(hashBytes);
        }

        // Upload to blob storage
        var blobName = $"{meta.RequestId ?? "pre"}/{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
        using var uploadStream = file.OpenReadStream();
        var uploadResult = await _blobStorage.UploadAsync("evidence", blobName, uploadStream, file.ContentType, ct);
        if (!uploadResult.Success)
            return StatusCode(500, new { error = $"Blob upload failed: {uploadResult.ErrorMessage}" });

        // Create submission record
        var submission = new EvidenceSubmission
        {
            Id = Guid.NewGuid().ToString(),
            RequestId = meta.RequestId,
            UserId = meta.UserId,
            SubmitterId = meta.SubmitterId ?? meta.UserId,
            Phase = meta.Phase,
            SubmissionType = MapMimeToType(file.ContentType),
            Title = meta.Title,
            Description = meta.Description,
            Latitude = meta.Latitude,
            Longitude = meta.Longitude,
            Accuracy = meta.Accuracy,
            ContentHash = contentHash,
            MimeType = file.ContentType,
            FileSizeBytes = file.Length,
            BlobReference = uploadResult.Data,
            Status = SubmissionStatus.Processing,
            SubmittedAt = DateTime.UtcNow,
            DeviceId = meta.DeviceId,
            DeviceModel = meta.DeviceModel,
            AppVersion = meta.AppVersion
        };

        var result = await _evidencePort.SubmitAsync(submission, ct);
        if (!result.Success)
            return StatusCode(500, new { error = result.ErrorMessage });

        // Broadcast and audit
        await BroadcastEvidenceSubmitted(submission, ct);
        await LogEvidenceAudit(submission, ct);

        _logger.LogInformation(
            "Evidence uploaded: Id={Id}, Type={Type}, Phase={Phase}, RequestId={RequestId}, Size={Size}",
            submission.Id, submission.SubmissionType, submission.Phase, submission.RequestId, submission.FileSizeBytes);

        return Accepted(BuildSubmissionResponse(submission));
    }

    // ─────────────────────────────────────────────────────────────
    // Text-only submission (sitreps, notes)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Submit text-only evidence (sitreps, quick notes, incident narratives).
    /// No file upload — the text IS the content.
    /// </summary>
    [HttpPost("text")]
    public async Task<IActionResult> SubmitText(
        [FromBody] TextSubmissionRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
            return BadRequest(new { error = "UserId is required" });
        if (string.IsNullOrWhiteSpace(request.Text))
            return BadRequest(new { error = "Text is required" });

        var textBytes = System.Text.Encoding.UTF8.GetBytes(request.Text);
        var contentHash = Convert.ToHexStringLower(SHA256.HashData(textBytes));

        var submission = new EvidenceSubmission
        {
            Id = Guid.NewGuid().ToString(),
            RequestId = request.RequestId,
            UserId = request.UserId,
            SubmitterId = request.SubmitterId ?? request.UserId,
            Phase = request.Phase,
            SubmissionType = SubmissionType.Text,
            Title = request.Title,
            Description = request.Text,
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            ContentHash = contentHash,
            MimeType = "text/plain",
            FileSizeBytes = textBytes.Length,
            Status = SubmissionStatus.Available, // Text needs no processing
            SubmittedAt = DateTime.UtcNow,
            DeviceId = request.DeviceId,
            AppVersion = request.AppVersion
        };

        var result = await _evidencePort.SubmitAsync(submission, ct);
        if (!result.Success)
            return StatusCode(500, new { error = result.ErrorMessage });

        await BroadcastEvidenceSubmitted(submission, ct);
        await LogEvidenceAudit(submission, ct);

        _logger.LogInformation("Text evidence submitted: Id={Id}, Phase={Phase}", submission.Id, submission.Phase);

        return Accepted(BuildSubmissionResponse(submission));
    }

    // ─────────────────────────────────────────────────────────────
    // Query: Get all evidence for a request
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// GET /api/evidence/{requestId} — list all evidence for an incident.
    /// This is the primary query endpoint for mobile clients and the dashboard.
    /// Returns evidence ordered by submission time (newest first).
    /// </summary>
    [HttpGet("request/{requestId}")]
    public async Task<IActionResult> GetByRequestId(string requestId, CancellationToken ct)
    {
        var result = await _evidencePort.GetByRequestIdAsync(requestId, ct);
        return Ok(result.Data ?? new List<EvidenceSubmission>());
    }

    /// <summary>Get a single submission by its ID.</summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        var result = await _evidencePort.GetByIdAsync(id, ct);
        if (!result.Success)
            return NotFound(new { error = result.ErrorMessage });
        return Ok(result.Data);
    }

    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetByUserId(string userId, [FromQuery] SubmissionPhase? phase, CancellationToken ct)
    {
        var result = await _evidencePort.GetByUserIdAsync(userId, phase, ct);
        return Ok(result.Data ?? new List<EvidenceSubmission>());
    }

    // ─────────────────────────────────────────────────────────────
    // Download (signed URL redirect)
    // ─────────────────────────────────────────────────────────────

    [HttpGet("{id}/download")]
    public async Task<IActionResult> Download(string id, CancellationToken ct)
    {
        var result = await _evidencePort.GetByIdAsync(id, ct);
        if (!result.Success || result.Data?.BlobReference is null)
            return NotFound(new { error = "Submission not found or has no blob" });

        var url = await _blobStorage.GetDownloadUrlAsync("evidence", result.Data.BlobReference, TimeSpan.FromMinutes(15), ct);
        return Redirect(url);
    }

    // ─────────────────────────────────────────────────────────────
    // Soft delete
    // ─────────────────────────────────────────────────────────────

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        var result = await _evidencePort.UpdateStatusAsync(id, SubmissionStatus.Archived, ct);
        if (!result.Success)
            return NotFound(new { error = result.ErrorMessage });

        await _auditTrail.AppendAsync(new AuditEntry
        {
            UserId = "system",
            Action = AuditAction.EvidenceDeleted,
            EntityType = "EvidenceSubmission",
            EntityId = id,
            SourceSystem = "Dashboard.Api",
            SourceComponent = "EvidenceController",
            Severity = AuditSeverity.Notice,
            DataClassification = DataClassification.HighlyConfidential,
            Outcome = AuditOutcome.Success,
            Reason = "Soft delete (archived)"
        }, ct);

        return Ok(new { id, status = "Archived" });
    }

    // ─────────────────────────────────────────────────────────────
    // Offline sync
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Sync an offline-captured submission. Client sends the full submission
    /// with IsOfflineSubmission=true and OfflineCapturedAt set to the real capture time.
    /// Server verifies the content hash matches the uploaded blob.
    /// </summary>
    [HttpPost("{id}/offline-sync")]
    public async Task<IActionResult> OfflineSync(
        string id,
        [FromBody] OfflineSyncRequest request,
        CancellationToken ct)
    {
        var existing = await _evidencePort.GetByIdAsync(id, ct);
        if (!existing.Success || existing.Data is null)
            return NotFound(new { error = $"Submission '{id}' not found" });

        var sub = existing.Data;

        // Verify content hash if provided
        if (!string.IsNullOrEmpty(request.ContentHash) && sub.ContentHash != request.ContentHash)
        {
            _logger.LogWarning("Offline sync hash mismatch for {Id}: expected={Expected}, got={Got}",
                id, sub.ContentHash, request.ContentHash);

            await _auditTrail.AppendAsync(new AuditEntry
            {
                UserId = sub.UserId,
                Action = AuditAction.EvidenceIntegrityFailed,
                EntityType = "EvidenceSubmission",
                EntityId = id,
                SourceSystem = "Dashboard.Api",
                SourceComponent = "EvidenceController",
                Severity = AuditSeverity.Critical,
                DataClassification = DataClassification.HighlyConfidential,
                Outcome = AuditOutcome.Failure,
                Reason = $"Offline sync hash mismatch: stored={sub.ContentHash}, provided={request.ContentHash}"
            }, ct);

            return Conflict(new { error = "Content hash mismatch - possible tampering or corruption" });
        }

        sub.IsOfflineSubmission = true;
        sub.OfflineCapturedAt = request.OfflineCapturedAt;

        var updateResult = await _evidencePort.SubmitAsync(sub, ct);
        if (!updateResult.Success)
            return StatusCode(500, new { error = updateResult.ErrorMessage });

        _logger.LogInformation("Offline evidence synced: Id={Id}, CapturedAt={CapturedAt}", id, request.OfflineCapturedAt);

        return Ok(new
        {
            id,
            IsOfflineSubmission = true,
            request.OfflineCapturedAt,
            Status = sub.Status.ToString()
        });
    }

    // ─────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Core upload logic shared by all typed endpoints.
    /// Hashes content, uploads to blob storage, creates the EvidenceSubmission record.
    /// </summary>
    private async Task<(EvidenceSubmission? Submission, IActionResult? Error)> UploadCore(
        EvidenceUploadMetadata meta,
        IFormFile file,
        SubmissionType type,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(meta.UserId))
            return (null, BadRequest(new { error = "UserId is required in metadata" }));

        // Hash the content for tamper detection (chain of custody)
        string contentHash;
        using (var hashStream = file.OpenReadStream())
        {
            var hashBytes = await SHA256.HashDataAsync(hashStream, ct);
            contentHash = Convert.ToHexStringLower(hashBytes);
        }

        // Upload to blob storage
        var blobName = $"{meta.RequestId ?? "pre"}/{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
        using var uploadStream = file.OpenReadStream();
        var uploadResult = await _blobStorage.UploadAsync("evidence", blobName, uploadStream, file.ContentType, ct);
        if (!uploadResult.Success)
            return (null, StatusCode(500, new { error = $"Blob upload failed: {uploadResult.ErrorMessage}" }));

        var submission = new EvidenceSubmission
        {
            Id = Guid.NewGuid().ToString(),
            RequestId = meta.RequestId,
            UserId = meta.UserId,
            SubmitterId = meta.SubmitterId ?? meta.UserId,
            Phase = meta.Phase,
            SubmissionType = type,
            Title = meta.Title,
            Description = meta.Description,
            Latitude = meta.Latitude,
            Longitude = meta.Longitude,
            Accuracy = meta.Accuracy,
            ContentHash = contentHash,
            MimeType = file.ContentType,
            FileSizeBytes = file.Length,
            BlobReference = uploadResult.Data,
            Status = SubmissionStatus.Processing,
            SubmittedAt = DateTime.UtcNow,
            DeviceId = meta.DeviceId,
            DeviceModel = meta.DeviceModel,
            AppVersion = meta.AppVersion
        };

        var result = await _evidencePort.SubmitAsync(submission, ct);
        if (!result.Success)
            return (null, StatusCode(500, new { error = result.ErrorMessage }));

        return (submission, null);
    }

    /// <summary>
    /// Broadcast evidence submission to the incident's response group via SignalR.
    /// All connected responders and dashboard clients see the new evidence immediately.
    /// </summary>
    private async Task BroadcastEvidenceSubmitted(EvidenceSubmission submission, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(submission.RequestId))
            return; // Pre-incident evidence has no response group

        try
        {
            await _hubContext.Clients.Group($"response-{submission.RequestId}").SendAsync(
                "EvidenceSubmitted",
                new
                {
                    SubmissionId = submission.Id,
                    submission.RequestId,
                    Phase = submission.Phase.ToString(),
                    Type = submission.SubmissionType.ToString(),
                    ThumbnailUrl = submission.ThumbnailBlobReference,
                    submission.Title,
                    submission.SubmitterId,
                    submission.FileSizeBytes,
                    submission.MimeType,
                    Timestamp = DateTime.UtcNow
                }, ct);
        }
        catch (Exception ex)
        {
            // SignalR broadcast failure should not block the upload response
            _logger.LogWarning(ex, "Failed to broadcast evidence submission {Id} via SignalR", submission.Id);
        }
    }

    /// <summary>
    /// Log evidence submission to the audit trail for chain-of-custody tracking.
    /// </summary>
    private async Task LogEvidenceAudit(EvidenceSubmission submission, CancellationToken ct)
    {
        try
        {
            await _auditTrail.AppendAsync(new AuditEntry
            {
                UserId = submission.SubmitterId,
                Action = AuditAction.EvidenceSubmitted,
                EntityType = "EvidenceSubmission",
                EntityId = submission.Id,
                CorrelationId = submission.RequestId ?? submission.Id,
                SourceSystem = "Dashboard.Api",
                SourceComponent = "EvidenceController",
                Severity = AuditSeverity.Notice,
                DataClassification = DataClassification.HighlyConfidential,
                Outcome = AuditOutcome.Success,
                Reason = $"{submission.SubmissionType} evidence submitted, phase={submission.Phase}",
                NewValue = JsonSerializer.Serialize(new
                {
                    submission.Id,
                    submission.RequestId,
                    submission.SubmissionType,
                    submission.Phase,
                    submission.MimeType,
                    submission.FileSizeBytes,
                    submission.ContentHash,
                    submission.IsOfflineSubmission
                })
            }, ct);
        }
        catch (Exception ex)
        {
            // Audit failure should not block the upload response
            _logger.LogWarning(ex, "Failed to log audit entry for evidence {Id}", submission.Id);
        }
    }

    /// <summary>Deserialize metadata JSON, defaulting RequestId from the route param.</summary>
    private static EvidenceUploadMetadata? DeserializeMeta(string metadataJson, string requestId)
    {
        try
        {
            var meta = JsonSerializer.Deserialize<EvidenceUploadMetadata>(metadataJson);
            if (meta is null) return null;

            // Override/default the RequestId from the route parameter
            return meta with { RequestId = requestId };
        }
        catch
        {
            return null;
        }
    }

    private static object BuildSubmissionResponse(EvidenceSubmission s) => new
    {
        s.Id,
        s.RequestId,
        Phase = s.Phase.ToString(),
        Type = s.SubmissionType.ToString(),
        Status = s.Status.ToString(),
        s.ContentHash,
        s.BlobReference,
        s.ThumbnailBlobReference,
        s.FileSizeBytes,
        s.MimeType,
        s.SubmittedAt
    };

    private static SubmissionType MapMimeToType(string? mimeType) => mimeType?.Split('/')[0] switch
    {
        "image" => SubmissionType.Image,
        "audio" => SubmissionType.Audio,
        "video" => SubmissionType.Video,
        "text" => SubmissionType.Text,
        _ => SubmissionType.Document
    };
}

// ─────────────────────────────────────────────────────────────
// Request DTOs
// ─────────────────────────────────────────────────────────────

public record EvidenceUploadMetadata(
    string UserId,
    string? SubmitterId,
    string? RequestId,
    SubmissionPhase Phase,
    string? Title = null,
    string? Description = null,
    double Latitude = 0,
    double Longitude = 0,
    double? Accuracy = null,
    string? DeviceId = null,
    string? DeviceModel = null,
    string? AppVersion = null
);

public record TextSubmissionRequest(
    string UserId,
    string? SubmitterId,
    string? RequestId,
    SubmissionPhase Phase,
    string Text,
    string? Title = null,
    double Latitude = 0,
    double Longitude = 0,
    string? DeviceId = null,
    string? AppVersion = null
);

public record SitrepRequest(
    string UserId,
    string? SubmitterId,
    SubmissionPhase Phase,
    string Text,
    string? Title = null,
    double Latitude = 0,
    double Longitude = 0,
    string? DeviceId = null,
    string? AppVersion = null
);

public record OfflineSyncRequest(
    string? ContentHash,
    DateTime OfflineCapturedAt
);
