// =============================================================================
// Mock Notification & SMS Adapters — permanent first-class implementations.
// =============================================================================
// These are NOT throwaway test doubles. They are fully functional in-memory
// implementations used in Development/Staging and for the Aspire dashboard.
//
// MockNotificationSendAdapter — logs push notifications, simulates delivery
// MockSmsAdapter — logs SMS messages, parses inbound Y/N replies
// MockNotificationRegistrationAdapter — manages device tokens in-memory
// MockNotificationTrackingAdapter — tracks delivery & responses in-memory
// =============================================================================

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Adapters.Mock;

// ═══════════════════════════════════════════════════════════════
// Push Notification Adapter
// ═══════════════════════════════════════════════════════════════

public class MockNotificationSendAdapter : INotificationSendPort
{
    private readonly ILogger<MockNotificationSendAdapter> _logger;
    private readonly ConcurrentDictionary<string, NotificationPayload> _sentNotifications = new();

    public MockNotificationSendAdapter(ILogger<MockNotificationSendAdapter> logger)
    {
        _logger = logger;
    }

    public Task<NotificationResult> SendPushAsync(
        NotificationPayload payload, CancellationToken ct = default)
    {
        _sentNotifications[payload.NotificationId] = payload;

        _logger.LogInformation(
            "[MOCK PUSH] → {Recipient} | {Category} | {Priority} | \"{Title}: {Body}\"",
            payload.RecipientUserId, payload.Category, payload.Priority,
            payload.Title, payload.Body);

        if (payload.Priority == NotificationPriority.Critical)
        {
            _logger.LogWarning(
                "[MOCK PUSH] ⚠ CRITICAL notification — would bypass DND on device. " +
                "RequestId={RequestId}, Scope={Scope}",
                payload.RequestId, payload.Scope);
        }

        var result = new NotificationResult(
            NotificationId: payload.NotificationId,
            RecipientUserId: payload.RecipientUserId,
            Channel: NotificationChannel.Push,
            Status: NotificationDeliveryStatus.Delivered,
            ExternalMessageId: $"mock-fcm-{Guid.NewGuid():N}"[..20],
            ErrorMessage: null,
            SentAt: DateTime.UtcNow
        );

        return Task.FromResult(result);
    }

    public async Task<IReadOnlyList<NotificationResult>> SendPushBatchAsync(
        IReadOnlyList<NotificationPayload> payloads, CancellationToken ct = default)
    {
        _logger.LogInformation("[MOCK PUSH BATCH] Sending {Count} notifications", payloads.Count);

        var results = new List<NotificationResult>(payloads.Count);
        foreach (var payload in payloads)
        {
            results.Add(await SendPushAsync(payload, ct));
        }

        _logger.LogInformation(
            "[MOCK PUSH BATCH] Delivered {Delivered}/{Total}",
            results.Count(r => r.Status == NotificationDeliveryStatus.Delivered),
            results.Count);

        return results.AsReadOnly();
    }

    public Task<bool> CancelNotificationAsync(
        string notificationId, string recipientUserId, CancellationToken ct = default)
    {
        var removed = _sentNotifications.TryRemove(notificationId, out _);

        _logger.LogInformation(
            "[MOCK PUSH] Cancel notification {NotificationId} for {User}: {Result}",
            notificationId, recipientUserId, removed ? "CANCELLED" : "NOT_FOUND");

        return Task.FromResult(removed);
    }

    // Test helper — get all sent notifications
    public IReadOnlyDictionary<string, NotificationPayload> GetSentNotifications()
        => _sentNotifications;
}

// ═══════════════════════════════════════════════════════════════
// SMS Adapter
// ═══════════════════════════════════════════════════════════════

public class MockSmsAdapter : ISmsPort
{
    private readonly ILogger<MockSmsAdapter> _logger;
    private readonly ConcurrentDictionary<string, (string Phone, string Message, string? RequestId)> _sentMessages = new();

    // Maps phone number → userId for inbound reply routing
    private readonly ConcurrentDictionary<string, string> _phoneToUserMap = new();

    // Maps phone number → most recent requestId for reply context
    private readonly ConcurrentDictionary<string, string> _phoneToRequestMap = new();

