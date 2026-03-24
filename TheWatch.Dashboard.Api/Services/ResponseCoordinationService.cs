// ResponseCoordinationService — orchestrates the full SOS response pipeline.
// Composes IResponseRequestPort, IResponseDispatchPort, IResponseTrackingPort,
// IEscalationPort, and IParticipationPort into a single coherent workflow.
//
// This service:
//   1. Creates ResponseRequests with scope-appropriate presets
//   2. Finds eligible responders based on participation preferences
//   3. Dispatches notifications to eligible responders
//   4. Schedules escalation timers via Hangfire
//   5. Broadcasts real-time updates via SignalR
//
// All business rules (scope presets, escalation policies) live in Shared.
// This service is pure orchestration — no domain logic.

using Hangfire;
using Microsoft.AspNetCore.SignalR;
using TheWatch.Dashboard.Api.Hubs;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Dashboard.Api.Services;

public class ResponseCoordinationService : IResponseCoordinationService
{
    private readonly IResponseRequestPort _requestPort;
    private readonly IResponseDispatchPort _dispatchPort;
    private readonly IResponseTrackingPort _trackingPort;
    private readonly IEscalationPort _escalationPort;
    private readonly IParticipationPort _participationPort;
    private readonly INavigationPort _navigationPort;
    private readonly IHubContext<DashboardHub> _hubContext;
    private readonly IBackgroundJobClient _hangfireClient;
    private readonly ILogger<ResponseCoordinationService> _logger;

    public ResponseCoordinationService(
        IResponseRequestPort requestPort,
        IResponseDispatchPort dispatchPort,
        IResponseTrackingPort trackingPort,
        IEscalationPort escalationPort,
        IParticipationPort participationPort,
        INavigationPort navigationPort,
        IHubContext<DashboardHub> hubContext,
        IBackgroundJobClient hangfireClient,
        ILogger<ResponseCoordinationService> logger)
    {
        _requestPort = requestPort;
        _dispatchPort = dispatchPort;
        _trackingPort = trackingPort;
        _escalationPort = escalationPort;
        _participationPort = participationPort;
        _navigationPort = navigationPort;
        _hubContext = hubContext;
        _hangfireClient = hangfireClient;
        _logger = logger;
    }

    public async Task<ResponseRequest> CreateResponseAsync(
        string userId,
        ResponseScope scope,
        double latitude,
        double longitude,
        string? description = null,
        string? triggerSource = null,
        CancellationToken ct = default)
    {
        // 1. Get scope-appropriate defaults
        var (radiusMeters, desiredResponders, escalation, strategy, escalationTimeout)
            = ResponseScopePresets.GetDefaults(scope);

        _logger.LogWarning(
            "SOS RESPONSE INITIATED: UserId={UserId}, Scope={Scope}, Radius={Radius}m, " +
            "DesiredResponders={Desired}, Escalation={Escalation}, Strategy={Strategy}, Source={Source}",
            userId, scope, radiusMeters, desiredResponders, escalation, strategy, triggerSource);

        // 2. Create the request record
        var request = new ResponseRequest(
            RequestId: Guid.NewGuid().ToString("N")[..12],
            UserId: userId,
            DeviceId: null,
            Scope: scope,
            Escalation: escalation,
            Strategy: strategy,
            Latitude: latitude,
            Longitude: longitude,
            AccuracyMeters: null,
            RadiusMeters: radiusMeters,
            DesiredResponderCount: desiredResponders,
            EscalationTimeout: escalationTimeout,
            Description: description,
            TriggerSource: triggerSource,
            TriggerConfidence: null,
            CreatedAt: DateTime.UtcNow,
            Status: ResponseStatus.Dispatching
        );

        var created = await _requestPort.CreateRequestAsync(request, ct);

        // 3. Find eligible responders in the area
        var eligible = await _participationPort.FindEligibleRespondersAsync(
            latitude, longitude, radiusMeters, scope,
            maxResults: Math.Min(desiredResponders * 3, 150), // Over-query to account for declines
            ct: ct);

        _logger.LogInformation(
            "Found {EligibleCount} eligible responders for {Scope} request {RequestId}",
            eligible.Count, scope, created.RequestId);

        // 4. Dispatch notifications
        var dispatched = await _dispatchPort.DispatchAsync(created, ct);

        // 5. Update status to Active
        var active = await _requestPort.UpdateStatusAsync(created.RequestId, ResponseStatus.Active, ct);

        // 6. Schedule escalation if policy requires it
        if (escalation != EscalationPolicy.Manual && escalationTimeout > TimeSpan.Zero)
        {
            await _escalationPort.ScheduleEscalationAsync(active, ct);

            // Also schedule a Hangfire delayed job for the escalation check
            _hangfireClient.Schedule<IEscalationPort>(
                port => port.CheckAndEscalateAsync(active.RequestId, CancellationToken.None),
                escalationTimeout);

            _logger.LogInformation(
                "Escalation scheduled for {RequestId}: policy={Policy}, timeout={Timeout}",
                active.RequestId, escalation, escalationTimeout);
        }

        // 7. Broadcast to dashboard via SignalR
        await _hubContext.Clients.All.SendAsync("SOSResponseCreated", new
        {
            active.RequestId,
            active.UserId,
            Scope = active.Scope.ToString(),
            active.Latitude,
            active.Longitude,
            active.RadiusMeters,
            active.DesiredResponderCount,
            Strategy = active.Strategy.ToString(),
            Escalation = active.Escalation.ToString(),
            Status = active.Status.ToString(),
            EligibleResponders = eligible.Count,
            Dispatched = dispatched,
            active.CreatedAt
        }, ct);

        _logger.LogWarning(
            "SOS RESPONSE ACTIVE: RequestId={RequestId}, Dispatched={Dispatched} of {Eligible} eligible",
            active.RequestId, dispatched, eligible.Count);

        return active;
    }

