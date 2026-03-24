// SubmissionPhase — lifecycle stage at which evidence or a survey is captured.
// Pre-incident = preparation/documentation, Active = during SOS, Post = follow-up.
// Example: var phase = SubmissionPhase.Active; // user submitting photo during an SOS

namespace TheWatch.Shared.Enums;

public enum SubmissionPhase
{
    /// <summary>Preparation: dwelling photos, hazard docs, household surveys.</summary>
    PreIncident = 0,

    /// <summary>During an active SOS: real-time photos, video, audio, sitreps.</summary>
    Active = 1,

    /// <summary>After resolution: follow-up surveys, damage docs, incident reports.</summary>
    PostIncident = 2
}
