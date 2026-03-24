// IResponseCoordinationPort — port interfaces for emergency response coordination.
//
// This is the CORE life-safety coordination system. Modular by design:
// a check-in request for 5-10 neighbors is fundamentally different from
// an evacuation during a natural disaster. The type system enforces this
// by making ResponseScope, EscalationPolicy, and DispatchStrategy
// first-class concepts rather than string flags.
//
// Architecture:
//   User triggers SOS → ResponseRequest created (with scope + escalation policy)
//   → IResponseDispatchPort fans out via RabbitMQ to eligible responders
//   → IResponseTrackingPort tracks acknowledgments, ETAs, arrivals
//   → IEscalationPort auto-escalates if thresholds aren't met
//
// Every interface here is a port. Mock adapters are first-class.
// Real adapters dispatch via RabbitMQ (Hangfire schedules escalation jobs).

namespace TheWatch.Shared.Domain.Ports;

// ═══════════════════════════════════════════════════════════════
// Response Scope — what KIND of response is this?
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// The category of emergency response. Each scope has different
/// dispatch radius, responder count, escalation rules, and UI treatment.
/// </summary>
public enum ResponseScope
{
    /// <summary>
    /// Check-in: "I feel unsafe, can someone nearby check on me?"
    /// Small radius (500m–1km), 5–10 responders, no 911 escalation by default.
    /// </summary>
    CheckIn,

    /// <summary>
    /// Neighborhood emergency: assault, medical event, fire.
    /// Medium radius (1–5km), 10–25 responders, auto-escalates to 911 after timer.
    /// </summary>
    Neighborhood,

    /// <summary>
    /// Community-wide: active threat, large-scale incident.
    /// Large radius (5–15km), 50+ responders, immediate 911 notification.
    /// </summary>
    Community,

    /// <summary>
    /// Natural disaster evacuation: wildfire, flood, earthquake, tornado.
    /// Unlimited radius, all opted-in users in zone, government agencies notified.
    /// </summary>
    Evacuation,

    /// <summary>
    /// Silent duress: user is being coerced/threatened.
    /// Small radius, pre-selected trusted contacts ONLY, no visible notifications on user's device.
    /// </summary>
    SilentDuress,

    /// <summary>
    /// Custom: user-defined scope with manual parameters.
    /// </summary>
    Custom
}

/// <summary>
/// How aggressively to escalate if response thresholds aren't met.
/// </summary>
public enum EscalationPolicy
{
    /// <summary>No auto-escalation. User must manually escalate.</summary>
    Manual,

    /// <summary>Escalate to next tier after configured timeout (default: 2 min).</summary>
    TimedEscalation,

    /// <summary>Escalate to 911 immediately in parallel with volunteer dispatch.</summary>
    Immediate911,

    /// <summary>Escalate to 911 only if fewer than N responders acknowledge within timeout.</summary>
    Conditional911,

    /// <summary>Full cascade: volunteers → 911 → emergency contacts → public broadcast.</summary>
    FullCascade
}

/// <summary>
/// How responders are selected and notified.
/// </summary>
public enum DispatchStrategy
{
    /// <summary>Nearest N opted-in responders by distance.</summary>
    NearestN,

    /// <summary>All opted-in responders within radius.</summary>
    RadiusBroadcast,

    /// <summary>Only user's pre-selected trusted contacts.</summary>
    TrustedContactsOnly,

    /// <summary>Certified responders first (EMT, nurse, etc.), then volunteers.</summary>
    CertifiedFirst,

    /// <summary>Government emergency broadcast (evacuation only).</summary>
    EmergencyBroadcast
}

// ═══════════════════════════════════════════════════════════════
// Core Records
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// A request for emergency response coordination.
/// Created when any SOS trigger fires (phrase, tap, manual button).
/// </summary>
public record ResponseRequest(
    string RequestId,
    string UserId,
    string? DeviceId,
    ResponseScope Scope,
    EscalationPolicy Escalation,
    DispatchStrategy Strategy,

    // Location
    double Latitude,
    double Longitude,
    double? AccuracyMeters,

    // Scope parameters
    double RadiusMeters,
    int DesiredResponderCount,
    TimeSpan EscalationTimeout,

    // Metadata
    string? Description,
    string? TriggerSource,    // "PHRASE", "QUICK_TAP", "MANUAL_BUTTON", "WEARABLE", "FALL_DETECTION"
    float? TriggerConfidence,
    DateTime CreatedAt,
    ResponseStatus Status = ResponseStatus.Pending
);

