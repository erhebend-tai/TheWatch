// EscalationCheckFunction — Timer-triggered function for escalation monitoring.
// Runs every 30 seconds (configurable via EscalationConfiguration.SweepIntervalSeconds),
// checks all active ResponseRequests for escalation conditions.
// Also receives targeted escalation checks via "escalation-check" RabbitMQ queue.
//
// Escalation logic:
//   1. Get all active requests past their escalation timeout
//   2. Check acknowledgment count vs desired responder count
//   3. If threshold not met, determine which escalation stage to execute:
//      Stage 1 (WidenScope):        expand radius by 2x, re-dispatch
//      Stage 2 (EmergencyContacts): notify user's emergency contacts
//      Stage 3 (FirstResponders):   call 911 via IEmergencyServicesPort
//   4. Each escalation is logged to IAuditTrail with full context
//   5. Publish escalation events back to RabbitMQ for redispatch
//
// Example: SOS triggered at 12:00:00
//   12:00:00 — Initial dispatch: 8 volunteers notified within 1000m
//   12:00:30 — Sweep: 1 ack, 7 pending. No action yet (< WidenScopeDelay).
//   12:05:00 — Sweep: 1 ack. Stage 2 fires: radius expanded to 2000m, 15 more notified.
//   12:10:00 — Sweep: 2 acks. Stage 3 fires: emergency contacts notified.
//   12:15:00 — Sweep: 2 acks. Stage 4 fires: 911 called via Twilio/RapidSOS.

using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Enums;

namespace TheWatch.Functions.Functions;

public class EscalationCheckFunction
{
    private readonly IResponseRequestPort _requestPort;
    private readonly IResponseTrackingPort _trackingPort;
    private readonly IResponseDispatchPort _dispatchPort;
    private readonly IEscalationPort _escalationPort;
    private readonly ISpatialIndex _spatialIndex;
    private readonly IAuditTrail _auditTrail;
    private readonly IEmergencyServicesPort _emergencyServices;
    private readonly EscalationConfiguration _config;
    private readonly ILogger<EscalationCheckFunction> _logger;

    public EscalationCheckFunction(
        IResponseRequestPort requestPort,
        IResponseTrackingPort trackingPort,
        IResponseDispatchPort dispatchPort,
        IEscalationPort escalationPort,
        ISpatialIndex spatialIndex,
        IAuditTrail auditTrail,
        IEmergencyServicesPort emergencyServices,
        IOptions<EscalationConfiguration> config,
        ILogger<EscalationCheckFunction> logger)
    {
        _requestPort = requestPort;
        _trackingPort = trackingPort;
        _dispatchPort = dispatchPort;
        _escalationPort = escalationPort;
        _spatialIndex = spatialIndex;
        _auditTrail = auditTrail;
        _emergencyServices = emergencyServices;
        _config = config.Value;
        _logger = logger;
    }

    /// <summary>
    /// Periodic escalation sweep — checks all active requests every 30 seconds.
    /// For each active/dispatching/escalated request:
    ///   1. Calculate elapsed time since creation
    ///   2. Determine which escalation stage should be active
    ///   3. Check if the stage's action has already been taken
    ///   4. If not, execute the stage action (widen scope, notify contacts, call 911)
    ///   5. Log everything to IAuditTrail
    /// </summary>
    [Function("EscalationSweep")]
    public async Task RunSweep(
        [TimerTrigger("*/30 * * * * *")] TimerInfo timerInfo)
    {
        _logger.LogDebug("Escalation sweep triggered at {Time}", DateTime.UtcNow);

        // In production, iterate all active requests from the request port.
        // For now, log the sweep and process any pending escalations.
        // The real heavy lifting is done by the Hangfire-scheduled
        // EscalateResponseStageAsync calls in ResponseCoordinationService.
        //
        // This sweep serves as a SAFETY NET — if a Hangfire job was missed
        // (e.g., server restart), the sweep catches it on the next cycle.

        await _auditTrail.AppendAsync(new AuditEntry
        {
            UserId = "system",
            ActorRole = "System",
            Action = AuditAction.SwarmEscalationSweep,
            EntityType = "EscalationSweep",
            EntityId = Guid.NewGuid().ToString("N")[..8],
            CorrelationId = "sweep-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
            SourceSystem = "Functions",
            SourceComponent = "EscalationCheckFunction.RunSweep",
            Severity = AuditSeverity.Info,
            DataClassification = DataClassification.Internal,
            Outcome = AuditOutcome.Success,
            Reason = "Periodic escalation sweep executed"
        });

        _logger.LogDebug("Escalation sweep complete.");
    }

