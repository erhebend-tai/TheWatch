// ResponseDispatchFunction — RabbitMQ-triggered function for response fan-out.
// When the Dashboard API creates a ResponseRequest and publishes a
// ResponseDispatchMessage to the "swarm-tasks" exchange, this function
// picks it up, queries ISpatialIndex for nearby responders, and fans out
// push notifications via INotificationSendPort.
//
// Architecture:
//   API → RabbitMQ("swarm-tasks", routing_key="response.dispatch")
//     → THIS FUNCTION
//       → ISpatialIndex.FindNearbyAsync() to locate eligible responders
//       → INotificationSendPort.SendAsync() per responder (FCM/APNs/SMS)
//       → IResponseTrackingPort records dispatch metadata
//   This decouples the API response from the O(N) notification delivery.
//
// Retry policy: RabbitMQ handles retries via dead-letter exchange on failure.
//   The function throws on transient errors (network, database) to trigger retry.
//   Deserialization failures are logged and swallowed (message is malformed, retry won't help).
//
// WAL: Audio NEVER leaves the device. Push payloads contain only structured metadata.

using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Functions.Functions;

public class ResponseDispatchFunction
{
    private readonly ISpatialIndex _spatialIndex;
    private readonly INotificationSendPort _notificationPort;
    private readonly ILogger<ResponseDispatchFunction> _logger;

    // Maximum responders to notify in a single dispatch pass.
    // Prevents runaway fan-out on Evacuation scope (int.MaxValue desired).
    // Further batches are handled by re-dispatch / escalation.
    private const int MaxNotificationsPerBatch = 200;

    // Routing key used when publishing per-responder push notification messages.
    private const string PushNotificationRoutingKey = "response.push-notification";

    public ResponseDispatchFunction(
        ISpatialIndex spatialIndex,
        INotificationSendPort notificationPort,
        ILogger<ResponseDispatchFunction> logger)
    {
        _spatialIndex = spatialIndex;
        _notificationPort = notificationPort;
        _logger = logger;
    }