    public MockSmsAdapter(ILogger<MockSmsAdapter> logger)
    {
        _logger = logger;

        // Seed some phone-to-user mappings for testing
        _phoneToUserMap["+15551234001"] = "mock-user-001";
        _phoneToUserMap["+15551234002"] = "mock-user-002";
        _phoneToUserMap["+15551234003"] = "mock-user-003";
    }

    public Task<NotificationResult> SendSmsAsync(
        string phoneNumber, string message, string? requestId = null,
        CancellationToken ct = default)
    {
        var smsId = Guid.NewGuid().ToString("N")[..12];
        _sentMessages[smsId] = (phoneNumber, message, requestId);

        // Track which request this phone last received (for reply routing)
        if (requestId is not null)
        {
            _phoneToRequestMap[phoneNumber] = requestId;
        }

        _logger.LogInformation(
            "[MOCK SMS] → {Phone} | \"{Message}\" | RequestId={RequestId}",
            phoneNumber, message.Length > 80 ? message[..80] + "..." : message, requestId);

        var result = new NotificationResult(
            NotificationId: smsId,
            RecipientUserId: _phoneToUserMap.GetValueOrDefault(phoneNumber, "unknown"),
            Channel: NotificationChannel.Sms,
            Status: NotificationDeliveryStatus.Delivered,
            ExternalMessageId: $"mock-twilio-{smsId}",
            ErrorMessage: null,
            SentAt: DateTime.UtcNow
        );

        return Task.FromResult(result);
    }

    public async Task<IReadOnlyList<NotificationResult>> SendSmsBatchAsync(
        IReadOnlyList<(string PhoneNumber, string Message, string? RequestId)> messages,
        CancellationToken ct = default)
    {
        _logger.LogInformation("[MOCK SMS BATCH] Sending {Count} messages", messages.Count);

        var results = new List<NotificationResult>(messages.Count);
        foreach (var (phone, message, requestId) in messages)
        {
            results.Add(await SendSmsAsync(phone, message, requestId, ct));
        }

        return results.AsReadOnly();
    }

    public Task<NotificationResponse?> ProcessInboundSmsAsync(
        string fromNumber, string body, string? toNumber = null,
        CancellationToken ct = default)
    {
        var normalizedBody = body.Trim().ToUpperInvariant();

        _logger.LogInformation(
            "[MOCK SMS INBOUND] ← {From} | \"{Body}\"",
            fromNumber, body);

        // Determine the action from the reply text
        NotificationResponseAction? action = normalizedBody switch
        {
            "Y" or "YES" or "ACCEPT" or "OK" or "OMW" => NotificationResponseAction.Accept,
            "N" or "NO" or "DECLINE" or "BUSY" => NotificationResponseAction.Decline,
            "HELP" or "NEED HELP" or "SOS" => NotificationResponseAction.NeedHelp,
            "OK" or "IM OK" or "I'M OK" or "SAFE" => NotificationResponseAction.ImOk,
            "911" or "CALL 911" => NotificationResponseAction.Call911,
            _ => null
        };

        if (action is null)
        {
            _logger.LogWarning(
                "[MOCK SMS INBOUND] Unrecognized reply from {From}: \"{Body}\"",
                fromNumber, body);
            return Task.FromResult<NotificationResponse?>(null);
        }

        // Find the user and request this reply is for
        var userId = _phoneToUserMap.GetValueOrDefault(fromNumber, "unknown-sms-user");
        var requestId = _phoneToRequestMap.GetValueOrDefault(fromNumber, "unknown-request");

        var response = new NotificationResponse(
            ResponseId: Guid.NewGuid().ToString("N")[..12],
            NotificationId: "sms-inbound",
            RequestId: requestId,
            ResponderId: userId,
            Action: action.Value,
            SourceChannel: NotificationChannel.Sms,
            RawSmsBody: body,
            ResponderLatitude: null,
            ResponderLongitude: null,
            RespondedAt: DateTime.UtcNow
        );

        _logger.LogInformation(
            "[MOCK SMS INBOUND] Parsed: {Action} from {User} for request {RequestId}",
            action, userId, requestId);

        return Task.FromResult<NotificationResponse?>(response);
    }

    // Test helpers
    public void RegisterPhoneMapping(string phoneNumber, string userId)
        => _phoneToUserMap[phoneNumber] = userId;

