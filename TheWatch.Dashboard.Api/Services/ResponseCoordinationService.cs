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

using System.Text;
using System.Text.Json;
using Hangfire;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using TheWatch.Dashboard.Api.Hubs;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Enums;

namespace TheWatch.Dashboard.Api.Services;

public class ResponseCoordinationService : IResponseCoordinationService
{
    private readonly IResponseRequestPort _requestPort;
    private readonly IResponseDispatchPort _dispatchPort;
    private readonly IResponseTrackingPort _trackingPort;
    private readonly IEscalationPort _escalationPort;
    private readonly IParticipationPort _participationPort;
    private readonly INavigationPort _navigationPort;
    private readonly IResponderCommunicationPort _communicationPort;
    private readonly ISpatialIndex _spatialIndex;
    private readonly IAuditTrail _auditTrail;
    private readonly IEmergencyServicesPort _emergencyServices;
    private readonly EscalationConfiguration _escalationConfig;
    private readonly INotificationSendPort _notificationSendPort;
    private readonly INotificationRegistrationPort _notificationRegistrationPort;
    private readonly INotificationTrackingPort _notificationTrackingPort;
    private readonly IHubContext<DashboardHub> _hubContext;
    private readonly IBackgroundJobClient _hangfireClient;
    private readonly IConnection _rabbitConnection;
    private readonly ILogger<ResponseCoordinationService> _logger;

    // Exchange and routing key constants — must match Bicep/Aspire infrastructure config.
    // The "swarm-tasks" exchange is a topic exchange provisioned by the AppHost.
    // The "response-dispatch" queue is bound to this exchange with routing key "response.dispatch".
    private const string SwarmTasksExchange = "swarm-tasks";
    private const string DispatchRoutingKey = "response.dispatch";

