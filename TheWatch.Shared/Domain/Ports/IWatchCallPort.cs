// =============================================================================
// IWatchCallPort — port interfaces for live-video Watch Calls.
// =============================================================================
// A Watch Call is a structured, video-enabled community safety check. Users
// enroll as participants in their neighborhood; when a call is initiated (either
// from an SOS escalation, a guard report, or a manual request), enrolled
// participants nearby are connected via live WebRTC video.
//
// Core design principle: DE-ESCALATION THROUGH NEUTRAL OBSERVATION.
//
// The system pairs live video with an AI "scene narrator" that describes what
// it sees in plain, neutral, factual language — never identifying individuals
// by race, ethnicity, age, or other protected characteristics. The narrator
// "parrots back" the scene:
//
//   "I see two people standing near a parked vehicle. One person is holding
//    a flashlight. The vehicle's trunk is open. The street appears well-lit."
//
// This narration serves multiple purposes:
//   1. REMOVES BIAS — participants hear factual descriptions, not subjective
//      interpretations colored by fear or prejudice.
//   2. DE-ESCALATES — neutral language lowers emotional temperature.
//   3. CREATES A RECORD — narration is timestamped and stored as evidence
//      (text only, video is ephemeral unless the user opts into recording).
//   4. ACCESSIBILITY — participants who can't see the video clearly (night,
//      poor connection) still know what's happening.
//
// Enrollment & Mock Calls:
//   Users MUST complete mock watch calls before participating in live ones.
//   Mock calls place the user at the center of a simulated scenario in their
//   own neighborhood. This achieves two things:
//     a) TRAINING — users learn how calls work, what to expect, how to behave.
//     b) EMPATHY — by being the "subject" of a watch call, users experience
//        what it feels like to be observed, building empathy and reducing
//        the likelihood of aggressive or biased behavior during real calls.
//
//   Mock call scenarios include:
//     - "You're walking home late at night and a neighbor calls a watch"
//     - "You're working on your car in the driveway and someone doesn't recognize you"
//     - "You're a delivery driver and a camera flags you as unknown"
//     - "You're visiting a friend and waiting on the porch"
//
//   After each mock call, the user sees a debrief screen:
//     "Notice how the narrator described the scene without assumptions.
//      That's how Watch calls work — facts only, no judgment."
//
// Call lifecycle:
//   REQUESTED → CONNECTING → ACTIVE → NARRATING → RESOLVED / ESCALATED / EXPIRED
//
// Privacy:
//   - Video is PEER-TO-PEER (WebRTC). The server only handles signaling.
//   - Video is NOT recorded by default. Users can opt-in to recording, which
//     routes through a TURN server to Cloudflare Stream.
//   - The AI narrator receives periodic frame snapshots (1 fps), NOT the full
//     video stream, to minimize bandwidth and data exposure.
//   - Snapshots are processed in-memory and NOT stored unless attached to
//     an active incident as evidence.
//   - Participant identities are not revealed to each other during the call
//     (anonymized as "Watcher 1", "Watcher 2", etc.).
//
// WebRTC Signaling (via SignalR):
//   The DashboardHub handles WebRTC offer/answer/ICE candidate exchange.
//   Participants join a SignalR group "watchcall-{callId}" for signaling.
//   STUN servers: Google's public STUN (stun.l.google.com:19302).
//   TURN servers: Cloudflare TURN (configured per deployment) for NAT traversal.
//
// WAL: Video frames sent to the narrator are processed in-memory only.
//      No raw video is persisted unless the user explicitly enables recording
//      AND the call is linked to an active ResponseRequest.
// =============================================================================

using TheWatch.Shared.Domain.Models;

namespace TheWatch.Shared.Domain.Ports;

// ═══════════════════════════════════════════════════════════════
// Enrollment
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// A user's enrollment status for Watch Calls in their area.
/// Enrollment requires completing at least one mock call.
/// </summary>
public enum WatchCallEnrollmentStatus
{
    /// <summary>User has signed up but not yet completed a mock call.</summary>
    PendingTraining,

    /// <summary>User has completed at least one mock call and is eligible for live calls.</summary>
    Active,

    /// <summary>User has been temporarily suspended (e.g., behavior violation).</summary>
    Suspended,

