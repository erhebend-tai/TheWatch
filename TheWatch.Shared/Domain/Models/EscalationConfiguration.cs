// EscalationConfiguration — configurable escalation stage timeouts and behavior.
//
// Bound from appsettings.json section "Escalation". Controls how fast the system
// escalates when responders don't acknowledge, and whether to auto-dial 911.
//
// Escalation stages (in order):
//   Stage 1: Initial Dispatch (0 min)       — notify nearby volunteers
//   Stage 2: Widen Scope    (5 min default)  — if < 2 responders acked, expand radius 2x
//   Stage 3: Emergency Contacts (10 min)     — notify user's emergency contacts list
//   Stage 4: First Responders   (15 min)     — call 911/emergency services if user opted in (default: yes)
//
// Example appsettings.json:
//   "Escalation": {
//     "InitialDispatchDelaySeconds": 0,
//     "WidenScopeDelaySeconds": 300,
//     "EmergencyContactsDelaySeconds": 600,
//     "FirstRespondersDelaySeconds": 900,
//     "MinRespondersBeforeWiden": 2,
//     "RadiusMultiplierOnWiden": 2.0,
//     "MaxRadiusExpansionMultiplier": 4.0,
//     "AutoDial911": true,
//     "SweepIntervalSeconds": 30
//   }
//
// Example usage:
//   var config = configuration.GetSection("Escalation").Get<EscalationConfiguration>()
//                ?? new EscalationConfiguration();
//   if (elapsed >= config.WidenScopeDelay && ackCount < config.MinRespondersBeforeWiden)
//       // expand radius by config.RadiusMultiplierOnWiden

namespace TheWatch.Shared.Domain.Models;

/// <summary>
/// Configurable escalation chain parameters. Bound from "Escalation" appsettings section.
/// All durations are expressed as seconds in JSON for readability, converted to TimeSpan via properties.
/// </summary>
public class EscalationConfiguration
{
    public const string SectionName = "Escalation";

    // ── Stage Timeouts (seconds in config, TimeSpan in code) ─────────

    /// <summary>
    /// Delay before initial volunteer dispatch. Normally 0 (immediate).
    /// Only non-zero for SilentDuress where a brief delay prevents accidental triggers.
    /// </summary>
    public int InitialDispatchDelaySeconds { get; set; } = 0;

    /// <summary>
    /// Delay before widening scope (expanding search radius).
    /// Default: 300 seconds (5 minutes).
    /// If fewer than MinRespondersBeforeWiden have acknowledged by this time,
    /// the system doubles the search radius and re-dispatches.
    /// </summary>
    public int WidenScopeDelaySeconds { get; set; } = 300;

    /// <summary>
    /// Delay before notifying the user's emergency contacts.
    /// Default: 600 seconds (10 minutes).
    /// Emergency contacts receive a push notification + SMS with the user's
    /// location and incident details.
    /// </summary>
    public int EmergencyContactsDelaySeconds { get; set; } = 600;

    /// <summary>
    /// Delay before calling 911 / first responders.
    /// Default: 900 seconds (15 minutes).
    /// Only fires if AutoDial911 is true AND the user opted in at signup.
    /// </summary>
    public int FirstRespondersDelaySeconds { get; set; } = 900;

    // ── Computed TimeSpan Properties ─────────────────────────────────

    /// <summary>Stage 1 delay as TimeSpan.</summary>
    public TimeSpan InitialDispatchDelay => TimeSpan.FromSeconds(InitialDispatchDelaySeconds);

    /// <summary>Stage 2 delay as TimeSpan.</summary>
    public TimeSpan WidenScopeDelay => TimeSpan.FromSeconds(WidenScopeDelaySeconds);

    /// <summary>Stage 3 delay as TimeSpan.</summary>
    public TimeSpan EmergencyContactsDelay => TimeSpan.FromSeconds(EmergencyContactsDelaySeconds);

