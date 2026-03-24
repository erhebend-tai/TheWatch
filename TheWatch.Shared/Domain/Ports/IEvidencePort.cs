// IEvidencePort — domain port for evidence metadata CRUD + query.
// NO database SDK imports allowed in this file. Adapters implement this.
// Backed by the relational data layer (SQL Server, PostgreSQL, CosmosDB, etc.).
//
// Separate from IBlobStoragePort: this stores the METADATA about evidence,
// while IBlobStoragePort stores the actual binary content.
//
// Example:
//   var result = await evidencePort.SubmitAsync(submission, ct);
//   var allForIncident = await evidencePort.GetByRequestIdAsync("req-123", ct);
//   var isValid = await evidencePort.VerifyIntegrityAsync("sub-789", ct);

using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Domain.Ports;

public interface IEvidencePort
{
    /// <summary>Create a new evidence submission record.</summary>
    Task<StorageResult<EvidenceSubmission>> SubmitAsync(EvidenceSubmission submission, CancellationToken ct = default);

    /// <summary>Retrieve a single submission by ID.</summary>
    Task<StorageResult<EvidenceSubmission>> GetByIdAsync(string submissionId, CancellationToken ct = default);

    /// <summary>Get all evidence for a specific incident (ResponseRequest).</summary>
    Task<StorageResult<List<EvidenceSubmission>>> GetByRequestIdAsync(string requestId, CancellationToken ct = default);

    /// <summary>Get all evidence for a user, optionally filtered by phase.</summary>
    Task<StorageResult<List<EvidenceSubmission>>> GetByUserIdAsync(string userId, SubmissionPhase? phase = null, CancellationToken ct = default);

    /// <summary>Flexible query with filtering, pagination.</summary>
    Task<StorageResult<List<EvidenceSubmission>>> QueryAsync(EvidenceQuery query, CancellationToken ct = default);

    /// <summary>Update the status of a submission (e.g., Processing → Available).</summary>
    Task<StorageResult<bool>> UpdateStatusAsync(string submissionId, SubmissionStatus status, CancellationToken ct = default);

    /// <summary>Attach processing results (thumbnail path, metadata, transcription) to a submission.</summary>
    Task<StorageResult<bool>> AttachProcessingResultAsync(string submissionId, EvidenceProcessingResult result, CancellationToken ct = default);

    /// <summary>
    /// Re-hash the blob and compare with stored ContentHash for tamper detection.
    /// Returns true if the stored hash matches the actual blob content.
    /// </summary>
    Task<bool> VerifyIntegrityAsync(string submissionId, CancellationToken ct = default);
}