    public IReadOnlyDictionary<string, (string Phone, string Message, string? RequestId)> GetSentMessages()
        => _sentMessages;
}

// ═══════════════════════════════════════════════════════════════
// Device Registration Adapter
// ═══════════════════════════════════════════════════════════════

public class MockNotificationRegistrationAdapter : INotificationRegistrationPort
{
    private readonly ILogger<MockNotificationRegistrationAdapter> _logger;
    private readonly ConcurrentDictionary<string, UserNotificationProfile> _profiles = new();

    public MockNotificationRegistrationAdapter(ILogger<MockNotificationRegistrationAdapter> logger)
    {
        _logger = logger;
        SeedMockProfiles();
    }

    private void SeedMockProfiles()
    {
        // Seed profiles that match the mock responders from MockParticipationAdapter
        var profiles = new[]
        {
            CreateProfile("mock-user-001", "Sarah Chen", "+15551234001", DevicePlatform.iOS),
            CreateProfile("mock-user-002", "Marcus Johnson", "+15551234002", DevicePlatform.Android),
            CreateProfile("mock-user-003", "Elena Rodriguez", "+15551234003", DevicePlatform.iOS),
            CreateProfile("mock-user-004", "David Park", "+15551234004", DevicePlatform.Android),
            CreateProfile("mock-user-005", "Aisha Williams", "+15551234005", DevicePlatform.iOS),
            CreateProfile("mock-user-006", "Tom Brennan", "+15551234006", DevicePlatform.Android),
            CreateProfile("mock-user-007", "Priya Patel", "+15551234007", DevicePlatform.Android),
            CreateProfile("mock-user-008", "James O'Sullivan", "+15551234008", DevicePlatform.iOS),
        };

        foreach (var p in profiles)
            _profiles[p.UserId] = p;
    }

    private static UserNotificationProfile CreateProfile(
        string userId, string name, string phone, DevicePlatform platform)
    {
        var device = new DeviceRegistration(
            DeviceId: $"{userId}-device-1",
            DeviceToken: $"mock-token-{userId}-{Guid.NewGuid():N}"[..40],
            Platform: platform,
            DeviceName: platform == DevicePlatform.iOS ? $"{name}'s iPhone" : $"{name}'s Android",
            RegisteredAt: DateTime.UtcNow.AddDays(-30),
            LastSeenAt: DateTime.UtcNow.AddMinutes(-5),
            IsActive: true
        );

        return new UserNotificationProfile(
            UserId: userId,
            DisplayName: name,
            PhoneNumber: phone,
            SmsEnabled: true,
            PushEnabled: true,
            Devices: new[] { device },
            MinimumPriority: NotificationPriority.Normal,
            LastUpdated: DateTime.UtcNow
        );
    }

    public Task<DeviceRegistration> RegisterDeviceAsync(
        string userId, string deviceToken, DevicePlatform platform,
        string? deviceName = null, CancellationToken ct = default)
    {
        var device = new DeviceRegistration(
            DeviceId: $"{userId}-{Guid.NewGuid():N}"[..20],
            DeviceToken: deviceToken,
            Platform: platform,
            DeviceName: deviceName,
            RegisteredAt: DateTime.UtcNow,
            LastSeenAt: DateTime.UtcNow,
            IsActive: true
        );

        var profile = _profiles.GetOrAdd(userId, _ => new UserNotificationProfile(
            UserId: userId, DisplayName: null, PhoneNumber: null,
            SmsEnabled: false, PushEnabled: true,
            Devices: Array.Empty<DeviceRegistration>(),
            MinimumPriority: NotificationPriority.Normal,
            LastUpdated: DateTime.UtcNow
        ));

        var devices = profile.Devices.ToList();
        devices.Add(device);
        _profiles[userId] = profile with { Devices = devices.AsReadOnly(), LastUpdated = DateTime.UtcNow };

        _logger.LogInformation(
            "[MOCK REGISTRATION] Device registered: {UserId} / {Platform} / {DeviceName}",
            userId, platform, deviceName);

        return Task.FromResult(device);
    }