    /// <summary>User voluntarily unenrolled.</summary>
    Unenrolled
}

/// <summary>
/// A user's Watch Call enrollment record. Tracks training completion,
/// participation history, and behavioral standing.
/// </summary>
public record WatchCallEnrollment(
    string EnrollmentId,
    string UserId,
    string DisplayAlias,          // Anonymized: "Watcher-7F3A" — NOT their real name

    WatchCallEnrollmentStatus Status,

    // Location (center of the user's watch radius)
    double Latitude,
    double Longitude,
    double WatchRadiusMeters,     // How far from home they're willing to participate (default: 2000m)

    // Training
    int MockCallsCompleted,       // Must be >= 1 to activate
    DateTime? LastMockCallAt,
    DateTime? TrainingCompletedAt,

    // Participation stats
    int LiveCallsParticipated,
    int CallsAsSubject,           // Times this user was the "subject" of a mock call
    double? AverageBehaviorScore, // 1.0-5.0, aggregated from post-call surveys (null if no calls yet)

    // Standing
    string? SuspensionReason,
    DateTime? SuspendedUntil,

    DateTime EnrolledAt,
    DateTime LastUpdated
);

// ═══════════════════════════════════════════════════════════════
// Mock Call Scenarios
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// A mock call training scenario. These are pre-built situations designed to
/// teach users how watch calls work by placing them as the subject.
/// </summary>
public record MockCallScenario(
    string ScenarioId,
    string Title,
    string Description,

    /// <summary>
    /// Narrative prompt shown to the trainee before the call starts.
    /// Example: "You're walking home from the bus stop at 11 PM. A neighbor
    /// sees someone they don't recognize and initiates a Watch Call."
    /// </summary>
    string SetupNarrative,

    /// <summary>
    /// What the AI narrator will say during the mock call.
    /// Pre-scripted for training scenarios (live calls use real-time vision).
    /// </summary>
    IReadOnlyList<TimestampedNarration> ScriptedNarrations,

    /// <summary>
    /// Debrief text shown after the mock call completes.
    /// Reinforces neutral observation principles.
    /// </summary>
    string DebriefText,

    /// <summary>
    /// Tags for scenario selection (e.g., "nighttime", "vehicle", "delivery", "visitor").
    /// </summary>
    IReadOnlyList<string> Tags,

    TimeSpan EstimatedDuration
);

/// <summary>
/// A single narration line with its timestamp offset from call start.
/// </summary>
public record TimestampedNarration(
    TimeSpan Offset,
    string NarrationText
);

// ═══════════════════════════════════════════════════════════════
// Watch Call
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Status of a Watch Call through its lifecycle.
/// </summary>
public enum WatchCallStatus
{
    /// <summary>Call requested, waiting for participants to connect.</summary>
    Requested,

    /// <summary>WebRTC signaling in progress, peers connecting.</summary>
    Connecting,

    /// <summary>At least two participants connected, video flowing.</summary>
    Active,

    /// <summary>AI narrator is actively describing the scene.</summary>
    Narrating,

    /// <summary>Call ended normally — situation assessed as safe.</summary>
    Resolved,

    /// <summary>Call escalated to a full SOS ResponseRequest.</summary>
    Escalated,

    /// <summary>No participants connected within timeout (default: 60s).</summary>
    Expired,

    /// <summary>This is a mock/training call, not a live incident.</summary>
    MockTraining
}

