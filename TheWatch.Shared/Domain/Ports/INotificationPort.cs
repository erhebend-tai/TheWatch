// =============================================================================
// INotificationPort — port interfaces for push notifications and SMS delivery.
// =============================================================================
// Handles TWO delivery channels:
//   1. Push Notifications (FCM for Android, APNs for iOS) with actionable buttons
//   2. SMS/MMS for fallback delivery and users who don't have the app installed
//
// Response flow:
//   Dispatch → Push Notification with Accept/Decline actions
//            → SMS with reply codes (Y/N)
//            → User taps Accept or replies "Y"
//            → Response routed back to IResponseTrackingPort.AcknowledgeAsync()
//
// All notification content is constructed here in Shared (platform-agnostic).
// Adapters handle the actual delivery via FCM/APNs/Twilio/etc.
//
// WAL: Audio NEVER leaves the device. Notifications contain zero audio data.
//      Only structured metadata (who needs help, where, what type).
// =============================================================================

namespace TheWatch.Shared.Domain.Ports;

// ═══════════════════════════════════════════════════════════════
// Notification Models
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// The delivery channel for a notification.
/// </summary>
public enum NotificationChannel
{
    Push,       // FCM (Android) or APNs (iOS)
    Sms,        // Text message via SMS gateway (Twilio, Azure Communication Services, etc.)
    InApp,      // In-app banner/toast (SignalR)
    Email,      // Email (low-priority, post-incident reports)

    /// <summary>
    /// Voice call to landline or mobile — TTS message with DTMF response menu.
    /// "Press 1 to accept, 2 to decline, 9 to call 911."
    /// Delivered via Twilio Programmable Voice, Vonage Voice API, or Bandwidth.com.
    /// Critical for reaching users who only have landline phones.
    /// </summary>
    VoiceCall,

    /// <summary>
    /// Alarm panel notification — text/chime/siren on alarm keypad.
    /// Routed via Alarm.com API, SIA DC-07, or Contact ID reverse channel.
    /// </summary>
    AlarmPanel
}

/// <summary>
/// Priority level — maps to platform-specific priority flags.
/// Critical bypasses Do Not Disturb on both platforms.
/// </summary>
public enum NotificationPriority
{
    Low,        // Informational — post-incident updates
    Normal,     // Standard — check-in requests
    High,       // Urgent — neighborhood emergency
    Critical    // Life-safety — bypasses DND, plays alarm sound
}

/// <summary>
/// Category of notification — determines which action buttons are shown.
/// </summary>
public enum NotificationCategory
{
    SosDispatch,            // "Someone needs help" → Accept / Decline
    SosUpdate,              // Status update for an active response → View
    SosCancelled,           // Response was cancelled → Dismiss
    SosResolved,            // Response was resolved → View Summary
    EscalationAlert,        // Escalation triggered → Accept / Decline / Call 911
    CheckInRequest,         // Neighborhood check-in → I'm OK / Need Help
    EvacuationNotice,       // Evacuation order → Acknowledge / Need Assistance
    ResponderLocationUpdate // Responder en route → View Map
}

/// <summary>
/// A notification to be delivered to a user via one or more channels.
/// Platform-agnostic — adapters translate this to FCM/APNs/SMS payloads.
/// </summary>
public record NotificationPayload(
    string NotificationId,
    string RecipientUserId,
    string? RecipientDeviceToken,   // FCM/APNs device token (null for SMS-only)
    string? RecipientPhoneNumber,   // For SMS delivery (null for push-only)

    NotificationCategory Category,
    NotificationPriority Priority,
    NotificationChannel PreferredChannel,

    string Title,                   // "Emergency Alert" / "Check-In Request"
    string Body,                    // "Jane D. needs help at 123 Main St"
    string? Subtitle,               // iOS subtitle

    // Deep link — opens the app to the response detail screen
    string? DeepLink,               // "thewatch://response/{requestId}"

    // Payload data — structured metadata for the app to process
    string? RequestId,              // Links back to the ResponseRequest
    string? RequestorName,          // Who triggered the SOS
    ResponseScope? Scope,
    double? IncidentLatitude,
    double? IncidentLongitude,
    double? DistanceMeters,         // How far the responder is from the incident

    // SMS-specific
    string? SmsReplyInstructions,   // "Reply Y to accept, N to decline"

    // Timing
    DateTime CreatedAt,
    TimeSpan? ExpiresAfter          // Notification expires after this duration
);

