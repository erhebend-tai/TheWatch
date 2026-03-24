// IResponseCoordinationService — application-level orchestrator for the response pipeline.
// This is NOT a port (ports live in Shared). This is a Dashboard API service that
// composes multiple ports into a cohesive SOS → dispatch → track → escalate workflow.
//
// Flow:
//   Mobile SOS trigger → CreateResponseAsync() → creates request via IResponseRequestPort
//     → finds eligible responders via IParticipationPort
//     → dispatches via IResponseDispatchPort
//     → schedules escalation via IEscalationPort
//     → publishes real-time updates via SignalR
//
// WAL: This service coordinates ports but contains no business logic itself.
//      All scope presets, escalation policies, and dispatch strategies live in Shared.

using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Dashboard.Api.Services;

public interface IResponseCoordinationService
{
    /// <summary>
    /// Full SOS pipeline: create request → find responders → dispatch → schedule escalation.
    /// Returns the created ResponseRequest with status set to Dispatching.
    /// </summary>
    Task<ResponseRequest> CreateResponseAsync(
        string userId,
        ResponseScope scope,
        double latitude,
        double longitude,
        string? description = null,
        string? triggerSource = null,
        CancellationToken ct = default);

    /// <summary>
    /// Record a responder's acknowledgment ("I'm on my way").
    /// Checks if enough responders have acknowledged to cancel scheduled escalation.
    /// Returns the acknowledgment paired with navigation directions to the incident.
    /// </summary>
    Task<AcknowledgmentWithDirections> AcknowledgeResponseAsync(
        string requestId,
        string responderId,
        string responderName,
        string responderRole,
        double responderLatitude,
        double responderLongitude,
        double distanceMeters,
        bool hasVehicle = true,
        int? estimatedArrivalMinutes = null,
        CancellationToken ct = default);

    /// <summary>
    /// Cancel an active response request (user pressed "I'm OK" or clear word detected).
    /// Cancels scheduled escalation and notifies all acknowledged responders.
    /// </summary>
    Task<ResponseRequest> CancelResponseAsync(
        string requestId,
        string reason,
        CancellationToken ct = default);

    /// <summary>
    /// Mark a response as resolved (situation handled).
    /// </summary>
    Task<ResponseRequest> ResolveResponseAsync(
        string requestId,
        string resolvedBy,
        CancellationToken ct = default);

    /// <summary>
    /// Get the current state of a response request with all acknowledgments.
    /// </summary>
    Task<ResponseSituation?> GetSituationAsync(
        string requestId,
        CancellationToken ct = default);

    /// <summary>
    /// Get all active responses for a user.
    /// </summary>
    Task<IReadOnlyList<ResponseRequest>> GetActiveResponsesAsync(
        string userId,
        CancellationToken ct = default);
}

/// <summary>
/// Snapshot of a response situation — the request plus all responder acknowledgments
/// and escalation history. Used by the dashboard and mobile clients.
/// </summary>
public record ResponseSituation(
    ResponseRequest Request,
    IReadOnlyList<ResponderAcknowledgment> Acknowledgments,
    IReadOnlyList<EscalationEvent> EscalationHistory,
    int TotalDispatched,
    int TotalAcknowledged,
    int TotalEnRoute,
    int TotalOnScene
);

/// <summary>
/// Returned when a responder acknowledges — pairs the ack record with
/// navigation directions (deep links) to the incident location.
/// The mobile client uses the directions to launch turn-by-turn navigation.
/// </summary>
public record AcknowledgmentWithDirections(
    ResponderAcknowledgment Acknowledgment,
    NavigationDirections Directions
);