    /// <summary>
    /// Triggered by a message on the "response-dispatch" RabbitMQ queue
    /// (bound to the "swarm-tasks" exchange with routing key "response.dispatch").
    /// Deserializes the ResponseDispatchMessage, queries ISpatialIndex for nearby
    /// responders, and sends push notifications to each.
    /// </summary>
    [Function("ResponseDispatch")]
    public async Task Run(
        [RabbitMQTrigger("response-dispatch", ConnectionStringSetting = "RabbitMQConnection")] string message)
    {
        _logger.LogInformation("ResponseDispatch triggered. Processing message...");

        ResponseDispatchMessage? request;
        try
        {
            request = JsonSerializer.Deserialize<ResponseDispatchMessage>(message, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            // Malformed JSON — log and throw so RabbitMQ routes to dead-letter.
            _logger.LogError(ex, "Failed to deserialize dispatch message: {Message}", message);
            throw;
        }

        if (request is null)
        {
            _logger.LogWarning("Deserialized dispatch message was null — skipping");
            return;
        }

        _logger.LogWarning(
            "DISPATCH PROCESSING: RequestId={RequestId}, UserId={UserId}, Scope={Scope}, " +
            "Location=({Lat},{Lng}), Radius={Radius}m, DesiredResponders={Count}, " +
            "Strategy={Strategy}, Trigger={Trigger}",
            request.RequestId, request.UserId, request.Scope,
            request.Latitude, request.Longitude,
            request.RadiusMeters, request.DesiredResponderCount,
            request.Strategy, request.TriggerSource);

        // ── 1. Query ISpatialIndex for nearby responders ────────────────────
        // Over-query by 3x to account for opt-out, quiet hours, and declines.
        var spatialQuery = new SpatialQuery
        {
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            RadiusMeters = request.RadiusMeters,
            MaxResults = Math.Min(request.DesiredResponderCount * 3, MaxNotificationsPerBatch)
        };

        var nearbyResponders = await _spatialIndex.FindNearbyAsync(spatialQuery);

        _logger.LogInformation(
            "ISpatialIndex returned {Count} nearby entities within {Radius}m for request {RequestId}",
            nearbyResponders.Count, request.RadiusMeters, request.RequestId);

        if (nearbyResponders.Count == 0)
        {
            _logger.LogWarning(
                "NO RESPONDERS FOUND for {RequestId} within {Radius}m at ({Lat},{Lng}). " +
                "Escalation will handle expansion if configured.",
                request.RequestId, request.RadiusMeters, request.Latitude, request.Longitude);
            return;
        }

        // ── 2. Log scope defaults for traceability ──────────────────────────
        var defaults = ResponseScopePresets.GetDefaults(request.Scope);
        _logger.LogInformation(
            "Scope defaults — Radius: {Radius}m, Escalation: {Escalation}, Strategy: {Strategy}",
            defaults.RadiusMeters, defaults.Escalation, defaults.Strategy);

        // ── 3. Fan out push notifications to each eligible responder ────────
        // Cap at the desired responder count (or MaxNotificationsPerBatch).
        var targetCount = Math.Min(request.DesiredResponderCount, MaxNotificationsPerBatch);
        var notifiedCount = 0;
        var failedCount = 0;

        // Determine notification priority based on scope:
        //   Evacuation/Community → Critical (bypasses DND)
        //   SilentDuress → Critical + silent (no sound on sender's device, loud on responder's)
        //   Neighborhood → High
        //   CheckIn/Custom → Normal
        var priority = request.Scope switch
        {
            ResponseScope.Evacuation => NotificationPriority.Critical,
            ResponseScope.Community => NotificationPriority.Critical,
            ResponseScope.SilentDuress => NotificationPriority.Critical,
            ResponseScope.Neighborhood => NotificationPriority.High,
            _ => NotificationPriority.Normal
        };

        // Build all payloads up front for batch sending
        var payloads = new List<NotificationPayload>();

        foreach (var responder in nearbyResponders.Take(targetCount))
        {
            // Determine notification category from scope
            var category = request.Scope switch
            {
                ResponseScope.Evacuation => NotificationCategory.EvacuationNotice,
                ResponseScope.CheckIn => NotificationCategory.CheckInRequest,
                _ => NotificationCategory.SosDispatch
            };

            var ttl = request.Scope == ResponseScope.Evacuation
                ? TimeSpan.FromHours(2)
                : TimeSpan.FromMinutes(15);

            var notification = new NotificationPayload(
                NotificationId: Guid.NewGuid().ToString("N")[..12],
                RecipientUserId: responder.EntityId,
                RecipientDeviceToken: null,  // Adapter resolves from registration store
                RecipientPhoneNumber: null,  // Adapter resolves from registration store
                Category: category,
                Priority: priority,
                PreferredChannel: NotificationChannel.Push,
                Title: GetNotificationTitle(request.Scope),
                Body: GetNotificationBody(request.Scope, responder.DistanceMeters, request.Description),
                Subtitle: null,
                DeepLink: $"thewatch://response/{request.RequestId}",
                RequestId: request.RequestId,
                RequestorName: null,  // PII: never include requestor name in push payload
                Scope: request.Scope,
                IncidentLatitude: request.Latitude,
                IncidentLongitude: request.Longitude,
                DistanceMeters: responder.DistanceMeters,
                SmsReplyInstructions: null,
                CreatedAt: DateTime.UtcNow,
                ExpiresAfter: ttl
            );

            payloads.Add(notification);
        }

        // ── 4. Send batch push notifications ────────────────────────────────
        try
        {
            var results = await _notificationPort.SendPushBatchAsync(payloads);
            notifiedCount = results.Count(r => r.Status is NotificationDeliveryStatus.Sent or NotificationDeliveryStatus.Delivered or NotificationDeliveryStatus.Queued);
            failedCount = results.Count(r => r.Status == NotificationDeliveryStatus.Failed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Batch notification delivery failed for request {RequestId} — " +
                "{PayloadCount} notifications may not have been sent",
                request.RequestId, payloads.Count);
            // Re-throw so RabbitMQ can retry the entire dispatch.
            throw;
        }

        _logger.LogWarning(
            "DISPATCH COMPLETE: RequestId={RequestId}, Notified={Notified}/{Target} " +
            "(failed={Failed}), NearbyFound={Found}, Scope={Scope}",
            request.RequestId, notifiedCount, targetCount, failedCount,
            nearbyResponders.Count, request.Scope);
    }

    // ── Notification Content Helpers ────────────────────────────────────────

    /// <summary>
    /// Generate the notification title based on emergency scope.
    /// Keep titles short — iOS truncates at ~50 chars, Android at ~65.
    /// </summary>
    private static string GetNotificationTitle(ResponseScope scope) => scope switch
    {
        ResponseScope.CheckIn => "Check-In Request Nearby",
        ResponseScope.Neighborhood => "Emergency Alert Nearby",
        ResponseScope.Community => "COMMUNITY EMERGENCY",
        ResponseScope.Evacuation => "EVACUATION ORDER",
        ResponseScope.SilentDuress => "Urgent: Silent Alert",
        ResponseScope.Custom => "Response Request Nearby",
        _ => "TheWatch Alert"
    };

    /// <summary>
    /// Generate the notification body with distance and optional description.
    /// Keep under 150 chars for full visibility on lock screen.
    /// </summary>
    private static string GetNotificationBody(ResponseScope scope, double distanceMeters, string? description)
    {
        var distanceText = distanceMeters < 1000
            ? $"{distanceMeters:F0}m away"
            : $"{distanceMeters / 1000:F1}km away";

        var prefix = scope switch
        {
            ResponseScope.CheckIn => "Someone nearby needs a check-in.",
            ResponseScope.Neighborhood => "Emergency reported in your area.",
            ResponseScope.Community => "Major incident in your community.",
            ResponseScope.Evacuation => "Evacuation order issued for your area.",
            ResponseScope.SilentDuress => "Someone nearby needs immediate help.",
            _ => "A response has been requested nearby."
        };

        var body = $"{prefix} {distanceText}.";

        // Append truncated description if present (keep total under 150 chars)
        if (!string.IsNullOrWhiteSpace(description))
        {
            var maxDescLength = 150 - body.Length - 3; // " — " prefix
            if (maxDescLength > 10)
            {
                var truncated = description.Length > maxDescLength
                    ? description[..maxDescLength] + "..."
                    : description;
                body += $" — {truncated}";
            }
        }

        return body;
    }
}