/// <summary>
/// A Watch Call session — live or mock.
///
/// Example (live):
///   Guard files report → escalates → Watch Call created with Scope=Neighborhood.
///   3 enrolled watchers within 2km are notified via push.
///   They connect via WebRTC. AI narrator describes the scene.
///   Watchers see video, hear narration. Situation de-escalates.
///   Call resolved. Narration transcript attached as evidence.
///
/// Example (mock):
///   New user enrolls. System creates a MockTraining call using a scenario
///   that places the user "at the center." Pre-scripted narration plays.
///   After debrief, user's enrollment status moves to Active.
/// </summary>
public record WatchCall(
    string CallId,
    WatchCallStatus Status,

    /// <summary>Whether this is a mock training call or a live call.</summary>
    bool IsMockCall,

    /// <summary>For mock calls: the scenario being used. Null for live calls.</summary>
    string? MockScenarioId,

    // Origin — how this call was initiated
    /// <summary>The user who initiated or triggered the call.</summary>
    string InitiatorUserId,

    /// <summary>If linked to a ResponseRequest (SOS), the request ID.</summary>
    string? LinkedRequestId,

    /// <summary>If escalated from a guard report, the report ID.</summary>
    string? LinkedGuardReportId,

    // Location
    double Latitude,
    double Longitude,
    double RadiusMeters,

    // Participants
    /// <summary>
    /// Connected participants (watchers). Anonymized during the call.
    /// Does NOT include the subject (the person being observed).
    /// </summary>
    IReadOnlyList<WatchCallParticipant> Participants,

    /// <summary>Maximum number of watchers for this call (default: 5).</summary>
    int MaxParticipants,

    // Narration
    /// <summary>Timestamped narration lines produced by the AI scene narrator.</summary>
    IReadOnlyList<TimestampedNarration> NarrationTranscript,

    // Recording
    /// <summary>Whether video recording is enabled (opt-in by initiator).</summary>
    bool RecordingEnabled,

    /// <summary>If recorded, the blob reference for the video.</summary>
    string? RecordingBlobReference,

    // Timestamps
    DateTime RequestedAt,
    DateTime? ConnectedAt,
    DateTime? ResolvedAt,
    DateTime? EscalatedAt,

    // Resolution
    /// <summary>How the call ended: "safe", "escalated", "timeout", "cancelled".</summary>
    string? Resolution,

    /// <summary>If escalated, the ResponseRequest ID created from this call.</summary>
    string? EscalatedRequestId
);

/// <summary>
/// A participant in a Watch Call. Identity is anonymized during the call.
/// </summary>
public record WatchCallParticipant(
    string UserId,
    string AnonymizedAlias,       // "Watcher 1", "Watcher 2", etc.
    string? PeerConnectionId,     // WebRTC peer connection identifier
    DateTime JoinedAt,
    DateTime? LeftAt,
    bool IsVideoEnabled,
    bool IsAudioEnabled
);

// ═══════════════════════════════════════════════════════════════
// WebRTC Signaling Records
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// WebRTC signaling message types routed through the SignalR hub.
/// </summary>
public enum SignalingMessageType
{
    /// <summary>SDP offer from a peer wanting to connect.</summary>
    Offer,

    /// <summary>SDP answer from a peer accepting the connection.</summary>
    Answer,

    /// <summary>ICE candidate for NAT traversal.</summary>
    IceCandidate,

    /// <summary>Peer is leaving the call.</summary>
    Disconnect
}

/// <summary>
/// A WebRTC signaling message exchanged between peers via the SignalR hub.
/// The server does NOT inspect or store the SDP/ICE content — it's an opaque relay.
/// </summary>
public record SignalingMessage(
    string CallId,
    string FromPeerId,
    string ToPeerId,
    SignalingMessageType Type,

    /// <summary>
    /// The SDP offer/answer or ICE candidate JSON. Opaque to the server.
    /// </summary>
    string Payload,

    DateTime Timestamp
);

// ═══════════════════════════════════════════════════════════════
// ICE Server Configuration
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// ICE server configuration provided to WebRTC peers for STUN/TURN.
/// </summary>
public record IceServerConfig(
    /// <summary>STUN/TURN server URLs.</summary>
    IReadOnlyList<string> Urls,

    /// <summary>Username for TURN authentication (null for STUN).</summary>
    string? Username,

    /// <summary>Credential for TURN authentication (null for STUN).</summary>
    string? Credential
);

// ═══════════════════════════════════════════════════════════════
// Port Interface
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Port for Watch Call enrollment, lifecycle management, and mock call training.
///
/// Adapters:
///   - MockWatchCallAdapter: in-memory with seeded enrollments and scenarios (dev)
///   - Production: Firestore-backed enrollment + call state, Cloudflare TURN,
///     Azure SignalR for signaling fan-out, Hangfire for call timeouts
/// </summary>
public interface IWatchCallPort
{
    // ── Enrollment ────────────────────────────────────────────────

    /// <summary>Enroll a user for Watch Calls in their area.</summary>
    Task<WatchCallEnrollment> EnrollAsync(
        WatchCallEnrollment enrollment, CancellationToken ct = default);

