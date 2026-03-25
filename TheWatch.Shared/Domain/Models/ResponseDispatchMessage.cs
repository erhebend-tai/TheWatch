// ResponseDispatchMessage — the canonical message published to RabbitMQ when an SOS
// response is created. Consumed by ResponseDispatchFunction (Azure Functions) which
// fans out push notifications to eligible responders.
//
// Published to exchange: "swarm-tasks" (defined in Bicep/Aspire infrastructure)
// Routing key: "response.dispatch"
//
// Flow:
//   Dashboard API (CreateResponseAsync) → RabbitMQ "swarm-tasks" exchange
//     → ResponseDispatchFunction consumes → ISpatialIndex.FindNearbyAsync()
//     → INotificationSendPort per eligible responder
//
// Example:
//   var msg = new ResponseDispatchMessage(
//       RequestId: "a1b2c3d4e5f6",
//       UserId: "user-42",
//       Scope: ResponseScope.CheckIn,
//       Latitude: 30.2672,
//       Longitude: -97.7431,
//       RadiusMeters: 1000,
//       DesiredResponderCount: 8,
//       Strategy: DispatchStrategy.NearestN,
//       Description: "Feeling unsafe walking home",
//       TriggerSource: "PHRASE",
//       CreatedAt: DateTime.UtcNow);
//
// WAL: This record lives in Shared so both the publisher (Dashboard.Api) and
//      consumer (Functions) reference the same type. Never duplicate message contracts.

using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Shared.Domain.Models;

/// <summary>
/// Message published to the "swarm-tasks" RabbitMQ exchange when a new SOS response
/// is created. The ResponseDispatchFunction consumes this to find nearby responders
/// via ISpatialIndex and fan out push notifications via INotificationSendPort.
///
/// All fields are immutable — once published, the message is a snapshot of the
/// request state at creation time. Subsequent status changes (escalation, cancellation)
/// are published as separate message types.
/// </summary>
public record ResponseDispatchMessage(
    /// <summary>Unique ID of the response request (12-char hex).</summary>
    string RequestId,

    /// <summary>User who triggered the SOS.</summary>
    string UserId,

    /// <summary>Scope determines radius, responder count, escalation policy, dispatch strategy.</summary>
    ResponseScope Scope,

    /// <summary>Incident latitude (WGS-84).</summary>
    double Latitude,

    /// <summary>Incident longitude (WGS-84).</summary>
    double Longitude,

    /// <summary>Search radius for eligible responders, in meters.</summary>
    double RadiusMeters,

    /// <summary>Target number of responders to notify.</summary>
    int DesiredResponderCount,

    /// <summary>How responders are selected (NearestN, RadiusBroadcast, TrustedContactsOnly, etc.).</summary>
    DispatchStrategy Strategy,

    /// <summary>Optional human-readable description of the emergency.</summary>
    string? Description,

    /// <summary>What triggered this SOS: "PHRASE", "QUICK_TAP", "MANUAL_BUTTON", "WEARABLE", "FALL_DETECTION".</summary>
    string? TriggerSource,

    /// <summary>Timestamp when the response was created (UTC).</summary>
    DateTime CreatedAt
);

/// <summary>
/// Message published per-responder after ResponseDispatchFunction identifies eligible
/// responders. Consumed by the push notification delivery pipeline (FCM/APNs).
///
/// Example:
///   var pushMsg = new ResponderPushNotificationMessage(
///       RequestId: "a1b2c3d4e5f6",
///       ResponderId: "resp-001",
///       ResponderName: "Marcus Chen",
///       Channel: NotificationChannel.Push,
///       Scope: ResponseScope.CheckIn,
///       IncidentLatitude: 30.2672,
///       IncidentLongitude: -97.7431,
///       DistanceMeters: 312.5,
///       DispatchedAt: DateTime.UtcNow);
/// </summary>
public record ResponderPushNotificationMessage(
    /// <summary>Response request this notification belongs to.</summary>
    string RequestId,

    /// <summary>Target responder's user ID.</summary>
    string ResponderId,

    /// <summary>Responder's display name (for notification title).</summary>
    string ResponderName,

    /// <summary>Delivery channel (Push, Sms, InApp, VoiceCall, etc.).</summary>
    NotificationChannel Channel,

    /// <summary>Emergency scope (affects notification urgency/priority).</summary>
    ResponseScope Scope,

    /// <summary>Incident location latitude.</summary>
    double IncidentLatitude,

    /// <summary>Incident location longitude.</summary>
    double IncidentLongitude,

    /// <summary>Distance from responder to incident, in meters.</summary>
    double DistanceMeters,

    /// <summary>When this notification was dispatched (UTC).</summary>
    DateTime DispatchedAt
);
