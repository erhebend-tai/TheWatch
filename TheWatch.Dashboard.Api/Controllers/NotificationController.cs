// NotificationController — REST endpoints for push notification management.
//
// Endpoints:
//   POST  /api/notifications/register-device     — Register a device token for push notifications
//   POST  /api/notifications/unregister-device    — Remove a device registration
//   GET   /api/notifications/devices/{userId}     — Get all registered devices for a user
//   GET   /api/notifications/profile/{userId}     — Get notification preferences
//   PUT   /api/notifications/profile              — Update notification preferences
//   POST  /api/notifications/send                 — Send a notification (admin/system use)
//   GET   /api/notifications/delivery/{requestId} — Get delivery results for a request
//   GET   /api/notifications/responses/{requestId} — Get user responses for a request
//
// WAL: Device token registration flow:
//   1. Mobile client obtains FCM/APNs token on startup
//   2. Client POSTs token to /register-device with userId and platform
//   3. Server stores via INotificationRegistrationPort
//   4. Token is used by INotificationSendPort when dispatching SOS alerts
//   5. On token refresh (FCM rotation), client re-registers with new token
//   6. On logout, client calls /unregister-device to remove the token
//
// NOTE: The actual push notification delivery happens in the dispatch pipeline
// (ResponseCoordinationService). This controller handles device management and
// provides query endpoints for delivery tracking.

using Microsoft.AspNetCore.Mvc;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Enums;

namespace TheWatch.Dashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NotificationController : ControllerBase
{
    private readonly INotificationRegistrationPort _registrationPort;
    private readonly INotificationSendPort _sendPort;
    private readonly INotificationTrackingPort _trackingPort;
    private readonly IAuditTrail _auditTrail;
    private readonly ILogger<NotificationController> _logger;