    public async Task<AcknowledgmentWithDirections> AcknowledgeResponseAsync(
        string requestId,
        string responderId,
        string responderName,
        string responderRole,
        double responderLatitude,
        double responderLongitude,
        double distanceMeters,
        bool hasVehicle = true,
        int? estimatedArrivalMinutes = null,
        CancellationToken ct = default)
    {
        var ack = new ResponderAcknowledgment(
            AckId: Guid.NewGuid().ToString("N")[..12],
            RequestId: requestId,
            ResponderId: responderId,
            ResponderName: responderName,
            ResponderRole: responderRole,
            ResponderLatitude: responderLatitude,
            ResponderLongitude: responderLongitude,
            DistanceMeters: distanceMeters,
            EstimatedArrival: estimatedArrivalMinutes.HasValue
                ? TimeSpan.FromMinutes(estimatedArrivalMinutes.Value)
                : null,
            Status: AckStatus.EnRoute,
            AcknowledgedAt: DateTime.UtcNow
        );

        var recorded = await _trackingPort.AcknowledgeAsync(ack, ct);

        // Check if we have enough responders to cancel escalation
        var request = await _requestPort.GetRequestAsync(requestId, ct);

        // Generate directions from responder's location to the incident
        NavigationDirections directions;
        if (request is not null)
        {
            var ackCount = await _trackingPort.GetAcknowledgmentCountAsync(requestId, ct);

            if (ackCount >= request.DesiredResponderCount)
            {
                _logger.LogInformation(
                    "Sufficient responders ({Count}/{Desired}) for {RequestId} — cancelling escalation",
                    ackCount, request.DesiredResponderCount, requestId);

                await _escalationPort.CancelEscalationAsync(requestId, ct);
            }

            directions = await _navigationPort.GetDirectionsAsync(
                requestId, responderId,
                responderLatitude, responderLongitude,
                request.Latitude, request.Longitude,
                hasVehicle, ct);
        }
        else
        {
            // Fallback: generate directions using responder coords as both origin and destination
            // (request not found — should not happen in normal flow)
            _logger.LogWarning("Request {RequestId} not found when generating directions", requestId);
            directions = await _navigationPort.GetDirectionsAsync(
                requestId, responderId,
                responderLatitude, responderLongitude,
                responderLatitude, responderLongitude,
                hasVehicle, ct);
        }

        // Broadcast responder update via SignalR — include directions so dashboard shows navigation
        await _hubContext.Clients.All.SendAsync("ResponderAcknowledged", new
        {
            recorded.AckId,
            recorded.RequestId,
            recorded.ResponderId,
            recorded.ResponderName,
            recorded.ResponderRole,
            recorded.DistanceMeters,
            recorded.EstimatedArrival,
            Status = recorded.Status.ToString(),
            Directions = new
            {
                directions.TravelMode,
                directions.GoogleMapsUrl,
                directions.AppleMapsUrl,
                directions.WazeUrl,
                directions.EstimatedTravelTime
            }
        }, ct);

        return new AcknowledgmentWithDirections(recorded, directions);
    }

    public async Task<ResponseRequest> CancelResponseAsync(
        string requestId,
        string reason,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Cancelling response {RequestId}: {Reason}", requestId, reason);

        var cancelled = await _requestPort.CancelRequestAsync(requestId, reason, ct);
        await _escalationPort.CancelEscalationAsync(requestId, ct);

        // Broadcast cancellation via SignalR
        await _hubContext.Clients.All.SendAsync("SOSResponseCancelled", new
        {
            cancelled.RequestId,
            cancelled.UserId,
            Reason = reason,
            Status = cancelled.Status.ToString()
        }, ct);

        return cancelled;
    }

    public async Task<ResponseRequest> ResolveResponseAsync(
        string requestId,
        string resolvedBy,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Resolving response {RequestId} by {ResolvedBy}", requestId, resolvedBy);

        var resolved = await _requestPort.ResolveRequestAsync(requestId, resolvedBy, ct);
        await _escalationPort.CancelEscalationAsync(requestId, ct);

        // Broadcast resolution via SignalR
        await _hubContext.Clients.All.SendAsync("SOSResponseResolved", new
        {
            resolved.RequestId,
            resolved.UserId,
            ResolvedBy = resolvedBy,
            Status = resolved.Status.ToString()
        }, ct);

        return resolved;
    }

    public async Task<ResponseSituation?> GetSituationAsync(
        string requestId,
        CancellationToken ct = default)
    {
        var request = await _requestPort.GetRequestAsync(requestId, ct);
        if (request is null) return null;

        var acks = await _trackingPort.GetAcknowledgmentsAsync(requestId, ct);
        var escalationHistory = await _escalationPort.GetEscalationHistoryAsync(requestId, ct);

        return new ResponseSituation(
            Request: request,
            Acknowledgments: acks,
            EscalationHistory: escalationHistory,
            TotalDispatched: request.DesiredResponderCount,
            TotalAcknowledged: acks.Count,
            TotalEnRoute: acks.Count(a => a.Status == AckStatus.EnRoute),
            TotalOnScene: acks.Count(a => a.Status == AckStatus.OnScene)
        );
    }

    public async Task<IReadOnlyList<ResponseRequest>> GetActiveResponsesAsync(
        string userId,
        CancellationToken ct = default)
    {
        return await _requestPort.GetActiveRequestsAsync(userId, ct);
    }
}
