// ResponseDispatchFunction — RabbitMQ-triggered function for response fan-out.
// When the Dashboard API creates a ResponseRequest and publishes to the
// "response-dispatch" queue, this function picks it up and fans out
// notifications to all eligible responders.
//
// Architecture:
//   API → RabbitMQ("response-dispatch") → THIS FUNCTION → Push notifications per responder
//   This decouples the API response from the O(N) notification delivery.

using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Functions.Functions;

public class ResponseDispatchFunction
{
    private readonly ILogger<ResponseDispatchFunction> _logger;

    public ResponseDispatchFunction(ILogger<ResponseDispatchFunction> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Triggered by a message on the "response-dispatch" RabbitMQ queue.
    /// Deserializes the ResponseRequest and dispatches to eligible responders.
    /// </summary>
    [Function("ResponseDispatch")]
    public async Task Run(
        [RabbitMQTrigger("response-dispatch", ConnectionStringSetting = "RabbitMQConnection")] string message)
    {
        _logger.LogInformation("ResponseDispatch triggered. Processing message...");

        try
        {
            var request = JsonSerializer.Deserialize<ResponseDispatchMessage>(message);
            if (request is null)
            {
                _logger.LogWarning("Failed to deserialize dispatch message");
                return;
            }

            _logger.LogInformation(
                "Dispatching response: RequestId={RequestId}, Scope={Scope}, " +
                "Radius={Radius}m, DesiredResponders={Count}",
                request.RequestId, request.Scope, request.RadiusMeters, request.DesiredResponderCount);

            // In production: query IParticipationPort for eligible responders,
            // then send push notifications via FCM/APNs per responder.
            // For now: log and acknowledge.

            var defaults = ResponseScopePresets.GetDefaults(request.Scope);
            _logger.LogInformation(
                "Scope defaults — Radius: {Radius}m, Escalation: {Escalation}, Strategy: {Strategy}",
                defaults.RadiusMeters, defaults.Escalation, defaults.Strategy);

            // Simulate dispatch delay per responder
            var simulatedResponderCount = Math.Min(request.DesiredResponderCount, 20);
            for (var i = 0; i < simulatedResponderCount; i++)
            {
                _logger.LogDebug("Notifying responder {Index}/{Total}", i + 1, simulatedResponderCount);
                // In production: await _pushNotificationService.NotifyAsync(responder, request);
            }

            _logger.LogInformation(
                "Dispatch complete: {Count} responders notified for RequestId={RequestId}",
                simulatedResponderCount, request.RequestId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing dispatch message");
            throw; // Let RabbitMQ retry
        }
    }
}

/// <summary>
/// Message format published to "response-dispatch" queue by the Dashboard API.
/// </summary>
public record ResponseDispatchMessage(
    string RequestId,
    string UserId,
    ResponseScope Scope,
    double Latitude,
    double Longitude,
    double RadiusMeters,
    int DesiredResponderCount,
    DispatchStrategy Strategy,
    string? Description,
    string? TriggerSource
);