    public NotificationController(
        INotificationRegistrationPort registrationPort,
        INotificationSendPort sendPort,
        INotificationTrackingPort trackingPort,
        IAuditTrail auditTrail,
        ILogger<NotificationController> logger)
    {
        _registrationPort = registrationPort;
        _sendPort = sendPort;
        _trackingPort = trackingPort;
        _auditTrail = auditTrail;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────
    // Device Registration
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Register a device token for push notifications.
    /// Called by mobile clients on startup and on FCM/APNs token refresh.
    /// </summary>
    [HttpPost("register-device")]
    public async Task<IActionResult> RegisterDevice(
        [FromBody] RegisterDeviceRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
            return BadRequest(new { error = "UserId is required" });
        if (string.IsNullOrWhiteSpace(request.Token))
            return BadRequest(new { error = "Device token is required" });

        var platform = request.Platform?.ToLowerInvariant() switch
        {
            "android" => DevicePlatform.Android,
            "ios" => DevicePlatform.iOS,
            "landline" => DevicePlatform.LandlinePhone,
            "alarm" => DevicePlatform.AlarmPanel,
            _ => DevicePlatform.Android // Default to Android if not specified
        };

        var registration = await _registrationPort.RegisterDeviceAsync(
            request.UserId,
            request.Token,
            platform,
            request.DeviceName,
            ct);

        // Audit trail
        try
        {
            await _auditTrail.AppendAsync(new AuditEntry
            {
                UserId = request.UserId,
                Action = AuditAction.NotificationPreferencesUpdated,
                EntityType = "DeviceRegistration",
                EntityId = registration.DeviceId,
                SourceSystem = "Dashboard.Api",
                SourceComponent = "NotificationController",
                Severity = AuditSeverity.Info,
                DataClassification = DataClassification.Confidential,
                Outcome = AuditOutcome.Success,
                Reason = $"Device registered: {platform}, name={request.DeviceName}"
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to log audit for device registration");
        }

        _logger.LogInformation(
            "Device registered: UserId={UserId}, Platform={Platform}, DeviceId={DeviceId}",
            request.UserId, platform, registration.DeviceId);

        return Ok(new
        {
            registration.DeviceId,
            registration.DeviceToken,
            Platform = registration.Platform.ToString(),
            registration.DeviceName,
            registration.RegisteredAt,
            registration.IsActive
        });
    }

    /// <summary>
    /// Remove a device registration (user logged out, token expired).
    /// </summary>
    [HttpPost("unregister-device")]
    public async Task<IActionResult> UnregisterDevice(
        [FromBody] UnregisterDeviceRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
            return BadRequest(new { error = "UserId is required" });
        if (string.IsNullOrWhiteSpace(request.DeviceId))
            return BadRequest(new { error = "DeviceId is required" });

        var result = await _registrationPort.UnregisterDeviceAsync(request.UserId, request.DeviceId, ct);

        if (!result)
            return NotFound(new { error = $"Device '{request.DeviceId}' not found for user '{request.UserId}'" });

        _logger.LogInformation(
            "Device unregistered: UserId={UserId}, DeviceId={DeviceId}",
            request.UserId, request.DeviceId);

        return Ok(new { request.UserId, request.DeviceId, Status = "Unregistered" });
    }

    // ─────────────────────────────────────────────────────────────
    // Device Query
    // ─────────────────────────────────────────────────────────────

    /// <summary>Get all registered devices for a user.</summary>
    [HttpGet("devices/{userId}")]
    public async Task<IActionResult> GetDevices(string userId, CancellationToken ct)
    {
        var devices = await _registrationPort.GetDevicesAsync(userId, ct);
        return Ok(devices.Select(d => new
        {
            d.DeviceId,
            d.DeviceToken,
            Platform = d.Platform.ToString(),
            d.DeviceName,
            d.RegisteredAt,
            d.LastSeenAt,
            d.IsActive
        }));
    }

    // ─────────────────────────────────────────────────────────────
    // Notification Profile / Preferences
    // ─────────────────────────────────────────────────────────────

    /// <summary>Get a user's notification profile and preferences.</summary>
    [HttpGet("profile/{userId}")]
    public async Task<IActionResult> GetProfile(string userId, CancellationToken ct)
    {
        var profile = await _registrationPort.GetProfileAsync(userId, ct);
        return Ok(new
        {
            profile.UserId,
            profile.DisplayName,
            profile.PhoneNumber,
            profile.SmsEnabled,
            profile.PushEnabled,
            MinimumPriority = profile.MinimumPriority.ToString(),
            DeviceCount = profile.Devices.Count,
            profile.LastUpdated
        });
    }

    /// <summary>Update a user's notification preferences.</summary>
    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile(
        [FromBody] UpdateNotificationProfileRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
            return BadRequest(new { error = "UserId is required" });

        var existing = await _registrationPort.GetProfileAsync(request.UserId, ct);

        var priority = request.MinimumPriority?.ToLowerInvariant() switch
        {
            "low" => NotificationPriority.Low,
            "normal" => NotificationPriority.Normal,
            "high" => NotificationPriority.High,
            "critical" => NotificationPriority.Critical,
            _ => existing.MinimumPriority
        };

        var updated = existing with
        {
            SmsEnabled = request.SmsEnabled ?? existing.SmsEnabled,
            PushEnabled = request.PushEnabled ?? existing.PushEnabled,
            MinimumPriority = priority,
            PhoneNumber = request.PhoneNumber ?? existing.PhoneNumber
        };

        var result = await _registrationPort.UpdateProfileAsync(updated, ct);

        _logger.LogInformation(
            "Notification profile updated: UserId={UserId}, Push={Push}, SMS={Sms}, MinPriority={Priority}",
            request.UserId, result.PushEnabled, result.SmsEnabled, result.MinimumPriority);

        return Ok(new
        {
            result.UserId,
            result.PushEnabled,
            result.SmsEnabled,
            MinimumPriority = result.MinimumPriority.ToString(),
            result.LastUpdated
        });
    }

    // ─────────────────────────────────────────────────────────────
    // Send Notification (admin/system use)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Send a push notification to a specific user (admin/system endpoint).
    /// For SOS dispatch notifications, use the ResponseCoordinationService pipeline instead.
    /// </summary>
    [HttpPost("send")]
    public async Task<IActionResult> SendNotification(
        [FromBody] SendNotificationRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
            return BadRequest(new { error = "UserId is required" });
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { error = "Title is required" });

        // Get the user's devices to send push
        var devices = await _registrationPort.GetDevicesAsync(request.UserId, ct);
        var activeDevice = devices.FirstOrDefault(d => d.IsActive);

        if (activeDevice is null)
            return NotFound(new { error = $"No active devices found for user '{request.UserId}'" });

        var category = request.Category?.ToLowerInvariant() switch
        {
            "sos" => NotificationCategory.SosDispatch,
            "update" => NotificationCategory.SosUpdate,
            "checkin" => NotificationCategory.CheckInRequest,
            "evacuation" => NotificationCategory.EvacuationNotice,
            _ => NotificationCategory.SosUpdate
        };

        var payload = new NotificationPayload(
            NotificationId: Guid.NewGuid().ToString("N")[..12],
            RecipientUserId: request.UserId,
            RecipientDeviceToken: activeDevice.DeviceToken,
            RecipientPhoneNumber: null,
            Category: category,
            Priority: NotificationPriority.Normal,
            PreferredChannel: NotificationChannel.Push,
            Title: request.Title,
            Body: request.Body ?? "",
            Subtitle: null,
            DeepLink: request.DeepLink,
            RequestId: request.RequestId,
            RequestorName: null,
            Scope: null,
            IncidentLatitude: null,
            IncidentLongitude: null,
            DistanceMeters: null,
            SmsReplyInstructions: null,
            CreatedAt: DateTime.UtcNow,
            ExpiresAfter: TimeSpan.FromHours(1)
        );

        var result = await _sendPort.SendPushAsync(payload, ct);

        // Track delivery
        await _trackingPort.RecordDeliveryAsync(result, ct);

        return Ok(new
        {
            result.NotificationId,
            result.RecipientUserId,
            Channel = result.Channel.ToString(),
            Status = result.Status.ToString(),
            result.ExternalMessageId,
            result.SentAt
        });
    }

    // ─────────────────────────────────────────────────────────────
    // Delivery Tracking
    // ─────────────────────────────────────────────────────────────

    /// <summary>Get delivery results for a specific request (how many sent, delivered, failed).</summary>
    [HttpGet("delivery/{requestId}")]
    public async Task<IActionResult> GetDeliveryResults(string requestId, CancellationToken ct)
    {
        var results = await _trackingPort.GetDeliveryResultsAsync(requestId, ct);
        return Ok(results.Select(r => new
        {
            r.NotificationId,
            r.RecipientUserId,
            Channel = r.Channel.ToString(),
            Status = r.Status.ToString(),
            r.ExternalMessageId,
            r.ErrorMessage,
            r.SentAt
        }));
    }

    /// <summary>Get user responses for a specific request (how many accepted, declined).</summary>
    [HttpGet("responses/{requestId}")]
    public async Task<IActionResult> GetResponses(string requestId, CancellationToken ct)
    {
        var responses = await _trackingPort.GetResponsesAsync(requestId, ct);
        return Ok(responses.Select(r => new
        {
            r.ResponseId,
            r.NotificationId,
            r.RequestId,
            r.ResponderId,
            Action = r.Action.ToString(),
            SourceChannel = r.SourceChannel.ToString(),
            r.ResponderLatitude,
            r.ResponderLongitude,
            r.RespondedAt
        }));
    }
}

// ─────────────────────────────────────────────────────────────
// Request DTOs
// ─────────────────────────────────────────────────────────────

public record RegisterDeviceRequest(
    string UserId,
    string Token,
    string? Platform = "android",
    string? DeviceName = null
);

public record UnregisterDeviceRequest(
    string UserId,
    string DeviceId
);

public record UpdateNotificationProfileRequest(
    string UserId,
    bool? SmsEnabled = null,
    bool? PushEnabled = null,
    string? MinimumPriority = null,
    string? PhoneNumber = null
);

public record SendNotificationRequest(
    string UserId,
    string Title,
    string? Body = null,
    string? Category = null,
    string? DeepLink = null,
    string? RequestId = null
);