    /// <summary>Stage 4 delay as TimeSpan.</summary>
    public TimeSpan FirstRespondersDelay => TimeSpan.FromSeconds(FirstRespondersDelaySeconds);

    // ── Thresholds ──────────────────────────────────────────────────

    /// <summary>
    /// Minimum number of acknowledged responders required to prevent scope widening.
    /// If fewer than this many responders have acknowledged by WidenScopeDelay,
    /// the system expands the search radius. Default: 2.
    /// </summary>
    public int MinRespondersBeforeWiden { get; set; } = 2;

    /// <summary>
    /// Multiplier applied to the current radius when widening scope.
    /// Default: 2.0 (double the radius). Applied at each widen stage.
    /// </summary>
    public double RadiusMultiplierOnWiden { get; set; } = 2.0;

    /// <summary>
    /// Maximum total radius expansion multiplier relative to the original radius.
    /// Prevents unbounded expansion. Default: 4.0 (4x original radius).
    /// Example: CheckIn starts at 1000m, max expansion = 4000m.
    /// </summary>
    public double MaxRadiusExpansionMultiplier { get; set; } = 4.0;

    // ── 911 Behavior ────────────────────────────────────────────────

    /// <summary>
    /// Whether the system should automatically call 911 at Stage 4.
    /// Default: true. Even when true, the user's consent preferences are checked.
    /// Set to false to disable auto-911 system-wide (overrides user preference).
    /// </summary>
    public bool AutoDial911 { get; set; } = true;

    // ── Sweep Configuration ─────────────────────────────────────────

    /// <summary>
    /// How often the escalation sweep runs (seconds). Default: 30.
    /// The sweep checks all active requests for escalation conditions.
    /// Lower = more responsive but higher CPU. 30s is a good balance.
    /// </summary>
    public int SweepIntervalSeconds { get; set; } = 30;

    /// <summary>Sweep interval as TimeSpan.</summary>
    public TimeSpan SweepInterval => TimeSpan.FromSeconds(SweepIntervalSeconds);

    // ── Helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns the escalation stage that should be active given the elapsed time.
    /// Stage 0 = initial dispatch, 1 = widen scope, 2 = emergency contacts, 3 = first responders.
    /// </summary>
    public EscalationStage GetCurrentStage(TimeSpan elapsed)
    {
        if (elapsed >= FirstRespondersDelay) return EscalationStage.FirstResponders;
        if (elapsed >= EmergencyContactsDelay) return EscalationStage.EmergencyContacts;
        if (elapsed >= WidenScopeDelay) return EscalationStage.WidenScope;
        return EscalationStage.InitialDispatch;
    }

    /// <summary>
    /// Returns the maximum expanded radius given the original radius.
    /// </summary>
    public double GetMaxExpandedRadius(double originalRadiusMeters)
        => originalRadiusMeters * MaxRadiusExpansionMultiplier;

    /// <summary>
    /// Returns the expanded radius for a single widen step, capped at max.
    /// </summary>
    public double GetWidenedRadius(double currentRadiusMeters, double originalRadiusMeters)
    {
        var expanded = currentRadiusMeters * RadiusMultiplierOnWiden;
        var max = GetMaxExpandedRadius(originalRadiusMeters);
        return Math.Min(expanded, max);
    }
}

/// <summary>
/// Named escalation stages for readability and audit logging.
/// </summary>
public enum EscalationStage
{
    /// <summary>Stage 0: Initial volunteer dispatch (immediate).</summary>
    InitialDispatch = 0,

    /// <summary>Stage 1: Widen search radius and re-dispatch (default: 5 min).</summary>
    WidenScope = 1,

    /// <summary>Stage 2: Notify user's emergency contacts (default: 10 min).</summary>
    EmergencyContacts = 2,

    /// <summary>Stage 3: Call 911 / first responders (default: 15 min).</summary>
    FirstResponders = 3
}