/// <summary>
/// Result of sending a notification — tracks delivery status per channel.
/// </summary>
public record NotificationResult(
    string NotificationId,
    string RecipientUserId,
    NotificationChannel Channel,
    NotificationDeliveryStatus Status,
    string? ExternalMessageId,      // FCM message ID, Twilio SID, etc.
    string? ErrorMessage,
    DateTime SentAt
);

public enum NotificationDeliveryStatus
{
    Queued,         // Accepted by our system, not yet sent to provider
    Sent,           // Sent to FCM/APNs/Twilio
    Delivered,      // Delivery confirmed (FCM receipt, Twilio delivered callback)
    Read,           // User opened the notification
    Failed,         // Delivery failed (invalid token, unreachable number)
    Expired         // TTL expired before delivery
}

/// <summary>
/// An inbound response to a notification — the user's yes/no answer.
/// Can come from: push notification action button, SMS reply, or in-app tap.
/// </summary>
public record NotificationResponse(
    string ResponseId,
    string NotificationId,
    string RequestId,
    string ResponderId,
    NotificationResponseAction Action,
    NotificationChannel SourceChannel,  // Where did the response come from
    string? RawSmsBody,                 // The raw SMS text if source is SMS
    double? ResponderLatitude,          // Current location at time of response
    double? ResponderLongitude,
    DateTime RespondedAt
);

public enum NotificationResponseAction
{
    Accept,         // "I'm on my way" / "Y"
    Decline,        // "I can't help" / "N"
    Acknowledge,    // "I see it" (for info-only notifications)
    NeedHelp,       // "I need help too" (for check-in requests)
    ImOk,           // "I'm OK" (for check-in requests)
    Call911,        // "Calling 911" (escalation action)
    ViewDetails     // Opened the notification (implicit read)
}

/// <summary>
/// A user's notification preferences and registered device tokens.
/// </summary>
public record UserNotificationProfile(
    string UserId,
    string? DisplayName,
    string? PhoneNumber,            // For SMS fallback
    bool SmsEnabled,                // User has opted into SMS notifications
    bool PushEnabled,               // User has opted into push notifications
    IReadOnlyList<DeviceRegistration> Devices,
    NotificationPriority MinimumPriority,   // Don't send below this priority
    DateTime LastUpdated
);

public record DeviceRegistration(
    string DeviceId,
    string DeviceToken,             // FCM or APNs token
    DevicePlatform Platform,
    string? DeviceName,             // "Barton's iPhone", "Pixel 8"
    DateTime RegisteredAt,
    DateTime LastSeenAt,
    bool IsActive
);

public enum DevicePlatform
{
    Android,
    iOS,

    /// <summary>
    /// Landline phone via ATA/SIP bridge — notifications delivered as synthesized voice calls
    /// (TTS over SIP) or DTMF menu prompts. Cannot receive push notifications.
    /// </summary>
    LandlinePhone,

    /// <summary>
    /// Alarm system keypad/panel — notifications delivered as panel display text,
    /// chime patterns, or siren activation. Routed via Alarm.com API or SIA DC-07.
    /// </summary>
    AlarmPanel
}

// ═══════════════════════════════════════════════════════════════
// Port Interfaces
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Port for sending push notifications to mobile devices.
/// Adapters: MockNotificationAdapter (dev), FcmAdapter + ApnsAdapter (prod).
/// </summary>
public interface INotificationSendPort
{
    /// <summary>
    /// Send a notification via push (FCM/APNs).
    /// Returns the delivery result.
    /// </summary>
    Task<NotificationResult> SendPushAsync(
        NotificationPayload payload,
        CancellationToken ct = default);