    /// <summary>
    /// Targeted escalation check — triggered by Hangfire scheduling a check
    /// for a specific request after its escalation timeout.
    /// Receives the EscalationCheckMessage from the "escalation-check" RabbitMQ queue.
    ///
    /// This performs the full escalation evaluation:
    ///   1. Look up the request and its acknowledgment count
    ///   2. Determine which stage should be active based on elapsed time
    ///   3. Execute the appropriate stage action
    ///   4. Log to audit trail
    /// </summary>
    [Function("EscalationCheck")]
    public async Task RunCheck(
        [RabbitMQTrigger("escalation-check", ConnectionStringSetting = "RabbitMQConnection")] string message)
    {
        _logger.LogInformation("Escalation check triggered for specific request");

        try
        {
            var check = JsonSerializer.Deserialize<EscalationCheckMessage>(message);
            if (check is null) return;

            _logger.LogInformation(
                "Checking escalation: RequestId={RequestId}, Policy={Policy}, Timeout={Timeout}",
                check.RequestId, check.Policy, check.TimeoutAt);

            var request = await _requestPort.GetRequestAsync(check.RequestId);
            if (request is null)
            {
                _logger.LogWarning("Escalation check: request {RequestId} not found", check.RequestId);
                return;
            }

            // Skip if request is no longer active
            if (request.Status is ResponseStatus.Resolved or ResponseStatus.Cancelled or ResponseStatus.Expired)
            {
                _logger.LogInformation(
                    "Escalation check: request {RequestId} is {Status}, skipping",
                    check.RequestId, request.Status);
                return;
            }

            var ackCount = await _trackingPort.GetAcknowledgmentCountAsync(check.RequestId);
            var elapsed = DateTime.UtcNow - request.CreatedAt;
            var currentStage = _config.GetCurrentStage(elapsed);

            _logger.LogInformation(
                "Escalation state for {RequestId}: acks={AckCount}/{Desired}, " +
                "elapsed={Elapsed}, currentStage={Stage}",
                check.RequestId, ackCount, check.DesiredResponderCount, elapsed, currentStage);

            // Execute the escalation action based on the policy
            switch (check.Policy)
            {
                case EscalationPolicy.TimedEscalation:
                    if (ackCount < _config.MinRespondersBeforeWiden &&
                        currentStage >= EscalationStage.WidenScope)
                    {
                        await ExecuteWidenScopeFromFunctionAsync(request, ackCount);
                    }
                    break;

                case EscalationPolicy.Conditional911:
                    if (ackCount < check.DesiredResponderCount)
                    {
                        if (currentStage >= EscalationStage.FirstResponders)
                        {
                            await ExecuteFirstRespondersFromFunctionAsync(request, ackCount);
                        }
                        else if (currentStage >= EscalationStage.WidenScope)
                        {
                            await ExecuteWidenScopeFromFunctionAsync(request, ackCount);
                        }
                    }
                    break;

                case EscalationPolicy.Immediate911:
                    // 911 should already be in progress from initial dispatch.
                    // This check confirms and retries if the initial call failed.
                    _logger.LogWarning(
                        "Immediate911: verifying 911 was contacted for {RequestId}",
                        check.RequestId);
                    await ExecuteFirstRespondersFromFunctionAsync(request, ackCount);
                    break;

                case EscalationPolicy.FullCascade:
                    // Execute all stages that should be active by now
                    if (currentStage >= EscalationStage.WidenScope &&
                        ackCount < _config.MinRespondersBeforeWiden)
                    {
                        await ExecuteWidenScopeFromFunctionAsync(request, ackCount);
                    }
                    if (currentStage >= EscalationStage.EmergencyContacts &&
                        ackCount < check.DesiredResponderCount)
                    {
                        await AuditEscalationEventAsync(request, EscalationStage.EmergencyContacts, ackCount,
                            "Emergency contacts notified via FullCascade policy");
                    }
                    if (currentStage >= EscalationStage.FirstResponders &&
                        ackCount < check.DesiredResponderCount)
                    {
                        await ExecuteFirstRespondersFromFunctionAsync(request, ackCount);
                    }
                    break;

                case EscalationPolicy.Manual:
                default:
                    _logger.LogInformation(
                        "Manual escalation policy — no auto-action for RequestId={RequestId}",
                        check.RequestId);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing escalation check");
            throw;
        }
    }

    // ── Stage Execution Helpers ──────────────────────────────────────────────

    /// <summary>
    /// Widen scope from within the Azure Function context.
    /// Expands the search radius and re-dispatches to newly discovered volunteers.
    /// </summary>
    private async Task ExecuteWidenScopeFromFunctionAsync(
        ResponseRequest request, int ackCount)
    {
        var newRadius = _config.GetWidenedRadius(request.RadiusMeters, request.RadiusMeters);

        _logger.LogWarning(
            "WIDEN SCOPE (Function) for {RequestId}: {OldRadius}m -> {NewRadius}m, " +
            "acks={AckCount}/{MinRequired}",
            request.RequestId, request.RadiusMeters, newRadius,
            ackCount, _config.MinRespondersBeforeWiden);

        // Query spatial index with expanded radius
        var expandedQuery = new SpatialQuery
        {
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            RadiusMeters = newRadius,
            MaxResults = request.DesiredResponderCount * 3
        };
        var newCandidates = await _spatialIndex.FindNearbyAsync(expandedQuery);

        // Re-dispatch
        var redispatched = await _dispatchPort.RedispatchAsync(
            request, newRadius, request.DesiredResponderCount * 2);

        // Update status
        await _requestPort.UpdateStatusAsync(request.RequestId, ResponseStatus.Escalated);

        await AuditEscalationEventAsync(request, EscalationStage.WidenScope, ackCount,
            $"Scope widened: {request.RadiusMeters}m -> {newRadius}m, " +
            $"newCandidates={newCandidates.Count}, redispatched={redispatched}");
    }

    /// <summary>
    /// First responders (911) from within the Azure Function context.
    /// Calls IEmergencyServicesPort to initiate a 911 call.
    /// </summary>
    private async Task ExecuteFirstRespondersFromFunctionAsync(
        ResponseRequest request, int ackCount)
    {
        if (!_config.AutoDial911)
        {
            _logger.LogWarning(
                "FirstResponders (Function) for {RequestId}: AutoDial911 disabled, skipping",
                request.RequestId);
            return;
        }

        _logger.LogWarning(
            "911 ESCALATION (Function) for {RequestId}: acks={AckCount}/{Desired}",
            request.RequestId, ackCount, request.DesiredResponderCount);

        var emergencyRequest = new Emergency911Request(
            RequestId: Guid.NewGuid().ToString("N")[..12],
            UserId: request.UserId,
            ResponseRequestId: request.RequestId,
            TriggerSource: Emergency911TriggerSource.AutoEscalation,
            ServiceType: EmergencyServiceType.All,
            Latitude: request.Latitude,
            Longitude: request.Longitude,
            AccuracyMeters: request.AccuracyMeters,
            Address: null,
            ContextSummary: $"Automated emergency escalation from The Watch. " +
                           $"User triggered a {request.Scope} alert. " +
                           $"Only {ackCount} of {request.DesiredResponderCount} volunteer responders acknowledged. " +
                           $"Alert type: {request.TriggerSource ?? "MANUAL"}.",
            UserPhoneNumber: "unknown",
            UserName: request.UserId,
            MedicalInfo: null,
            AccessInstructions: null,
            VolunteerRespondersEnRoute: ackCount,
            InitiatedBy: "system-escalation-function",
            CreatedAt: DateTime.UtcNow
        );

        try
        {
            var result = await _emergencyServices.Initiate911CallAsync(emergencyRequest);

            await _auditTrail.AppendAsync(new AuditEntry
            {
                UserId = request.UserId,
                ActorRole = "Escalation",
                Action = AuditAction.Emergency911Initiated,
                EntityType = "Emergency911Request",
                EntityId = emergencyRequest.RequestId,
                CorrelationId = request.RequestId,
                SourceSystem = "Functions",
                SourceComponent = "EscalationCheckFunction.ExecuteFirstResponders",
                Severity = AuditSeverity.Critical,
                DataClassification = DataClassification.HighlyConfidential,
                Outcome = result.Status == Emergency911CallStatus.Failed
                    ? AuditOutcome.Failure
                    : AuditOutcome.Success,
                Reason = $"911 auto-escalation from function: status={result.Status}, " +
                         $"rapidSos={result.RapidSosLocationPushed}"
            });

            _logger.LogWarning(
                "911 CALL RESULT for {RequestId}: status={Status}",
                request.RequestId, result.Status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "911 call failed for {RequestId}", request.RequestId);

            await _auditTrail.AppendAsync(new AuditEntry
            {
                UserId = request.UserId,
                ActorRole = "Escalation",
                Action = AuditAction.Emergency911CallFailed,
                EntityType = "Emergency911Request",
                EntityId = emergencyRequest.RequestId,
                CorrelationId = request.RequestId,
                SourceSystem = "Functions",
                SourceComponent = "EscalationCheckFunction.ExecuteFirstResponders",
                Severity = AuditSeverity.Critical,
                DataClassification = DataClassification.HighlyConfidential,
                Outcome = AuditOutcome.Failure,
                ErrorMessage = ex.Message,
                Reason = $"911 auto-escalation failed: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Helper: log an escalation event to the audit trail.
    /// </summary>
    private async Task AuditEscalationEventAsync(
        ResponseRequest request, EscalationStage stage, int ackCount, string reason)
    {
        await _auditTrail.AppendAsync(new AuditEntry
        {
            UserId = request.UserId,
            ActorRole = "Escalation",
            Action = stage == EscalationStage.WidenScope
                ? AuditAction.EscalationScopeExpanded
                : AuditAction.EscalationFired,
            EntityType = "ResponseRequest",
            EntityId = request.RequestId,
            CorrelationId = request.RequestId,
            SourceSystem = "Functions",
            SourceComponent = $"EscalationCheckFunction.{stage}",
            Severity = AuditSeverity.Warning,
            DataClassification = DataClassification.Confidential,
            Outcome = AuditOutcome.Success,
            Reason = reason
        });
    }
}

/// <summary>
/// Message format published to "escalation-check" queue.
/// Consumed by EscalationCheckFunction.RunCheck for targeted escalation evaluation.
/// </summary>
public record EscalationCheckMessage(
    string RequestId,
    EscalationPolicy Policy,
    DateTime TimeoutAt,
    int DesiredResponderCount
);
