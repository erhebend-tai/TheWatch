// =============================================================================
// MockEvidencePort — In-memory mock adapter for IEvidencePort.
// =============================================================================
// Fully functional in-memory implementation for Development/Staging.
// Stores evidence submissions in a ConcurrentDictionary keyed by submission ID.
// Supports all CRUD operations, query, status updates, and integrity verification.
//
// Example:
//   var port = new MockEvidencePort(logger);
//   var result = await port.SubmitAsync(submission);
//   var all = await port.GetByRequestIdAsync("req-123");
//   var verified = await port.VerifyIntegrityAsync("sub-456");
//
// NOTE: Integrity verification always returns true in mock (no real blob to hash).
// The native adapter (CosmosDb/SqlServer) re-hashes blob content from IBlobStoragePort.
// =============================================================================

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Enums;

namespace TheWatch.Adapters.Mock;

public class MockEvidencePort : IEvidencePort
{
    private readonly ILogger<MockEvidencePort> _logger;
    private readonly ConcurrentDictionary<string, EvidenceSubmission> _submissions = new();

    public MockEvidencePort(ILogger<MockEvidencePort> logger)
    {
        _logger = logger;
    }

    /// <summary>Create or upsert an evidence submission record.</summary>
    public Task<StorageResult<EvidenceSubmission>> SubmitAsync(EvidenceSubmission submission, CancellationToken ct)
    {
        _submissions[submission.Id] = submission;

        _logger.LogInformation(
            "[MOCK EVIDENCE] Submitted: Id={Id}, Type={Type}, Phase={Phase}, RequestId={RequestId}",
            submission.Id, submission.SubmissionType, submission.Phase, submission.RequestId);

        return Task.FromResult(StorageResult<EvidenceSubmission>.Ok(submission));
    }

    /// <summary>Retrieve a single submission by ID.</summary>
    public Task<StorageResult<EvidenceSubmission>> GetByIdAsync(string submissionId, CancellationToken ct)
    {
        if (_submissions.TryGetValue(submissionId, out var submission))
            return Task.FromResult(StorageResult<EvidenceSubmission>.Ok(submission));

        return Task.FromResult(StorageResult<EvidenceSubmission>.Fail($"Submission '{submissionId}' not found"));
    }

    /// <summary>Get all evidence for a specific incident (ResponseRequest).</summary>
    public Task<StorageResult<List<EvidenceSubmission>>> GetByRequestIdAsync(string requestId, CancellationToken ct)
    {
        var results = _submissions.Values
            .Where(s => s.RequestId == requestId)
            .OrderByDescending(s => s.SubmittedAt)
            .ToList();

        _logger.LogDebug("[MOCK EVIDENCE] GetByRequestId({RequestId}): {Count} results", requestId, results.Count);
        return Task.FromResult(StorageResult<List<EvidenceSubmission>>.Ok(results));
    }

    /// <summary>Get all evidence for a user, optionally filtered by phase.</summary>
    public Task<StorageResult<List<EvidenceSubmission>>> GetByUserIdAsync(string userId, SubmissionPhase? phase, CancellationToken ct)
    {
        var query = _submissions.Values.Where(s => s.UserId == userId);
        if (phase.HasValue)
            query = query.Where(s => s.Phase == phase.Value);

        var results = query.OrderByDescending(s => s.SubmittedAt).ToList();
        return Task.FromResult(StorageResult<List<EvidenceSubmission>>.Ok(results));
    }

    /// <summary>Flexible query with filtering, pagination.</summary>
    public Task<StorageResult<List<EvidenceSubmission>>> QueryAsync(EvidenceQuery query, CancellationToken ct)
    {
        IEnumerable<EvidenceSubmission> q = _submissions.Values;

        if (!string.IsNullOrEmpty(query.RequestId))
            q = q.Where(s => s.RequestId == query.RequestId);
        if (!string.IsNullOrEmpty(query.UserId))
            q = q.Where(s => s.UserId == query.UserId);
        if (!string.IsNullOrEmpty(query.SubmitterId))
            q = q.Where(s => s.SubmitterId == query.SubmitterId);
        if (query.Phase.HasValue)
            q = q.Where(s => s.Phase == query.Phase.Value);
        if (query.Types is { Count: > 0 })
            q = q.Where(s => query.Types.Contains(s.SubmissionType));
        if (query.Status.HasValue)
            q = q.Where(s => s.Status == query.Status.Value);
        if (query.MinSubmittedAt.HasValue)
            q = q.Where(s => s.SubmittedAt >= query.MinSubmittedAt.Value);
        if (query.MaxSubmittedAt.HasValue)
            q = q.Where(s => s.SubmittedAt <= query.MaxSubmittedAt.Value);
        if (query.IsOfflineSubmission.HasValue)
            q = q.Where(s => s.IsOfflineSubmission == query.IsOfflineSubmission.Value);

        var results = q
            .OrderByDescending(s => s.SubmittedAt)
            .Skip(query.Skip)
            .Take(query.Take)
            .ToList();

        return Task.FromResult(StorageResult<List<EvidenceSubmission>>.Ok(results));
    }

    /// <summary>Update the status of a submission.</summary>
    public Task<StorageResult<bool>> UpdateStatusAsync(string submissionId, SubmissionStatus status, CancellationToken ct)
    {
        if (!_submissions.TryGetValue(submissionId, out var submission))
            return Task.FromResult(StorageResult<bool>.Fail($"Submission '{submissionId}' not found"));

        submission.Status = status;
        if (status == SubmissionStatus.Available)
            submission.ProcessedAt = DateTime.UtcNow;

        _logger.LogInformation("[MOCK EVIDENCE] Status updated: {Id} -> {Status}", submissionId, status);
        return Task.FromResult(StorageResult<bool>.Ok(true));
    }

    /// <summary>Attach processing results (thumbnail, transcription, metadata).</summary>
    public Task<StorageResult<bool>> AttachProcessingResultAsync(string submissionId, EvidenceProcessingResult result, CancellationToken ct)
    {
        if (!_submissions.TryGetValue(submissionId, out var submission))
            return Task.FromResult(StorageResult<bool>.Fail($"Submission '{submissionId}' not found"));

        if (result.ThumbnailGenerated)
            submission.ThumbnailBlobReference = $"thumbnails/{submissionId}_thumb.jpg";

        submission.Status = SubmissionStatus.Available;
        submission.ProcessedAt = result.ProcessedAt;

        _logger.LogInformation(
            "[MOCK EVIDENCE] Processing result attached: {Id}, Thumbnail={Thumb}, Transcription={HasText}",
            submissionId, result.ThumbnailGenerated,
            !string.IsNullOrEmpty(result.TranscriptionText));

        return Task.FromResult(StorageResult<bool>.Ok(true));
    }

    /// <summary>
    /// Integrity verification — always returns true in mock.
    /// Native adapter re-hashes blob content from IBlobStoragePort.
    /// </summary>
    public Task<bool> VerifyIntegrityAsync(string submissionId, CancellationToken ct)
    {
        var exists = _submissions.ContainsKey(submissionId);
        _logger.LogDebug("[MOCK EVIDENCE] VerifyIntegrity({Id}): {Result}", submissionId, exists);
        return Task.FromResult(exists);
    }

    // ── Test Helpers ──────────────────────────────────────────────

    /// <summary>Get all submissions (for test assertions).</summary>
    public IReadOnlyDictionary<string, EvidenceSubmission> GetAllSubmissions() => _submissions;
}