    public Task<bool> UnregisterDeviceAsync(
        string userId, string deviceId, CancellationToken ct = default)
    {
        if (!_profiles.TryGetValue(userId, out var profile))
            return Task.FromResult(false);

        var devices = profile.Devices.Where(d => d.DeviceId != deviceId).ToList();
        _profiles[userId] = profile with { Devices = devices.AsReadOnly(), LastUpdated = DateTime.UtcNow };

        _logger.LogInformation("[MOCK REGISTRATION] Device unregistered: {UserId} / {DeviceId}", userId, deviceId);
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<DeviceRegistration>> GetDevicesAsync(
        string userId, CancellationToken ct = default)
    {
        if (_profiles.TryGetValue(userId, out var profile))
            return Task.FromResult(profile.Devices);

        return Task.FromResult<IReadOnlyList<DeviceRegistration>>(Array.Empty<DeviceRegistration>());
    }

    public Task<UserNotificationProfile> GetProfileAsync(
        string userId, CancellationToken ct = default)
    {
        var profile = _profiles.GetOrAdd(userId, id => new UserNotificationProfile(
            UserId: id, DisplayName: null, PhoneNumber: null,
            SmsEnabled: false, PushEnabled: true,
            Devices: Array.Empty<DeviceRegistration>(),
            MinimumPriority: NotificationPriority.Normal,
            LastUpdated: DateTime.UtcNow
        ));

        return Task.FromResult(profile);
    }

    public Task<UserNotificationProfile> UpdateProfileAsync(
        UserNotificationProfile profile, CancellationToken ct = default)
    {
        var updated = profile with { LastUpdated = DateTime.UtcNow };
        _profiles[profile.UserId] = updated;

        _logger.LogInformation(
            "[MOCK REGISTRATION] Profile updated: {UserId} | Push={Push}, SMS={Sms}",
            profile.UserId, profile.PushEnabled, profile.SmsEnabled);

        return Task.FromResult(updated);
    }
}

// ═══════════════════════════════════════════════════════════════
// Notification Tracking Adapter
// ═══════════════════════════════════════════════════════════════

public class MockNotificationTrackingAdapter : INotificationTrackingPort
{
    private readonly ILogger<MockNotificationTrackingAdapter> _logger;
    private readonly ConcurrentDictionary<string, List<NotificationResult>> _deliveryByRequest = new();
    private readonly ConcurrentDictionary<string, List<NotificationResponse>> _responsesByRequest = new();

    public MockNotificationTrackingAdapter(ILogger<MockNotificationTrackingAdapter> logger)
    {
        _logger = logger;
    }

    public Task RecordDeliveryAsync(NotificationResult result, CancellationToken ct = default)
    {
        // Try to find the requestId from the notification — we store by request
        // For simplicity in mock, use the NotificationId as grouping key
        var list = _deliveryByRequest.GetOrAdd(result.NotificationId, _ => new List<NotificationResult>());
        lock (list) { list.Add(result); }

        _logger.LogDebug(
            "[MOCK TRACKING] Delivery recorded: {NotificationId} → {Status}",
            result.NotificationId, result.Status);

        return Task.CompletedTask;
    }

    public Task RecordResponseAsync(NotificationResponse response, CancellationToken ct = default)
    {
        var list = _responsesByRequest.GetOrAdd(response.RequestId, _ => new List<NotificationResponse>());
        lock (list) { list.Add(response); }

        _logger.LogInformation(
            "[MOCK TRACKING] Response recorded: {Action} from {Responder} via {Channel} for request {RequestId}",
            response.Action, response.ResponderId, response.SourceChannel, response.RequestId);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<NotificationResult>> GetDeliveryResultsAsync(
        string requestId, CancellationToken ct = default)
    {
        if (_deliveryByRequest.TryGetValue(requestId, out var list))
        {
            lock (list) { return Task.FromResult<IReadOnlyList<NotificationResult>>(list.ToList().AsReadOnly()); }
        }

        return Task.FromResult<IReadOnlyList<NotificationResult>>(Array.Empty<NotificationResult>());
    }

    public Task<IReadOnlyList<NotificationResponse>> GetResponsesAsync(
        string requestId, CancellationToken ct = default)
    {
        if (_responsesByRequest.TryGetValue(requestId, out var list))
        {
            lock (list) { return Task.FromResult<IReadOnlyList<NotificationResponse>>(list.ToList().AsReadOnly()); }
        }

        return Task.FromResult<IReadOnlyList<NotificationResponse>>(Array.Empty<NotificationResponse>());
    }
}
