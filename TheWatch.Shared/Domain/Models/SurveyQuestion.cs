// SurveyQuestion — individual question within a SurveyTemplate.
// Supports conditional display: a question can be shown only if a prior question
// was answered a certain way (e.g., show "Describe injury" only if "Are you injured?" = Yes).
//
// Example:
//   new SurveyQuestion
//   {
//       Id = "q-1",
//       TemplateId = "tpl-postincident-v1",
//       Text = "Are you safe now?",
//       QuestionType = QuestionType.YesNo,
//       IsRequired = true,
//       DisplayOrder = 1
//   }

using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Domain.Models;

public class SurveyQuestion
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TemplateId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;

    /// <summary>Determines the UI control rendered on mobile.</summary>
    public QuestionType QuestionType { get; set; }

    /// <summary>For MultipleChoice: the available options. Null for other types.</summary>
    public List<string>? Options { get; set; }

    public bool IsRequired { get; set; }

    /// <summary>Order in which questions appear (1-based).</summary>
    public int DisplayOrder { get; set; }

    // ── Conditional logic ──────────────────────────
    /// <summary>If set, this question only displays when the referenced question is answered.</summary>
    public string? ConditionalOnQuestionId { get; set; }
    /// <summary>The answer value that must match for this question to display.</summary>
    public string? ConditionalOnAnswer { get; set; }
}