public enum ResponseStatus
{
    Pending,        // Created, not yet dispatched
    Dispatching,    // Notifications being sent
    Active,         // At least one responder acknowledged
    Escalated,      // Escalation triggered (911, broader radius, etc.)
    Resolved,       // User confirmed safe or responder arrived
    Cancelled,      // User cancelled
    Expired         // No response within max timeout
}

/// <summary>
/// A responder's acknowledgment of a response request.
/// </summary>
public record ResponderAcknowledgment(
    string AckId,
    string RequestId,
    string ResponderId,
    string ResponderName,
    string? ResponderRole,  // "EMT", "NURSE", "VOLUNTEER", "NEIGHBOR", "TRUSTED_CONTACT"
    double ResponderLatitude,
    double ResponderLongitude,
    double DistanceMeters,
    TimeSpan? EstimatedArrival,
    AckStatus Status,
    DateTime AcknowledgedAt
);

public enum AckStatus
{
    Acknowledged,   // "I see it, I'm available"
    EnRoute,        // "I'm on my way"
    OnScene,        // "I've arrived"
    Declined,       // "I can't help right now"
    TimedOut        // No response from this responder
}

/// <summary>
/// Escalation event — logged when auto-escalation fires.
/// </summary>
public record EscalationEvent(
    string EventId,
    string RequestId,
    EscalationPolicy PolicyTriggered,
    string Reason,          // "Timeout: 0/5 responders acknowledged after 120s"
    ResponseScope? NewScope,
    double? NewRadiusMeters,
    DateTime TriggeredAt
);

// ═══════════════════════════════════════════════════════════════
// Scope Presets — factory for common configurations
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Factory for creating ResponseRequests with sensible defaults per scope.
/// This keeps the mobile clients simple — they just say "CheckIn" or "Evacuation"
/// and the preset fills in radius, responder count, escalation policy, etc.
/// </summary>
public static class ResponseScopePresets
{
    public static (double RadiusMeters, int DesiredResponders, EscalationPolicy Escalation,
                    DispatchStrategy Strategy, TimeSpan EscalationTimeout) GetDefaults(ResponseScope scope) => scope switch
    {
        ResponseScope.CheckIn => (
            RadiusMeters: 1000,
            DesiredResponders: 8,
            Escalation: EscalationPolicy.Manual,
            Strategy: DispatchStrategy.NearestN,
            EscalationTimeout: TimeSpan.FromMinutes(5)),

        ResponseScope.Neighborhood => (
            RadiusMeters: 3000,
            DesiredResponders: 15,
            Escalation: EscalationPolicy.TimedEscalation,
            Strategy: DispatchStrategy.CertifiedFirst,
            EscalationTimeout: TimeSpan.FromMinutes(2)),

        ResponseScope.Community => (
            RadiusMeters: 10000,
            DesiredResponders: 50,
            Escalation: EscalationPolicy.Immediate911,
            Strategy: DispatchStrategy.RadiusBroadcast,
            EscalationTimeout: TimeSpan.FromMinutes(1)),

        ResponseScope.Evacuation => (
            RadiusMeters: 50000,
            DesiredResponders: int.MaxValue,
            Escalation: EscalationPolicy.FullCascade,
            Strategy: DispatchStrategy.EmergencyBroadcast,
            EscalationTimeout: TimeSpan.Zero),

        ResponseScope.SilentDuress => (
            RadiusMeters: 500,
            DesiredResponders: 3,
            Escalation: EscalationPolicy.Conditional911,
            Strategy: DispatchStrategy.TrustedContactsOnly,
            EscalationTimeout: TimeSpan.FromMinutes(3)),

        ResponseScope.Custom => (
            RadiusMeters: 2000,
            DesiredResponders: 10,
            Escalation: EscalationPolicy.TimedEscalation,
            Strategy: DispatchStrategy.NearestN,
            EscalationTimeout: TimeSpan.FromMinutes(2)),

        _ => throw new ArgumentOutOfRangeException(nameof(scope))
    };
}

// ═══════════════════════════════════════════════════════════════
// Port Interfaces
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Port for creating and managing response requests.
/// This is the entry point — called by the API when any SOS trigger fires.
/// </summary>
public interface IResponseRequestPort
{
    /// <summary>Create a new response request and begin coordination.</summary>
    Task<ResponseRequest> CreateRequestAsync(ResponseRequest request, CancellationToken ct = default);

    /// <summary>Get a response request by ID.</summary>
    Task<ResponseRequest?> GetRequestAsync(string requestId, CancellationToken ct = default);

    /// <summary>Get all active requests for a user.</summary>
    Task<IReadOnlyList<ResponseRequest>> GetActiveRequestsAsync(string userId, CancellationToken ct = default);

