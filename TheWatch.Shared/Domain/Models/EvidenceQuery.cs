// EvidenceQuery — flexible query object for filtering evidence submissions.
// Used by IEvidencePort.QueryAsync to support dashboard search/filter UI.
//
// Example:
//   var query = new EvidenceQuery
//   {
//       RequestId = "req-123",
//       Phase = SubmissionPhase.Active,
//       Types = new() { SubmissionType.Image, SubmissionType.Video },
//       MinSubmittedAt = DateTime.UtcNow.AddHours(-2)
//   };

using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Domain.Models;

public class EvidenceQuery
{
    public string? RequestId { get; set; }
    public string? UserId { get; set; }
    public string? SubmitterId { get; set; }
    public SubmissionPhase? Phase { get; set; }
    public List<SubmissionType>? Types { get; set; }
    public SubmissionStatus? Status { get; set; }
    public DateTime? MinSubmittedAt { get; set; }
    public DateTime? MaxSubmittedAt { get; set; }
    public bool? IsOfflineSubmission { get; set; }
    public int Skip { get; set; } = 0;
    public int Take { get; set; } = 50;
}
