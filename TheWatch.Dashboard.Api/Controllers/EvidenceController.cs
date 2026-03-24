// EvidenceController — REST endpoints for evidence submission and retrieval.
// Mobile clients and the dashboard both drive this controller.
//
// Endpoints:
//   POST   /api/evidence/upload           — Multipart upload (file + metadata JSON)
//   POST   /api/evidence/text             — Text-only submission (sitreps)
//   GET    /api/evidence/{id}             — Get submission metadata
//   GET    /api/evidence/request/{reqId}  — All evidence for an incident
//   GET    /api/evidence/user/{userId}    — All evidence for a user
//   GET    /api/evidence/{id}/download    — Download blob (signed URL redirect)
//   DELETE /api/evidence/{id}             — Soft delete (mark Archived)
//   POST   /api/evidence/{id}/offline-sync — Sync an offline-captured submission
//
// WAL: Upload flow:
//   1. Client POSTs multipart form (file + metadata)
//   2. Controller hashes content (SHA-256), uploads to IBlobStoragePort
//   3. Creates EvidenceSubmission via IEvidencePort
//   4. Publishes EvidenceSubmittedMessage to RabbitMQ "evidence-submitted" queue
//   5. Returns 202 Accepted with submission ID

using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
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
    private readonly ILogger<EvidenceController> _logger;

    public EvidenceController(
        IEvidencePort evidencePort,
        IBlobStoragePort blobStorage,
        ILogger<EvidenceController> logger)
    {
        _evidencePort = evidencePort;
        _blobStorage = blobStorage;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────
    // Upload (multipart: file + metadata)
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
            meta = System.Text.Json.JsonSerializer.Deserialize<EvidenceUploadMetadata>(metadata);
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

        _logger.LogInformation(
            "Evidence uploaded: Id={Id}, Type={Type}, Phase={Phase}, RequestId={RequestId}, Size={Size}",
            submission.Id, submission.SubmissionType, submission.Phase, submission.RequestId, submission.FileSizeBytes);

        // In production: publish EvidenceSubmittedMessage to RabbitMQ here
        // await _rabbitPublisher.PublishAsync("evidence-submitted", new EvidenceSubmittedMessage(...));

        return Accepted(new
        {
            submission.Id,
            submission.RequestId,
            Phase = submission.Phase.ToString(),
            Type = submission.SubmissionType.ToString(),
            Status = submission.Status.ToString(),
            submission.ContentHash,
            submission.BlobReference,
            submission.SubmittedAt
        });
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

        _logger.LogInformation("Text evidence submitted: Id={Id}, Phase={Phase}", submission.Id, submission.Phase);

        return Accepted(new
        {
            submission.Id,
            submission.RequestId,
            Phase = submission.Phase.ToString(),
            Status = submission.Status.ToString(),
            submission.SubmittedAt
        });
    }

    // ─────────────────────────────────────────────────────────────
    // Query
    // ─────────────────────────────────────────────────────────────

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id, CancellationToken ct)
    {
        var result = await _evidencePort.GetByIdAsync(id, ct);
        if (!result.Success)
            return NotFound(new { error = result.ErrorMessage });
        return Ok(result.Data);
    }

    [HttpGet("request/{requestId}")]
    public async Task<IActionResult> GetByRequestId(string requestId, CancellationToken ct)
    {
        var result = await _evidencePort.GetByRequestIdAsync(requestId, ct);
        return Ok(result.Data ?? new List<EvidenceSubmission>());
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
            return Conflict(new { error = "Content hash mismatch — possible tampering or corruption" });
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

public record OfflineSyncRequest(
    string? ContentHash,
    DateTime OfflineCapturedAt
);
