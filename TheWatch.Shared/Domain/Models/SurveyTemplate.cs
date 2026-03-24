// SurveyTemplate — reusable survey forms dispatched at specific lifecycle phases.
// Templates are versioned so historical responses always reference the questions
// that were asked at the time. Templates define when they should auto-fire via TriggerCondition.
//
// Example:
//   var template = new SurveyTemplate
//   {
//       Id = "tpl-postincident-v1",
//       Title = "Post-Incident Wellbeing Check",
//       Phase = SubmissionPhase.PostIncident,
//       TriggerCondition = SurveyTrigger.OnResolution,
//       IsRequired = true,
//       TimeoutMinutes = 1440, // 24 hours to complete
//       Questions = new List<SurveyQuestion> { ... }
//   };

using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Domain.Models;

public class SurveyTemplate
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Which incident phase this survey is dispatched during.</summary>
    public SubmissionPhase Phase { get; set; }

    /// <summary>What event triggers automatic dispatch of this survey.</summary>
    public SurveyTrigger TriggerCondition { get; set; }

    /// <summary>Ordered list of questions in this survey.</summary>
    public List<SurveyQuestion> Questions { get; set; } = new();

    /// <summary>Whether the user must complete this survey (vs. optional).</summary>
    public bool IsRequired { get; set; }

    /// <summary>Minutes allowed to complete. Null = no timeout.</summary>
    public int? TimeoutMinutes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Template version — increment when questions change.</summary>
    public int Version { get; set; } = 1;
}