    /// <summary>
    /// Send notifications to multiple recipients in batch.
    /// Used for SOS dispatch fan-out.
    /// </summary>
    Task<IReadOnlyList<NotificationResult>> SendPushBatchAsync(
        IReadOnlyList<NotificationPayload> payloads,
        CancellationToken ct = default);

    /// <summary>
    /// Cancel/retract a previously sent notification (e.g., SOS cancelled).
    /// Not all platforms support this — best-effort.
    /// </summary>
    Task<bool> CancelNotificationAsync(
        string notificationId,
        string recipientUserId,
        CancellationToken ct = default);
}

/// <summary>
/// Port for sending and receiving SMS messages.
/// Adapters: MockSmsAdapter (dev), TwilioAdapter or AzureCommServicesAdapter (prod).
/// </summary>
public interface ISmsPort
{
    /// <summary>
    /// Send an SMS to a phone number.
    /// For SOS dispatch: includes reply instructions ("Reply Y to accept, N to decline").
    /// </summary>
    Task<NotificationResult> SendSmsAsync(
        string phoneNumber,
        string message,
        string? requestId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Send SMS to multiple recipients in batch.
    /// </summary>
    Task<IReadOnlyList<NotificationResult>> SendSmsBatchAsync(
        IReadOnlyList<(string PhoneNumber, string Message, string? RequestId)> messages,
        CancellationToken ct = default);

    /// <summary>
    /// Process an inbound SMS reply (webhook from Twilio/Azure Comm Services).
    /// Parses the reply and returns a structured NotificationResponse.
    /// </summary>
    Task<NotificationResponse?> ProcessInboundSmsAsync(
        string fromNumber,
        string body,
        string? toNumber = null,
        CancellationToken ct = default);
}

/// <summary>
/// Port for managing user device tokens and notification preferences.
/// </summary>
public interface INotificationRegistrationPort
{
    /// <summary>Register or update a device token for push notifications.</summary>
    Task<DeviceRegistration> RegisterDeviceAsync(
        string userId,
        string deviceToken,
        DevicePlatform platform,
        string? deviceName = null,
        CancellationToken ct = default);

    /// <summary>Remove a device registration (user logged out, token expired).</summary>
    Task<bool> UnregisterDeviceAsync(
        string userId,
        string deviceId,
        CancellationToken ct = default);

    /// <summary>Get all registered devices for a user.</summary>
    Task<IReadOnlyList<DeviceRegistration>> GetDevicesAsync(
        string userId,
        CancellationToken ct = default);

    /// <summary>Get or create a user's notification profile.</summary>
    Task<UserNotificationProfile> GetProfileAsync(
        string userId,
        CancellationToken ct = default);

    /// <summary>Update notification preferences (SMS enabled, minimum priority, etc.).</summary>
    Task<UserNotificationProfile> UpdateProfileAsync(
        UserNotificationProfile profile,
        CancellationToken ct = default);
}

/// <summary>
/// Port for tracking notification delivery and responses.
/// Used by the dashboard to show delivery status and response rates.
/// </summary>
public interface INotificationTrackingPort
{
    /// <summary>Record a delivery result.</summary>
    Task RecordDeliveryAsync(NotificationResult result, CancellationToken ct = default);

    /// <summary>Record an inbound response (accept/decline from any channel).</summary>
    Task RecordResponseAsync(NotificationResponse response, CancellationToken ct = default);

    /// <summary>Get all delivery results for a request (how many were sent, delivered, failed).</summary>
    Task<IReadOnlyList<NotificationResult>> GetDeliveryResultsAsync(
        string requestId,
        CancellationToken ct = default);

    /// <summary>Get all responses for a request (how many accepted, declined).</summary>
    Task<IReadOnlyList<NotificationResponse>> GetResponsesAsync(
        string requestId,
        CancellationToken ct = default);
}
