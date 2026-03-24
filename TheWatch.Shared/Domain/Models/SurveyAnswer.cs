// SurveyAnswer — a single answer within a SurveyResponse.
// Flexible: text, selected options, numeric scale, or an attached evidence submission
// (e.g., a PhotoCapture question links to an EvidenceSubmission).
//
// Example:
//   new SurveyAnswer
//   {
//       QuestionId = "q-1",
//       AnswerText = "Yes",
//       ScaleValue = null,
//       EvidenceSubmissionId = null
//   }

namespace TheWatch.Shared.Domain.Models;

public class SurveyAnswer
{
    public string QuestionId { get; set; } = string.Empty;

    /// <summary>Free-text answer or YesNo string ("Yes"/"No").</summary>
    public string? AnswerText { get; set; }

    /// <summary>For MultipleChoice: which options were selected.</summary>
    public List<string>? SelectedOptions { get; set; }

    /// <summary>For Scale questions: the numeric value selected.</summary>
    public int? ScaleValue { get; set; }

    /// <summary>
    /// If this answer includes captured evidence (photo/audio from a capture question),
    /// this links to the EvidenceSubmission that was created.
    /// </summary>
    public string? EvidenceSubmissionId { get; set; }
}
