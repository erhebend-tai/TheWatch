// SurveyResponse — a user's completed response to a SurveyTemplate.
// Links back to both the template (which questions were asked) and optionally
// to a RequestId (which incident this response relates to).
//
// Example:
//   var response = new SurveyResponse
//   {
//       TemplateId = "tpl-postincident-v1",
//       RequestId = "req-123",
//       UserId = "user-456",
//       Answers = new List<SurveyAnswer> { ... },
//       Phase = SubmissionPhase.PostIncident
//   };

using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Domain.Models;

public class SurveyResponse
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TemplateId { get; set; } = string.Empty;

    /// <summary>Linked incident. Null for pre-incident surveys (e.g., registration).</summary>
    public string? RequestId { get; set; }

    public string UserId { get; set; } = string.Empty;

    public List<SurveyAnswer> Answers { get; set; } = new();

    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
    public SubmissionPhase Phase { get; set; }

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}