    /// <summary>Cancel an active request (user confirmed safe).</summary>
    Task<ResponseRequest> CancelRequestAsync(string requestId, string reason, CancellationToken ct = default);

    /// <summary>Resolve a request (responder arrived, situation handled).</summary>
    Task<ResponseRequest> ResolveRequestAsync(string requestId, string resolvedBy, CancellationToken ct = default);

    /// <summary>Update request status.</summary>
    Task<ResponseRequest> UpdateStatusAsync(string requestId, ResponseStatus newStatus, CancellationToken ct = default);
}

/// <summary>
/// Port for dispatching notifications to eligible responders.
/// Implementation publishes messages to RabbitMQ for fan-out delivery.
/// </summary>
public interface IResponseDispatchPort
{
    /// <summary>
    /// Dispatch a response request to eligible responders.
    /// Publishes to RabbitMQ queue for async processing.
    /// Returns the number of responders notified.
    /// </summary>
    Task<int> DispatchAsync(ResponseRequest request, CancellationToken ct = default);

    /// <summary>
    /// Re-dispatch with expanded parameters (e.g., larger radius after escalation).
    /// </summary>
    Task<int> RedispatchAsync(ResponseRequest request, double newRadiusMeters,
        int newDesiredCount, CancellationToken ct = default);
}

/// <summary>
/// Port for tracking responder acknowledgments and arrivals.
/// </summary>
public interface IResponseTrackingPort
{
    /// <summary>Record a responder's acknowledgment.</summary>
    Task<ResponderAcknowledgment> AcknowledgeAsync(ResponderAcknowledgment ack, CancellationToken ct = default);

    /// <summary>Update a responder's status (e.g., EnRoute → OnScene).</summary>
    Task<ResponderAcknowledgment> UpdateAckStatusAsync(string ackId, AckStatus newStatus, CancellationToken ct = default);

    /// <summary>Get all acknowledgments for a request.</summary>
    Task<IReadOnlyList<ResponderAcknowledgment>> GetAcknowledgmentsAsync(string requestId, CancellationToken ct = default);

    /// <summary>Count acknowledged responders for a request.</summary>
    Task<int> GetAcknowledgmentCountAsync(string requestId, CancellationToken ct = default);
}

/// <summary>
/// Port for auto-escalation logic.
/// Implementation schedules Hangfire jobs for timed escalation checks.
/// </summary>
public interface IEscalationPort
{
    /// <summary>
    /// Schedule escalation monitoring for a request.
    /// Creates a Hangfire delayed job that checks responder count at timeout.
    /// </summary>
    Task ScheduleEscalationAsync(ResponseRequest request, CancellationToken ct = default);

    /// <summary>
    /// Execute escalation check. Called by Hangfire when the timer fires.
    /// If responder threshold isn't met, escalates per the request's policy.
    /// </summary>
    Task<EscalationEvent?> CheckAndEscalateAsync(string requestId, CancellationToken ct = default);

    /// <summary>Cancel scheduled escalation (request was resolved or cancelled).</summary>
    Task CancelEscalationAsync(string requestId, CancellationToken ct = default);

    /// <summary>Get escalation history for a request.</summary>
    Task<IReadOnlyList<EscalationEvent>> GetEscalationHistoryAsync(string requestId, CancellationToken ct = default);
}

// ═══════════════════════════════════════════════════════════════
// Participation (Opt-In / Opt-Out)
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// User's participation preferences — controls WHAT they're willing to respond to
/// and WHEN they're available. Granular per response scope.
/// </summary>
public record ParticipationPreferences(
    string UserId,

    // Global toggle
    bool IsResponderEnabled,

    // Per-scope opt-in (user can opt into some scopes but not others)
    bool OptedInCheckIn,
    bool OptedInNeighborhood,
    bool OptedInCommunity,
    bool OptedInEvacuation,

    // Availability
    bool IsCurrentlyAvailable,
    TimeOnly? AvailableFrom,      // null = always
    TimeOnly? AvailableTo,        // null = always
    DayOfWeek[]? AvailableDays,   // null = every day

    // Capabilities
    string[]? Certifications,     // "EMT", "NURSE", "CPR", "FIRST_AID"
    double MaxResponseRadiusMeters,
    bool WillingToBeFirstOnScene,
    bool HasVehicle,              // Whether responder has a vehicle — used to exclude on-foot
                                  // responders from dispatch when incident exceeds walking distance

    // Quiet hours — don't notify during these times even if opted in
    TimeOnly? QuietHoursStart,
    TimeOnly? QuietHoursEnd,

    DateTime LastUpdated
);