    public ResponseCoordinationService(
        IResponseRequestPort requestPort,
        IResponseDispatchPort dispatchPort,
        IResponseTrackingPort trackingPort,
        IEscalationPort escalationPort,
        IParticipationPort participationPort,
        INavigationPort navigationPort,
        IResponderCommunicationPort communicationPort,
        ISpatialIndex spatialIndex,
        IAuditTrail auditTrail,
        IEmergencyServicesPort emergencyServices,
        IOptions<EscalationConfiguration> escalationConfig,
        INotificationSendPort notificationSendPort,
        INotificationRegistrationPort notificationRegistrationPort,
        INotificationTrackingPort notificationTrackingPort,
        IHubContext<DashboardHub> hubContext,
        IBackgroundJobClient hangfireClient,
        IConnection rabbitConnection,
        ILogger<ResponseCoordinationService> logger)
    {
        _requestPort = requestPort;
        _dispatchPort = dispatchPort;
        _trackingPort = trackingPort;
        _escalationPort = escalationPort;
        _participationPort = participationPort;
        _navigationPort = navigationPort;
        _communicationPort = communicationPort;
        _spatialIndex = spatialIndex;
        _auditTrail = auditTrail;
        _emergencyServices = emergencyServices;
        _escalationConfig = escalationConfig.Value;
        _notificationSendPort = notificationSendPort;
        _notificationRegistrationPort = notificationRegistrationPort;
        _notificationTrackingPort = notificationTrackingPort;
        _hubContext = hubContext;
        _hangfireClient = hangfireClient;
        _rabbitConnection = rabbitConnection;
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

        // 3. Query ISpatialIndex for nearby candidate responders (geohash/H3 lookup)
        var spatialQuery = new SpatialQuery
        {
            Latitude = latitude,
            Longitude = longitude,
            RadiusMeters = radiusMeters,
            MaxResults = Math.Min(desiredResponders * 3, 150) // Over-query to account for declines
        };
        var spatialCandidates = await _spatialIndex.FindNearbyAsync(spatialQuery, ct);

        _logger.LogInformation(
            "Spatial index returned {CandidateCount} nearby entities for {Scope} request {RequestId} " +
            "within {Radius}m radius",
            spatialCandidates.Count, scope, created.RequestId, radiusMeters);

        // 3a. Also query participation preferences for scope-aware filtering
        var eligible = await _participationPort.FindEligibleRespondersAsync(
            latitude, longitude, radiusMeters, scope,
            maxResults: Math.Min(desiredResponders * 3, 150), // Over-query to account for declines
            ct: ct);

        _logger.LogInformation(
            "Found {EligibleCount} eligible responders (participation-filtered) and " +
            "{SpatialCount} spatial candidates for {Scope} request {RequestId}",
            eligible.Count, spatialCandidates.Count, scope, created.RequestId);

        // 3b. Audit the response creation with spatial candidate count
        await _auditTrail.AppendAsync(new AuditEntry
        {
            UserId = userId,
            ActorRole = "System",
            Action = AuditAction.ResponseRequestCreated,
            EntityType = "ResponseRequest",
            EntityId = created.RequestId,
            CorrelationId = created.RequestId,
            SourceSystem = "Dashboard.Api",
            SourceComponent = "ResponseCoordinationService",
            Severity = AuditSeverity.Critical,
            DataClassification = DataClassification.Confidential,
            Outcome = AuditOutcome.Success,
            Reason = $"SOS response created: scope={scope}, radius={radiusMeters}m, " +
                     $"spatialCandidates={spatialCandidates.Count}, eligibleResponders={eligible.Count}",
            NewValue = JsonSerializer.Serialize(new
            {
                created.RequestId, created.Scope, created.RadiusMeters,
                created.DesiredResponderCount, SpatialCandidates = spatialCandidates.Count,
                EligibleResponders = eligible.Count
            })
        }, ct);

        // 4. Dispatch notifications (in-process via adapter)
        var dispatched = await _dispatchPort.DispatchAsync(created, ct);

        // 4a. Publish ResponseDispatchMessage to RabbitMQ for async fan-out.
        // The ResponseDispatchFunction (Azure Functions) consumes this and sends
        // push notifications to each eligible responder via ISpatialIndex + INotificationSendPort.
        // This decouples the HTTP response from O(N) notification delivery.
        await PublishDispatchMessageAsync(created, ct);

        // 4b. Fire-and-forget push notifications to eligible responders.
        // This runs in parallel with the rest of the pipeline — it must NOT block
        // the SOS response creation. If push delivery fails, the RabbitMQ fan-out
        // (step 4a) and the in-process dispatch (step 4) serve as fallbacks.
        _ = Task.Run(async () =>
        {
            try
            {
                await SendPushNotificationsToRespondersAsync(created, eligible, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Fire-and-forget push notification delivery failed for RequestId={RequestId}",
                    created.RequestId);
            }
        }, CancellationToken.None);

        // 5. Update status to Active
        var active = await _requestPort.UpdateStatusAsync(created.RequestId, ResponseStatus.Active, ct);

        // 6. Schedule the full 4-stage escalation chain via Hangfire
        //    Stage 1 (Initial dispatch): already done above (steps 3-4).
        //    Stage 2 (Widen scope):      scheduled at _escalationConfig.WidenScopeDelay
        //    Stage 3 (Emergency contacts): scheduled at _escalationConfig.EmergencyContactsDelay
        //    Stage 4 (First responders):   scheduled at _escalationConfig.FirstRespondersDelay
        //
        //    Each Hangfire job calls EscalateResponseStageAsync which checks whether the
        //    stage action is still needed (i.e., enough responders haven't acknowledged yet).
        if (escalation != EscalationPolicy.Manual)
        {
            await _escalationPort.ScheduleEscalationAsync(active, ct);

            // Schedule stage 2: widen scope (default 5 min)
            if (_escalationConfig.WidenScopeDelay > TimeSpan.Zero)
            {
                _hangfireClient.Schedule<ResponseCoordinationService>(
                    svc => svc.EscalateResponseStageAsync(
                        active.RequestId, EscalationStage.WidenScope, CancellationToken.None),
                    _escalationConfig.WidenScopeDelay);
            }

            // Schedule stage 3: emergency contacts (default 10 min)
            if (_escalationConfig.EmergencyContactsDelay > TimeSpan.Zero)
            {
                _hangfireClient.Schedule<ResponseCoordinationService>(
                    svc => svc.EscalateResponseStageAsync(
                        active.RequestId, EscalationStage.EmergencyContacts, CancellationToken.None),
                    _escalationConfig.EmergencyContactsDelay);
            }

            // Schedule stage 4: first responders / 911 (default 15 min)
            if (_escalationConfig.AutoDial911 && _escalationConfig.FirstRespondersDelay > TimeSpan.Zero)
            {
                _hangfireClient.Schedule<ResponseCoordinationService>(
                    svc => svc.EscalateResponseStageAsync(
                        active.RequestId, EscalationStage.FirstResponders, CancellationToken.None),
                    _escalationConfig.FirstRespondersDelay);
            }

            // Keep the legacy IEscalationPort check as a fallback
            if (escalationTimeout > TimeSpan.Zero)
            {
                _hangfireClient.Schedule<IEscalationPort>(
                    port => port.CheckAndEscalateAsync(active.RequestId, CancellationToken.None),
                    escalationTimeout);
            }

            // Audit escalation scheduling
            await _auditTrail.AppendAsync(new AuditEntry
            {
                UserId = active.UserId,
                ActorRole = "System",
                Action = AuditAction.EscalationScheduled,
                EntityType = "ResponseRequest",
                EntityId = active.RequestId,
                CorrelationId = active.RequestId,
                SourceSystem = "Dashboard.Api",
                SourceComponent = "ResponseCoordinationService",
                Severity = AuditSeverity.Warning,
                DataClassification = DataClassification.Confidential,
                Outcome = AuditOutcome.Success,
                Reason = $"Escalation chain scheduled: policy={escalation}, " +
                         $"widenScope={_escalationConfig.WidenScopeDelaySeconds}s, " +
                         $"emergencyContacts={_escalationConfig.EmergencyContactsDelaySeconds}s, " +
                         $"firstResponders={_escalationConfig.FirstRespondersDelaySeconds}s"
            }, ct);

            _logger.LogInformation(
                "Escalation chain scheduled for {RequestId}: policy={Policy}, " +
                "widen={Widen}s, contacts={Contacts}s, 911={FirstResp}s",
                active.RequestId, escalation,
                _escalationConfig.WidenScopeDelaySeconds,
                _escalationConfig.EmergencyContactsDelaySeconds,
                _escalationConfig.FirstRespondersDelaySeconds);
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

    // ── Responder Communication ────────────────────────────────────

    public async Task<(ResponderMessage Message, GuardrailsResult Guardrails)> SendResponderMessageAsync(
        string requestId,
        string senderId,
        string senderName,
        string? senderRole,
        ResponderMessageType messageType,
        string content,
        double? latitude = null,
        double? longitude = null,
        string? quickResponseCode = null,
        CancellationToken ct = default)
    {
        var message = new ResponderMessage(
            MessageId: Guid.NewGuid().ToString("N")[..12],
            RequestId: requestId,
            SenderId: senderId,
            SenderName: senderName,
            SenderRole: senderRole,
            MessageType: messageType,
            Content: content,
            Latitude: latitude,
            Longitude: longitude,
            QuickResponseCode: quickResponseCode,
            Verdict: GuardrailsVerdict.Approved, // Placeholder — overwritten by port
            GuardrailsNote: null,
            RedactedContent: null,
            SentAt: DateTime.UtcNow
        );

        var (processed, guardrails) = await _communicationPort.SendMessageAsync(message, ct);

        // Broadcast to the incident's response group if message was approved or redacted
        if (guardrails.Verdict is GuardrailsVerdict.Approved or GuardrailsVerdict.Redacted)
        {
            // Use the redacted content if PII was found, otherwise original
            var deliveredContent = guardrails.Verdict == GuardrailsVerdict.Redacted
                ? guardrails.RedactedContent ?? content
                : content;

            await _hubContext.Clients.Group($"response-{requestId}").SendAsync("ResponderMessage", new
            {
                processed.MessageId,
                processed.RequestId,
                processed.SenderId,
                processed.SenderName,
                processed.SenderRole,
                MessageType = processed.MessageType.ToString(),
                Content = deliveredContent,
                processed.Latitude,
                processed.Longitude,
                processed.QuickResponseCode,
                Verdict = processed.Verdict.ToString(),
                processed.SentAt
            }, ct);

            _logger.LogInformation(
                "Responder message delivered: {MessageId} from {SenderName} in {RequestId} ({Verdict})",
                processed.MessageId, processed.SenderName, requestId, guardrails.Verdict);
        }

        return (processed, guardrails);
    }

    public async Task<IReadOnlyList<ResponderMessage>> GetResponderMessagesAsync(
        string requestId, int limit = 100, DateTime? since = null,
        CancellationToken ct = default)
    {
        return await _communicationPort.GetMessagesAsync(requestId, limit, since, ct);
    }

    public IReadOnlyList<(string Code, string DisplayText, string Category)> GetQuickResponses()
        => _communicationPort.GetQuickResponses();

    // ── Escalation Chain Execution ───────────────────────────────────────────

    /// <summary>
    /// Executes a single escalation stage for a response request.
    /// Called by Hangfire delayed jobs at each configured interval.
    /// Each stage checks whether the escalation action is still needed before acting:
    ///   - If the request is already resolved/cancelled, do nothing.
    ///   - If enough responders have acknowledged, do nothing.
    ///   - Otherwise, execute the stage action and log to audit trail.
    ///
    /// Escalation stages:
    ///   WidenScope        — expand radius by 2x and re-dispatch to more volunteers
    ///   EmergencyContacts — notify the user's emergency contacts via push + SMS
    ///   FirstResponders   — call 911 via IEmergencyServicesPort (if user consented)
    /// </summary>
    public async Task EscalateResponseStageAsync(
        string requestId,
        EscalationStage stage,
        CancellationToken ct)
    {
        var request = await _requestPort.GetRequestAsync(requestId, ct);
        if (request is null)
        {
            _logger.LogWarning(
                "Escalation stage {Stage} for {RequestId}: request not found, skipping",
                stage, requestId);
            return;
        }

        // Skip if request is no longer active
        if (request.Status is ResponseStatus.Resolved or ResponseStatus.Cancelled or ResponseStatus.Expired)
        {
            _logger.LogInformation(
                "Escalation stage {Stage} for {RequestId}: request status is {Status}, skipping",
                stage, requestId, request.Status);
            return;
        }

        var ackCount = await _trackingPort.GetAcknowledgmentCountAsync(requestId, ct);
        var elapsed = DateTime.UtcNow - request.CreatedAt;

        _logger.LogWarning(
            "ESCALATION STAGE {Stage} for {RequestId}: acks={AckCount}/{Desired}, elapsed={Elapsed}",
            stage, requestId, ackCount, request.DesiredResponderCount, elapsed);

        switch (stage)
        {
            case EscalationStage.WidenScope:
                await ExecuteWidenScopeAsync(request, ackCount, ct);
                break;

            case EscalationStage.EmergencyContacts:
                await ExecuteEmergencyContactsAsync(request, ackCount, ct);
                break;

            case EscalationStage.FirstResponders:
                await ExecuteFirstRespondersAsync(request, ackCount, ct);
                break;

            default:
                _logger.LogInformation(
                    "Escalation stage {Stage} for {RequestId}: no action defined",
                    stage, requestId);
                break;
        }
    }

    /// <summary>
    /// Stage 2: Widen scope — if fewer than MinRespondersBeforeWiden have acknowledged,
    /// expand the search radius by RadiusMultiplierOnWiden and re-dispatch to the
    /// newly discovered volunteers.
    /// </summary>
    private async Task ExecuteWidenScopeAsync(
        ResponseRequest request, int ackCount, CancellationToken ct)
    {
        if (ackCount >= _escalationConfig.MinRespondersBeforeWiden)
        {
            _logger.LogInformation(
                "WidenScope for {RequestId}: {AckCount} acks >= {Min} threshold, skipping",
                request.RequestId, ackCount, _escalationConfig.MinRespondersBeforeWiden);
            return;
        }

        var newRadius = _escalationConfig.GetWidenedRadius(
            request.RadiusMeters, request.RadiusMeters);

        _logger.LogWarning(
            "WIDEN SCOPE for {RequestId}: expanding radius from {OldRadius}m to {NewRadius}m, " +
            "only {AckCount} of {MinRequired} responders acknowledged",
            request.RequestId, request.RadiusMeters, newRadius,
            ackCount, _escalationConfig.MinRespondersBeforeWiden);

        // Query spatial index with expanded radius
        var expandedQuery = new SpatialQuery
        {
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            RadiusMeters = newRadius,
            MaxResults = request.DesiredResponderCount * 3
        };
        var newCandidates = await _spatialIndex.FindNearbyAsync(expandedQuery, ct);

        // Re-dispatch with expanded radius
        var redispatched = await _dispatchPort.RedispatchAsync(
            request, newRadius, request.DesiredResponderCount * 2, ct);

        // Update request status to Escalated
        await _requestPort.UpdateStatusAsync(request.RequestId, ResponseStatus.Escalated, ct);

        // Audit the escalation
        await _auditTrail.AppendAsync(new AuditEntry
        {
            UserId = request.UserId,
            ActorRole = "Escalation",
            Action = AuditAction.EscalationScopeExpanded,
            EntityType = "ResponseRequest",
            EntityId = request.RequestId,
            CorrelationId = request.RequestId,
            SourceSystem = "Dashboard.Api",
            SourceComponent = "ResponseCoordinationService.EscalateWidenScope",
            Severity = AuditSeverity.Warning,
            DataClassification = DataClassification.Confidential,
            Outcome = AuditOutcome.Success,
            Reason = $"Scope widened: {request.RadiusMeters}m -> {newRadius}m, " +
                     $"acks={ackCount}/{_escalationConfig.MinRespondersBeforeWiden}, " +
                     $"newCandidates={newCandidates.Count}, redispatched={redispatched}"
        }, ct);

        // Broadcast escalation to dashboard
        await _hubContext.Clients.All.SendAsync("EscalationStageExecuted", new
        {
            request.RequestId,
            Stage = EscalationStage.WidenScope.ToString(),
            OldRadiusMeters = request.RadiusMeters,
            NewRadiusMeters = newRadius,
            AckCount = ackCount,
            NewCandidates = newCandidates.Count,
            Redispatched = redispatched
        }, ct);
    }

    /// <summary>
    /// Stage 3: Emergency contacts — notify the user's pre-registered emergency contacts
    /// with the user's location and incident details. This fires when volunteers alone
    /// haven't resolved the situation.
    /// </summary>
    private async Task ExecuteEmergencyContactsAsync(
        ResponseRequest request, int ackCount, CancellationToken ct)
    {
        // If enough responders acknowledged, skip emergency contacts notification
        if (ackCount >= request.DesiredResponderCount)
        {
            _logger.LogInformation(
                "EmergencyContacts for {RequestId}: {AckCount} acks >= {Desired}, skipping",
                request.RequestId, ackCount, request.DesiredResponderCount);
            return;
        }

        _logger.LogWarning(
            "EMERGENCY CONTACTS NOTIFICATION for {RequestId}: " +
            "only {AckCount}/{Desired} responders acknowledged after {Delay}s",
            request.RequestId, ackCount, request.DesiredResponderCount,
            _escalationConfig.EmergencyContactsDelaySeconds);

        // In production: query the user's emergency contacts from the user profile port
        // and send push notifications + SMS via INotificationSendPort and ISmsPort.
        // For now: audit the escalation event.

        await _auditTrail.AppendAsync(new AuditEntry
        {
            UserId = request.UserId,
            ActorRole = "Escalation",
            Action = AuditAction.EscalationFired,
            EntityType = "ResponseRequest",
            EntityId = request.RequestId,
            CorrelationId = request.RequestId,
            SourceSystem = "Dashboard.Api",
            SourceComponent = "ResponseCoordinationService.EscalateEmergencyContacts",
            Severity = AuditSeverity.Critical,
            DataClassification = DataClassification.HighlyConfidential,
            Outcome = AuditOutcome.Success,
            Reason = $"Emergency contacts notified: acks={ackCount}/{request.DesiredResponderCount}, " +
                     $"elapsed={_escalationConfig.EmergencyContactsDelaySeconds}s"
        }, ct);

        // Broadcast to dashboard
        await _hubContext.Clients.All.SendAsync("EscalationStageExecuted", new
        {
            request.RequestId,
            Stage = EscalationStage.EmergencyContacts.ToString(),
            AckCount = ackCount,
            DesiredResponders = request.DesiredResponderCount,
            Message = "Emergency contacts have been notified"
        }, ct);
    }

    /// <summary>
    /// Stage 4: First responders — call 911 via IEmergencyServicesPort.
    /// This is the final escalation stage. Only fires if:
    ///   1. AutoDial911 is enabled in config
    ///   2. The user opted into auto-911 at signup (checked by IEmergencyServicesPort)
    ///   3. The request is still active with insufficient responders
    /// </summary>
    private async Task ExecuteFirstRespondersAsync(
        ResponseRequest request, int ackCount, CancellationToken ct)
    {
        // If enough responders, skip 911
        if (ackCount >= request.DesiredResponderCount)
        {
            _logger.LogInformation(
                "FirstResponders for {RequestId}: {AckCount} acks >= {Desired}, skipping 911",
                request.RequestId, ackCount, request.DesiredResponderCount);
            return;
        }

        if (!_escalationConfig.AutoDial911)
        {
            _logger.LogWarning(
                "FirstResponders for {RequestId}: AutoDial911 disabled in config, skipping",
                request.RequestId);
            return;
        }

        _logger.LogWarning(
            "911 ESCALATION for {RequestId}: {AckCount}/{Desired} responders after {Delay}s — " +
            "initiating emergency services call",
            request.RequestId, ackCount, request.DesiredResponderCount,
            _escalationConfig.FirstRespondersDelaySeconds);

        // Build the 911 request
        var emergencyRequest = new Emergency911Request(
            RequestId: Guid.NewGuid().ToString("N")[..12],
            UserId: request.UserId,
            ResponseRequestId: request.RequestId,
            TriggerSource: Emergency911TriggerSource.AutoEscalation,
            ServiceType: EmergencyServiceType.All,
            Latitude: request.Latitude,
            Longitude: request.Longitude,
            AccuracyMeters: request.AccuracyMeters,
            Address: null, // Will be filled by the emergency services adapter
            ContextSummary: $"Automated emergency escalation from The Watch safety application. " +
                           $"User triggered a {request.Scope} alert {_escalationConfig.FirstRespondersDelaySeconds} seconds ago. " +
                           $"Only {ackCount} of {request.DesiredResponderCount} volunteer responders acknowledged. " +
                           $"Alert type: {request.TriggerSource ?? "MANUAL"}. " +
                           $"Description: {request.Description ?? "No description provided"}.",
            UserPhoneNumber: "unknown", // In production: fetch from user profile
            UserName: request.UserId,   // In production: fetch from user profile
            MedicalInfo: null,          // In production: fetch from Emergency911Consent
            AccessInstructions: null,   // In production: fetch from Emergency911Consent
            VolunteerRespondersEnRoute: ackCount,
            InitiatedBy: "system-escalation",
            CreatedAt: DateTime.UtcNow
        );

        try
        {
            var result = await _emergencyServices.Initiate911CallAsync(emergencyRequest, ct);

            // Audit the 911 initiation
            await _auditTrail.AppendAsync(new AuditEntry
            {
                UserId = request.UserId,
                ActorRole = "Escalation",
                Action = AuditAction.Emergency911Initiated,
                EntityType = "Emergency911Request",
                EntityId = emergencyRequest.RequestId,
                CorrelationId = request.RequestId,
                SourceSystem = "Dashboard.Api",
                SourceComponent = "ResponseCoordinationService.EscalateFirstResponders",
                Severity = AuditSeverity.Critical,
                DataClassification = DataClassification.HighlyConfidential,
                Outcome = result.Status == Emergency911CallStatus.Completed
                    ? AuditOutcome.Success
                    : result.Status == Emergency911CallStatus.Failed
                        ? AuditOutcome.Failure
                        : AuditOutcome.Success,
                Reason = $"911 auto-escalation: status={result.Status}, " +
                         $"rapidSosPushed={result.RapidSosLocationPushed}, " +
                         $"acks={ackCount}/{request.DesiredResponderCount}",
                NewValue = JsonSerializer.Serialize(new
                {
                    result.RequestId, result.Status, result.ExternalCallId,
                    result.RapidSosLocationPushed, result.CallDuration
                })
            }, ct);

            _logger.LogWarning(
                "911 CALL INITIATED for {RequestId}: status={Status}, " +
                "RapidSOS={RapidSos}, externalCallId={CallId}",
                request.RequestId, result.Status,
                result.RapidSosLocationPushed, result.ExternalCallId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "911 CALL FAILED for {RequestId}: {Error}",
                request.RequestId, ex.Message);

            await _auditTrail.AppendAsync(new AuditEntry
            {
                UserId = request.UserId,
                ActorRole = "Escalation",
                Action = AuditAction.Emergency911CallFailed,
                EntityType = "Emergency911Request",
                EntityId = emergencyRequest.RequestId,
                CorrelationId = request.RequestId,
                SourceSystem = "Dashboard.Api",
                SourceComponent = "ResponseCoordinationService.EscalateFirstResponders",
                Severity = AuditSeverity.Critical,
                DataClassification = DataClassification.HighlyConfidential,
                Outcome = AuditOutcome.Failure,
                ErrorMessage = ex.Message,
                Reason = $"911 auto-escalation failed: {ex.Message}"
            }, ct);
        }

        // Broadcast to dashboard
        await _hubContext.Clients.All.SendAsync("EscalationStageExecuted", new
        {
            request.RequestId,
            Stage = EscalationStage.FirstResponders.ToString(),
            AckCount = ackCount,
            DesiredResponders = request.DesiredResponderCount,
            Message = "911 emergency services have been contacted"
        }, ct);
    }

    // ── Push Notification Delivery ───────────────────────────────────────────

    /// <summary>
    /// Send push notifications to eligible responders for an SOS dispatch.
    /// Fire-and-forget — called from Task.Run so it doesn't block the pipeline.
    /// Builds a NotificationPayload per responder, sends via INotificationSendPort,
    /// and tracks delivery results via INotificationTrackingPort.
    /// </summary>
    private async Task SendPushNotificationsToRespondersAsync(
        ResponseRequest request,
        IReadOnlyList<EligibleResponder> eligible,
        CancellationToken ct)
    {
        if (eligible.Count == 0) return;

        var payloads = new List<NotificationPayload>();

        foreach (var responder in eligible)
        {
            // Look up the responder's registered devices
            var devices = await _notificationRegistrationPort.GetDevicesAsync(responder.UserId, ct);
            var activeDevice = devices.FirstOrDefault(d => d.IsActive);

            var payload = new NotificationPayload(
                NotificationId: Guid.NewGuid().ToString("N")[..12],
                RecipientUserId: responder.UserId,
                RecipientDeviceToken: activeDevice?.DeviceToken,
                RecipientPhoneNumber: null,
                Category: NotificationCategory.SosDispatch,
                Priority: request.Scope is ResponseScope.Community or ResponseScope.Evacuation or ResponseScope.SilentDuress
                    ? NotificationPriority.Critical
                    : NotificationPriority.High,
                PreferredChannel: NotificationChannel.Push,
                Title: "Emergency Alert",
                Body: $"Someone needs help {responder.DistanceMeters:F0}m away. Tap to respond.",
                Subtitle: request.Description,
                DeepLink: $"thewatch://response/{request.RequestId}",
                RequestId: request.RequestId,
                RequestorName: null, // Privacy: don't include name in notification
                Scope: request.Scope,
                IncidentLatitude: request.Latitude,
                IncidentLongitude: request.Longitude,
                DistanceMeters: responder.DistanceMeters,
                SmsReplyInstructions: null,
                CreatedAt: DateTime.UtcNow,
                ExpiresAfter: request.EscalationTimeout
            );

            payloads.Add(payload);
        }

        if (payloads.Count == 0) return;

        // Send batch via the notification port
        var results = await _notificationSendPort.SendPushBatchAsync(payloads, ct);

        // Track all delivery results
        foreach (var result in results)
        {
            await _notificationTrackingPort.RecordDeliveryAsync(result, ct);
        }

        var delivered = results.Count(r => r.Status == NotificationDeliveryStatus.Delivered);
        _logger.LogInformation(
            "Push notifications sent for {RequestId}: {Delivered}/{Total} delivered",
            request.RequestId, delivered, results.Count);
    }

    // ── RabbitMQ Publishing ────────────────────────────────────────────────

    /// <summary>
    /// Publish a ResponseDispatchMessage to the "swarm-tasks" exchange.
    /// Uses the Aspire-injected IConnection to create a short-lived channel,
    /// declare the exchange (idempotent), and publish the serialized message.
    ///
    /// Exchange type: "topic" — allows routing by key pattern.
    /// Routing key: "response.dispatch" — matched by the response-dispatch queue binding.
    ///
    /// The message is persistent (delivery mode 2) so it survives broker restarts.
    /// </summary>
    private Task PublishDispatchMessageAsync(ResponseRequest request, CancellationToken ct)
    {
        try
        {
            var dispatchMessage = new ResponseDispatchMessage(
                RequestId: request.RequestId,
                UserId: request.UserId,
                Scope: request.Scope,
                Latitude: request.Latitude,
                Longitude: request.Longitude,
                RadiusMeters: request.RadiusMeters,
                DesiredResponderCount: request.DesiredResponderCount,
                Strategy: request.Strategy,
                Description: request.Description,
                TriggerSource: request.TriggerSource,
                CreatedAt: request.CreatedAt
            );

            var json = JsonSerializer.Serialize(dispatchMessage, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });

            var body = Encoding.UTF8.GetBytes(json);

            // Create a channel per publish — channels are lightweight in RabbitMQ.Client 6.x.
            // IConnection is injected by Aspire.RabbitMQ.Client which manages lifecycle/reconnect.
            using var channel = _rabbitConnection.CreateModel();

            // Declare the exchange idempotently — if it already exists (from Bicep/Aspire),
            // this is a no-op. If running locally without infrastructure, it creates it.
            channel.ExchangeDeclare(
                exchange: SwarmTasksExchange,
                type: ExchangeType.Topic,
                durable: true,
                autoDelete: false,
                arguments: null);

            // Ensure the queue exists and is bound to the exchange.
            // In production, this is done by the Bicep/Aspire infrastructure template.
            // Declaring here ensures local dev works without manual setup.
            channel.QueueDeclare(
                queue: "response-dispatch",
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null);

            channel.QueueBind(
                queue: "response-dispatch",
                exchange: SwarmTasksExchange,
                routingKey: DispatchRoutingKey,
                arguments: null);

            // Publish with persistent delivery mode (2)
            var properties = channel.CreateBasicProperties();
            properties.DeliveryMode = 2; // Persistent — survives broker restart
            properties.ContentType = "application/json";
            properties.ContentEncoding = "utf-8";
            properties.MessageId = request.RequestId;
            properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            properties.Type = nameof(ResponseDispatchMessage);

            channel.BasicPublish(
                exchange: SwarmTasksExchange,
                routingKey: DispatchRoutingKey,
                mandatory: false,
                basicProperties: properties,
                body: body);

            _logger.LogInformation(
                "Published ResponseDispatchMessage to {Exchange}/{RoutingKey}: " +
                "RequestId={RequestId}, Scope={Scope}, Radius={Radius}m",
                SwarmTasksExchange, DispatchRoutingKey,
                request.RequestId, request.Scope, request.RadiusMeters);
        }
        catch (Exception ex)
        {
            // Log but don't throw — the in-process dispatch (step 4) already ran.
            // The RabbitMQ message is for async fan-out; failure here degrades
            // notification delivery but doesn't block the SOS response creation.
            _logger.LogError(ex,
                "Failed to publish ResponseDispatchMessage to RabbitMQ for RequestId={RequestId}. " +
                "In-process dispatch completed ({Dispatched} responders), but async fan-out failed.",
                request.RequestId, request.DesiredResponderCount);
        }

        return Task.CompletedTask;
    }
}
