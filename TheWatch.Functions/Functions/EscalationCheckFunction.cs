// EscalationCheckFunction — Timer-triggered function for escalation monitoring.
// Runs every 30 seconds, checks all active ResponseRequests for escalation conditions.
// Also receives targeted escalation checks via "escalation-check" RabbitMQ queue.
//
// Escalation logic:
//   1. Get all active requests past their escalation timeout
//   2. Check acknowledgment count vs desired responder count
//   3. If threshold not met, escalate per the request's EscalationPolicy
//   4. Publish escalation events back to RabbitMQ for redispatch

using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Functions.Functions;

public class EscalationCheckFunction
{
    private readonly ILogger<EscalationCheckFunction> _logger;

    public EscalationCheckFunction(ILogger<EscalationCheckFunction> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Periodic escalation sweep — checks all active requests every 30 seconds.
    /// </summary>
    [Function("EscalationSweep")]
    public async Task RunSweep(
        [TimerTrigger("*/30 * * * * *")] TimerInfo timerInfo)
    {
        _logger.LogDebug("Escalation sweep triggered at {Time}", DateTime.UtcNow);

        // In production:
        // 1. Query IResponseRequestPort for all Active/Dispatching requests
        // 2. For each, check if escalation timeout has elapsed
        // 3. Query IResponseTrackingPort for acknowledgment count
        // 4. If ack count < desired count AND timeout elapsed → escalate
        // 5. Escalation action depends on EscalationPolicy

        // For now: log sweep
        _logger.LogDebug("Escalation sweep complete. No active requests in mock mode.");
    }

    /// <summary>
    /// Targeted escalation check — triggered by Hangfire scheduling a check
    /// for a specific request after its escalation timeout.
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

            // In production:
            // var ackCount = await _trackingPort.GetAcknowledgmentCountAsync(check.RequestId);
            // var request = await _requestPort.GetRequestAsync(check.RequestId);
            // if (ackCount < request.DesiredResponderCount) { escalate(...) }

            // Escalation actions by policy:
            switch (check.Policy)
            {
                case EscalationPolicy.TimedEscalation:
                    _logger.LogWarning(
                        "TimedEscalation: Expanding radius for RequestId={RequestId}", check.RequestId);
                    // Double radius, re-dispatch
                    break;

                case EscalationPolicy.Conditional911:
                    _logger.LogWarning(
                        "Conditional911: Insufficient responders for RequestId={RequestId}, notifying 911",
                        check.RequestId);
                    // Notify 911 via integration
                    break;

                case EscalationPolicy.Immediate911:
                    _logger.LogWarning(
                        "Immediate911: Already in progress for RequestId={RequestId}", check.RequestId);
                    break;

                case EscalationPolicy.FullCascade:
                    _logger.LogWarning(
                        "FullCascade: Activating all tiers for RequestId={RequestId}", check.RequestId);
                    // Volunteers → 911 → emergency contacts → public broadcast
                    break;

                case EscalationPolicy.Manual:
                default:
                    _logger.LogInformation(
                        "Manual escalation policy — no auto-action for RequestId={RequestId}", check.RequestId);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing escalation check");
            throw;
        }
    }
}

public record EscalationCheckMessage(
    string RequestId,
    EscalationPolicy Policy,
    DateTime TimeoutAt,
    int DesiredResponderCount
);