/// <summary>
/// Port for managing user participation preferences.
/// </summary>
public interface IParticipationPort
{
    /// <summary>Get a user's participation preferences.</summary>
    Task<ParticipationPreferences?> GetPreferencesAsync(string userId, CancellationToken ct = default);

    /// <summary>Update a user's participation preferences.</summary>
    Task<ParticipationPreferences> UpdatePreferencesAsync(ParticipationPreferences prefs, CancellationToken ct = default);

    /// <summary>
    /// Find eligible responders near a location for a given scope.
    /// Filters by: opt-in for scope, currently available, within radius, not in quiet hours.
    /// </summary>
    Task<IReadOnlyList<EligibleResponder>> FindEligibleRespondersAsync(
        double latitude, double longitude,
        double radiusMeters, ResponseScope scope,
        int maxResults = 50,
        CancellationToken ct = default);

    /// <summary>Quick opt-out toggle (e.g., "I'm busy for the next 2 hours").</summary>
    Task SetAvailabilityAsync(string userId, bool isAvailable, TimeSpan? duration = null, CancellationToken ct = default);
}

/// <summary>
/// A responder who is eligible to receive a dispatch for a specific request.
/// </summary>
public record EligibleResponder(
    string UserId,
    string Name,
    double Latitude,
    double Longitude,
    double DistanceMeters,
    string[]? Certifications,
    bool IsFirstOnSceneWilling,
    bool HasVehicle,
    DateTime LastActiveAt
);

// ═══════════════════════════════════════════════════════════════
// Navigation / Directions
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Maximum distance in meters a responder without a vehicle should be asked to respond.
/// Beyond this threshold, only responders with vehicles are dispatched.
/// 1600m ≈ 1 mile — roughly a 20-minute brisk walk.
///
/// Example: If an incident is 3km away and a volunteer has no car, they are excluded
///          from dispatch. A volunteer 800m away on foot is still eligible.
///
/// Configurable per deployment via INavigationPort.MaxWalkingDistanceMeters.
/// </summary>
public static class DispatchDistancePolicy
{
    /// <summary>Default max walking distance: 1600m (≈1 mile / ~20 min walk).</summary>
    public const double DefaultMaxWalkingDistanceMeters = 1600;
}

/// <summary>
/// Directions payload returned to a responder after acknowledgment.
/// Contains platform-specific deep links so the responder's device can
/// launch its native maps app with turn-by-turn navigation to the incident.
///
/// Example deep links generated:
///   Google Maps: "https://www.google.com/maps/dir/?api=1&amp;origin=30.27,-97.74&amp;destination=30.28,-97.73&amp;travelmode=driving"
///   Apple Maps:  "https://maps.apple.com/?saddr=30.27,-97.74&amp;daddr=30.28,-97.73&amp;dirflg=d"
///   Waze:        "https://waze.com/ul?ll=30.28,-97.73&amp;navigate=yes"
/// </summary>
public record NavigationDirections(
    string RequestId,
    string ResponderId,

    // Incident location the responder is navigating TO
    double IncidentLatitude,
    double IncidentLongitude,

    // Responder's current location (origin)
    double ResponderLatitude,
    double ResponderLongitude,

    // Distance and travel mode
    double DistanceMeters,
    string TravelMode,           // "driving" or "walking"

    // Platform deep links — mobile clients pick the appropriate one
    string GoogleMapsUrl,
    string AppleMapsUrl,
    string WazeUrl,

    // Estimated travel time (null if routing service unavailable)
    TimeSpan? EstimatedTravelTime
);

/// <summary>
/// Port for generating navigation directions to an incident.
/// Adapters may use Google Directions API, Mapbox, OSRM, or simple deep-link generation.
/// </summary>
public interface INavigationPort
{
    /// <summary>
    /// Maximum distance (meters) a responder without a vehicle should travel on foot.
    /// Responders beyond this distance who lack a vehicle are excluded from dispatch.
    /// </summary>
    double MaxWalkingDistanceMeters { get; }

    /// <summary>
    /// Generate navigation directions from a responder's location to the incident.
    /// Returns deep links for Google Maps, Apple Maps, and Waze plus estimated travel time.
    /// </summary>
    Task<NavigationDirections> GetDirectionsAsync(
        string requestId,
        string responderId,
        double responderLatitude,
        double responderLongitude,
        double incidentLatitude,
        double incidentLongitude,
        bool hasVehicle,
        CancellationToken ct = default);

    /// <summary>
    /// Check whether a responder should be excluded from dispatch based on
    /// distance and vehicle availability.
    /// Returns true if the responder is too far to walk and has no vehicle.
    /// </summary>
    bool ShouldExcludeFromDispatch(double distanceMeters, bool hasVehicle);
}