    /// <summary>Get a user's enrollment record.</summary>
    Task<WatchCallEnrollment?> GetEnrollmentAsync(
        string userId, CancellationToken ct = default);

    /// <summary>Update enrollment (radius, status, etc.).</summary>
    Task<WatchCallEnrollment> UpdateEnrollmentAsync(
        WatchCallEnrollment enrollment, CancellationToken ct = default);

    /// <summary>Unenroll a user from Watch Calls.</summary>
    Task<bool> UnenrollAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Find enrolled watchers near a location who are eligible for a live call.
    /// Filters: Active status, within radius, not suspended, training complete.
    /// </summary>
    Task<IReadOnlyList<WatchCallEnrollment>> FindNearbyWatchersAsync(
        double latitude, double longitude, double radiusMeters,
        int maxResults = 10, CancellationToken ct = default);

    // ── Mock Call Training ────────────────────────────────────────

    /// <summary>Get all available mock call scenarios.</summary>
    Task<IReadOnlyList<MockCallScenario>> GetMockScenariosAsync(
        CancellationToken ct = default);

    /// <summary>Get a specific mock call scenario.</summary>
    Task<MockCallScenario?> GetMockScenarioAsync(
        string scenarioId, CancellationToken ct = default);

    /// <summary>
    /// Start a mock call for a user. Creates a WatchCall with IsMockCall=true
    /// and Status=MockTraining. The client plays the scenario locally.
    /// </summary>
    Task<WatchCall> StartMockCallAsync(
        string userId, string scenarioId, CancellationToken ct = default);

    /// <summary>
    /// Complete a mock call. Updates the user's enrollment (increments
    /// MockCallsCompleted, activates enrollment if first mock call).
    /// </summary>
    Task<WatchCallEnrollment> CompleteMockCallAsync(
        string callId, CancellationToken ct = default);

    // ── Live Call Lifecycle ───────────────────────────────────────

    /// <summary>
    /// Create a new live Watch Call. Notifies nearby enrolled watchers.
    /// Returns the call with Status=Requested.
    /// </summary>
    Task<WatchCall> CreateCallAsync(
        WatchCall call, CancellationToken ct = default);

    /// <summary>Get a Watch Call by ID.</summary>
    Task<WatchCall?> GetCallAsync(
        string callId, CancellationToken ct = default);

    /// <summary>Get active calls near a location.</summary>
    Task<IReadOnlyList<WatchCall>> GetActiveCallsNearbyAsync(
        double latitude, double longitude, double radiusMeters,
        CancellationToken ct = default);

    /// <summary>
    /// Join a call as a participant. Assigns an anonymized alias.
    /// Transitions call to Connecting if first participant, Active if second+.
    /// </summary>
    Task<WatchCallParticipant> JoinCallAsync(
        string callId, string userId, CancellationToken ct = default);

    /// <summary>Leave a call. If last participant leaves, transitions to Resolved.</summary>
    Task LeaveCallAsync(
        string callId, string userId, CancellationToken ct = default);

    /// <summary>
    /// Append a narration line to the call's transcript.
    /// Called by the scene narration service as it processes frames.
    /// </summary>
    Task AppendNarrationAsync(
        string callId, TimestampedNarration narration, CancellationToken ct = default);

    /// <summary>
    /// Resolve a call — situation assessed as safe. No further action.
    /// </summary>
    Task<WatchCall> ResolveCallAsync(
        string callId, string resolution, CancellationToken ct = default);

    /// <summary>
    /// Escalate a call to a full SOS ResponseRequest. Creates a ResponseRequest
    /// and links it to this call. Call transitions to Escalated.
    /// </summary>
    Task<WatchCall> EscalateCallAsync(
        string callId, string escalatedBy, string? reason = null,
        CancellationToken ct = default);

    // ── ICE Server Configuration ─────────────────────────────────

    /// <summary>
    /// Get STUN/TURN server configurations for WebRTC peer connections.
    /// In dev: returns Google public STUN only.
    /// In prod: includes Cloudflare TURN with short-lived credentials.
    /// </summary>
    Task<IReadOnlyList<IceServerConfig>> GetIceServersAsync(
        CancellationToken ct = default);
}
