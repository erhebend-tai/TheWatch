// SurveyTrigger — when a survey template should be automatically dispatched.
// The SurveyDispatchFunction checks these triggers on a timer to auto-send surveys.
// Example: SurveyTrigger.OnResolution → dispatch post-incident survey when SOS resolves.

namespace TheWatch.Shared.Enums;

public enum SurveyTrigger
{
    /// <summary>Dispatched when a new user completes registration.</summary>
    OnRegistration = 0,

    /// <summary>Dispatched when an SOS is triggered (quick status check).</summary>
    OnSOSTrigger = 1,

    /// <summary>Dispatched when an incident is resolved.</summary>
    OnResolution = 2,

    /// <summary>Dispatched on a recurring schedule (e.g., weekly safety check).</summary>
    OnSchedule = 3,

    /// <summary>Manually dispatched by a coordinator or admin.</summary>
    Manual = 4,

    /// <summary>Dispatched when an incident escalates to a higher scope.</summary>
    OnEscalation = 5
}
