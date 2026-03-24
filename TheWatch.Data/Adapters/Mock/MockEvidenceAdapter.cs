// MockEvidenceAdapter — ConcurrentDictionary-backed IEvidencePort for dev/testing.
// Supports all query operations with LINQ filtering over in-memory data.
// VerifyIntegrityAsync always returns true in mock mode (no real blob to hash).
//
// Example:
//   services.AddSingleton<IEvidencePort, MockEvidenceAdapter>();
//   var result = await evidencePort.SubmitAsync(submission, ct);

using System.Collections.Concurrent;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Enums;

namespace TheWatch.Data.Adapters.Mock;

public class MockEvidenceAdapter : IEvidencePort
{
    private readonly ConcurrentDictionary<string, EvidenceSubmission> _submissions = new();
    private readonly ConcurrentDictionary<string, EvidenceProcessingResult> _processingResults = new();

    public Task<StorageResult<EvidenceSubmission>> SubmitAsync(EvidenceSubmission submission, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(submission.Id))
            submission.Id = Guid.NewGuid().ToString();

        _submissions[submission.Id] = submission;
        return Task.FromResult(StorageResult<EvidenceSubmission>.Ok(submission));
    }

    public Task<StorageResult<EvidenceSubmission>> GetByIdAsync(string submissionId, CancellationToken ct = default)
    {
        if (_submissions.TryGetValue(submissionId, out var sub))
            return Task.FromResult(StorageResult<EvidenceSubmission>.Ok(sub));
        return Task.FromResult(StorageResult<EvidenceSubmission>.Fail($"Submission '{submissionId}' not found"));
    }

    public Task<StorageResult<List<EvidenceSubmission>>> GetByRequestIdAsync(string requestId, CancellationToken ct = default)
    {
        var results = _submissions.Values
            .Where(s => s.RequestId == requestId)
            .OrderByDescending(s => s.SubmittedAt)
            .ToList();
        return Task.FromResult(StorageResult<List<EvidenceSubmission>>.Ok(results));
    }

    public Task<StorageResult<List<EvidenceSubmission>>> GetByUserIdAsync(string userId, SubmissionPhase? phase = null, CancellationToken ct = default)
    {
        var results = _submissions.Values
            .Where(s => s.UserId == userId)
            .Where(s => phase is null || s.Phase == phase)
            .OrderByDescending(s => s.SubmittedAt)
            .ToList();
        return Task.FromResult(StorageResult<List<EvidenceSubmission>>.Ok(results));
    }

    public Task<StorageResult<List<EvidenceSubmission>>> QueryAsync(EvidenceQuery query, CancellationToken ct = default)
    {
        var q = _submissions.Values.AsEnumerable();

        if (query.RequestId is not null) q = q.Where(s => s.RequestId == query.RequestId);
        if (query.UserId is not null) q = q.Where(s => s.UserId == query.UserId);
        if (query.SubmitterId is not null) q = q.Where(s => s.SubmitterId == query.SubmitterId);
        if (query.Phase is not null) q = q.Where(s => s.Phase == query.Phase);
        if (query.Types is not null && query.Types.Count > 0) q = q.Where(s => query.Types.Contains(s.SubmissionType));
        if (query.Status is not null) q = q.Where(s => s.Status == query.Status);
        if (query.MinSubmittedAt is not null) q = q.Where(s => s.SubmittedAt >= query.MinSubmittedAt);
        if (query.MaxSubmittedAt is not null) q = q.Where(s => s.SubmittedAt <= query.MaxSubmittedAt);
        if (query.IsOfflineSubmission is not null) q = q.Where(s => s.IsOfflineSubmission == query.IsOfflineSubmission);

        var results = q
            .OrderByDescending(s => s.SubmittedAt)
            .Skip(query.Skip)
            .Take(query.Take)
            .ToList();

        return Task.FromResult(StorageResult<List<EvidenceSubmission>>.Ok(results));
    }

    public Task<StorageResult<bool>> UpdateStatusAsync(string submissionId, SubmissionStatus status, CancellationToken ct = default)
    {
        if (_submissions.TryGetValue(submissionId, out var sub))
        {
            sub.Status = status;
            if (status == SubmissionStatus.Available || status == SubmissionStatus.Rejected)
                sub.ProcessedAt = DateTime.UtcNow;
            return Task.FromResult(StorageResult<bool>.Ok(true));
        }
        return Task.FromResult(StorageResult<bool>.Fail($"Submission '{submissionId}' not found"));
    }

    public Task<StorageResult<bool>> AttachProcessingResultAsync(string submissionId, EvidenceProcessingResult result, CancellationToken ct = default)
    {
        if (_submissions.TryGetValue(submissionId, out var sub))
        {
            _processingResults[submissionId] = result;
            sub.ProcessedAt = result.ProcessedAt;
            if (result.ThumbnailGenerated)
                sub.ThumbnailBlobReference = $"thumbnails/{submissionId}_thumb.jpg";
            sub.Status = result.ModerationFlags.Count > 0 ? SubmissionStatus.Rejected : SubmissionStatus.Available;
            return Task.FromResult(StorageResult<bool>.Ok(true));
        }
        return Task.FromResult(StorageResult<bool>.Fail($"Submission '{submissionId}' not found"));
    }

    public Task<bool> VerifyIntegrityAsync(string submissionId, CancellationToken ct = default)
    {
        // Mock: always passes integrity check. Real adapter would re-hash the blob.
        return Task.FromResult(_submissions.ContainsKey(submissionId));
    }
}
