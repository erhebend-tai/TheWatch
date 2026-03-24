// =============================================================================
// Mock IoT Alert Adapter — permanent first-class in-memory implementation.
// =============================================================================
// NOT a throwaway test double. Fully functional in-memory implementation used
// in Development/Staging and for the Aspire dashboard.
//
// Simulates the full IoT alert lifecycle:
//   - Alert ingestion from any IoT source (Alexa, Google Home, SmartThings, etc.)
//   - External user → TheWatch user mapping resolution
//   - Device registration and status tracking
//   - Check-in processing with escalation on missed check-ins
//   - Alert throttling (debounce within 30s window)
//
// Seeded data:
//   - 3 user mappings (Alexa, Google Home, SmartThings)
//   - 6 registered devices across those users
//   - Demonstrates voice, panic button, and sensor trigger types
//
// WAL: All operations are in-memory only. No external calls. No PII persistence
//      beyond the mock seed data. ConcurrentDictionary ensures thread safety.
// =============================================================================

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Adapters.Mock;

public class MockIoTAlertAdapter : IIoTAlertPort
{
    private readonly ILogger<MockIoTAlertAdapter> _logger;

    // ── Storage ──────────────────────────────────────────────────
    private readonly ConcurrentDictionary<string, IoTAlertRequest> _activeAlerts = new();
    private readonly ConcurrentDictionary<string, IoTAlertResult> _alertResults = new();
    private readonly ConcurrentDictionary<string, IoTDeviceRegistration> _devices = new();
    private readonly ConcurrentDictionary<string, IoTUserMapping> _userMappings = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastCheckIns = new();
    private readonly ConcurrentDictionary<string, DateTime> _lastAlertTimes = new();

    public MockIoTAlertAdapter(ILogger<MockIoTAlertAdapter> logger)
    {
        _logger = logger;
        SeedMockData();
    }

    private void SeedMockData()
    {
        // ── User Mappings ────────────────────────────────────────
        var mappings = new[]
        {
            new IoTUserMapping(
                Source: IoTSource.Alexa,
                ExternalUserId: "amzn1.ask.account.MOCK001",
                TheWatchUserId: "mock-user-001",
                AccessToken: "mock-alexa-token-001",
                RefreshToken: "mock-alexa-refresh-001",
                TokenExpiresAt: DateTime.UtcNow.AddHours(1),
                LinkedAt: DateTime.UtcNow.AddDays(-60),
                LastUsedAt: DateTime.UtcNow.AddHours(-2)
            ),
            new IoTUserMapping(
                Source: IoTSource.GoogleHome,
                ExternalUserId: "google-uid-MOCK002",
                TheWatchUserId: "mock-user-002",
                AccessToken: "mock-google-token-002",
                RefreshToken: "mock-google-refresh-002",
                TokenExpiresAt: DateTime.UtcNow.AddHours(1),
                LinkedAt: DateTime.UtcNow.AddDays(-30),
                LastUsedAt: DateTime.UtcNow.AddHours(-6)
            ),
            new IoTUserMapping(
                Source: IoTSource.SmartThings,
                ExternalUserId: "smartthings-user-MOCK003",
                TheWatchUserId: "mock-user-003",
                AccessToken: "mock-st-token-003",
                RefreshToken: "mock-st-refresh-003",
                TokenExpiresAt: DateTime.UtcNow.AddHours(1),
                LinkedAt: DateTime.UtcNow.AddDays(-14),
                LastUsedAt: DateTime.UtcNow.AddDays(-1)
            )
        };

        foreach (var m in mappings)
            _userMappings[$"{m.Source}:{m.ExternalUserId}"] = m;

        // ── Device Registrations ─────────────────────────────────
        var devices = new[]
        {
            new IoTDeviceRegistration(
                DeviceId: "iot-dev-001", UserId: "mock-user-001",
                Source: IoTSource.Alexa, DeviceName: "Living Room Echo Dot",
                Capabilities: new[] { "VOICE_COMMAND", "SPEAKER", "DISPLAY" },
                RegisteredAt: DateTime.UtcNow.AddDays(-60),
                LastSeenAt: DateTime.UtcNow.AddMinutes(-10),
                FirmwareVersion: "716858720", IsOnline: true,
                InstallationZone: "Living Room"),
            new IoTDeviceRegistration(
                DeviceId: "iot-dev-002", UserId: "mock-user-001",
                Source: IoTSource.Alexa, DeviceName: "Bedroom Echo Show",
                Capabilities: new[] { "VOICE_COMMAND", "SPEAKER", "DISPLAY", "CAMERA" },
                RegisteredAt: DateTime.UtcNow.AddDays(-55),
                LastSeenAt: DateTime.UtcNow.AddMinutes(-3),
                FirmwareVersion: "716858720", IsOnline: true,
                InstallationZone: "Bedroom"),
            new IoTDeviceRegistration(
                DeviceId: "iot-dev-003", UserId: "mock-user-002",
                Source: IoTSource.GoogleHome, DeviceName: "Kitchen Nest Hub",
                Capabilities: new[] { "VOICE_COMMAND", "SPEAKER", "DISPLAY" },
                RegisteredAt: DateTime.UtcNow.AddDays(-30),
                LastSeenAt: DateTime.UtcNow.AddMinutes(-20),
                FirmwareVersion: "3.2.1", IsOnline: true,
                InstallationZone: "Kitchen"),
            new IoTDeviceRegistration(
                DeviceId: "iot-dev-004", UserId: "mock-user-002",
                Source: IoTSource.GoogleHome, DeviceName: "Hallway Nest Mini",
                Capabilities: new[] { "VOICE_COMMAND", "SPEAKER" },
                RegisteredAt: DateTime.UtcNow.AddDays(-28),
                LastSeenAt: DateTime.UtcNow.AddHours(-1),
                FirmwareVersion: "3.2.1", IsOnline: true,
                InstallationZone: "Hallway"),
            new IoTDeviceRegistration(
                DeviceId: "iot-dev-005", UserId: "mock-user-003",
                Source: IoTSource.SmartThings, DeviceName: "Front Door Button",
                Capabilities: new[] { "PANIC_BUTTON" },
                RegisteredAt: DateTime.UtcNow.AddDays(-14),
                LastSeenAt: DateTime.UtcNow.AddMinutes(-45),
                FirmwareVersion: "1.4.2", IsOnline: true,
                BatteryLevel: 87, InstallationZone: "Front Door"),
            new IoTDeviceRegistration(
                DeviceId: "iot-dev-006", UserId: "mock-user-003",
                Source: IoTSource.SmartThings, DeviceName: "Kitchen Smoke Detector",
                Capabilities: new[] { "SMOKE_DETECTOR", "CO_DETECTOR", "SIREN" },
                RegisteredAt: DateTime.UtcNow.AddDays(-14),
                LastSeenAt: DateTime.UtcNow.AddMinutes(-5),
                FirmwareVersion: "2.1.0", IsOnline: true,
                BatteryLevel: 94, InstallationZone: "Kitchen")
        };

        foreach (var d in devices)
            _devices[d.DeviceId] = d;

        // ── Seed check-in times ──────────────────────────────────
        _lastCheckIns["mock-user-001"] = DateTime.UtcNow.AddHours(-4);
        _lastCheckIns["mock-user-002"] = DateTime.UtcNow.AddHours(-8);
        _lastCheckIns["mock-user-003"] = DateTime.UtcNow.AddDays(-1);
    }

    // ═══════════════════════════════════════════════════════════════
    // Alert Ingestion
    // ═══════════════════════════════════════════════════════════════

    public Task<IoTAlertResult> TriggerIoTAlertAsync(
        IoTAlertRequest request, CancellationToken ct = default)
    {
        var alertId = $"iot-alert-{Guid.NewGuid():N}"[..24];
        var mappingKey = $"{request.Source}:{request.ExternalUserId}";

        _logger.LogWarning(
            "[MOCK IoT ALERT] Incoming: Source={Source}, ExternalUser={ExternalUser}, " +
            "Trigger={Trigger}, Device={Device}, Scope={Scope}",
            request.Source, request.ExternalUserId, request.TriggerMethod,
            request.DeviceType, request.Scope);

        // Check user mapping
        if (!_userMappings.TryGetValue(mappingKey, out var mapping))
        {
            _logger.LogWarning(
                "[MOCK IoT ALERT] User NOT MAPPED: {Source}:{ExternalUser}",
                request.Source, request.ExternalUserId);

            var unmappedResult = new IoTAlertResult(
                AlertId: alertId,
                RequestId: request.PlatformRequestId ?? alertId,
                Status: IoTAlertStatus.UserNotMapped,
                RespondersNotified: 0,
                Message: "Account not linked. Please link your account in The Watch app settings.",
                ResponseRequestId: null);

            return Task.FromResult(unmappedResult);
        }

        // Check throttling (30-second debounce per user)
        var userKey = mapping.TheWatchUserId;
        if (_lastAlertTimes.TryGetValue(userKey, out var lastAlert) &&
            DateTime.UtcNow - lastAlert < TimeSpan.FromSeconds(30))
        {
            _logger.LogWarning(
                "[MOCK IoT ALERT] THROTTLED: User {UserId} sent alert {Seconds}s ago",
                userKey, (DateTime.UtcNow - lastAlert).TotalSeconds);

            var throttledResult = new IoTAlertResult(
                AlertId: alertId,
                RequestId: request.PlatformRequestId ?? alertId,
                Status: IoTAlertStatus.Throttled,
                RespondersNotified: 0,
                Message: "Alert throttled. A recent alert is still active. Say 'cancel' to dismiss the active alert first.");

            return Task.FromResult(throttledResult);
        }

        _lastAlertTimes[userKey] = DateTime.UtcNow;

        // Store the alert
        _activeAlerts[alertId] = request;

        // Simulate dispatch
        var respondersNotified = Random.Shared.Next(3, 8);
        var responseRequestId = $"resp-{Guid.NewGuid():N}"[..20];

        var result = new IoTAlertResult(
            AlertId: alertId,
            RequestId: request.PlatformRequestId ?? alertId,
            Status: IoTAlertStatus.Dispatched,
            RespondersNotified: respondersNotified,
            Message: $"Emergency alert sent. {respondersNotified} nearby responders have been notified. Help is on the way.",
            ResponseRequestId: responseRequestId,
            EstimatedResponseTime: TimeSpan.FromMinutes(Random.Shared.Next(3, 12)));

        _alertResults[alertId] = result;

        // Update user mapping last used
        _userMappings[mappingKey] = mapping with { LastUsedAt = DateTime.UtcNow };

        _logger.LogWarning(
            "[MOCK IoT ALERT] DISPATCHED: AlertId={AlertId}, TheWatchUser={UserId}, " +
            "Responders={Responders}, ResponseRequest={ResponseRequestId}",
            alertId, userKey, respondersNotified, responseRequestId);

        return Task.FromResult(result);
    }

    public Task<IoTCheckInResult> ProcessIoTCheckInAsync(
        IoTCheckInRequest request, CancellationToken ct = default)
    {
        var checkInId = $"iot-checkin-{Guid.NewGuid():N}"[..24];
        var mappingKey = $"{request.Source}:{request.ExternalUserId}";

        _logger.LogInformation(
            "[MOCK IoT CHECK-IN] Source={Source}, ExternalUser={ExternalUser}, Status={Status}",
            request.Source, request.ExternalUserId, request.Status);

        if (!_userMappings.TryGetValue(mappingKey, out var mapping))
        {
            return Task.FromResult(new IoTCheckInResult(
                CheckInId: checkInId,
                Status: IoTCheckInResultStatus.UserNotMapped,
                Message: "Account not linked. Please link your account in The Watch app."));
        }

        _lastCheckIns[mapping.TheWatchUserId] = DateTime.UtcNow;

        // If user reports needing help, escalate
        if (request.Status == IoTCheckInStatus.NeedHelp || request.Status == IoTCheckInStatus.FeelingUnwell)
        {
            _logger.LogWarning(
                "[MOCK IoT CHECK-IN] ESCALATION: User {UserId} reported {Status}",
                mapping.TheWatchUserId, request.Status);

            return Task.FromResult(new IoTCheckInResult(
                CheckInId: checkInId,
                Status: IoTCheckInResultStatus.EscalationTriggered,
                Message: "We've notified your emergency contacts. Help is on the way. Stay where you are.",
                NextCheckInDue: DateTime.UtcNow.AddMinutes(15)));
        }

        // If missed check-in, escalate
        if (request.Status == IoTCheckInStatus.Missed)
        {
            _logger.LogWarning(
                "[MOCK IoT CHECK-IN] MISSED: User {UserId} did not check in on schedule",
                mapping.TheWatchUserId);

            return Task.FromResult(new IoTCheckInResult(
                CheckInId: checkInId,
                Status: IoTCheckInResultStatus.EscalationTriggered,
                Message: "Missed check-in detected. Emergency contacts have been notified.",
                NextCheckInDue: DateTime.UtcNow.AddMinutes(30)));
        }

        return Task.FromResult(new IoTCheckInResult(
            CheckInId: checkInId,
            Status: IoTCheckInResultStatus.Recorded,
            Message: "Check-in recorded. You're all set. Stay safe!",
            NextCheckInDue: DateTime.UtcNow.AddHours(12)));
    }

    public Task<IoTDeviceStatus> GetIoTDeviceStatusAsync(
        string userId, CancellationToken ct = default)
    {
        var devices = _devices.Values.Where(d => d.UserId == userId).ToList();
        var activeAlerts = _activeAlerts
            .Where(kvp =>
            {
                var mappingKey = $"{kvp.Value.Source}:{kvp.Value.ExternalUserId}";
                return _userMappings.TryGetValue(mappingKey, out var m) && m.TheWatchUserId == userId;
            })
            .Select(kvp => new IoTActiveAlertSummary(
                AlertId: kvp.Key,
                Source: kvp.Value.Source,
                TriggerMethod: kvp.Value.TriggerMethod,
                Status: _alertResults.TryGetValue(kvp.Key, out var r) ? r.Status : IoTAlertStatus.Dispatched,
                TriggeredAt: kvp.Value.Timestamp))
            .ToList();

        _lastCheckIns.TryGetValue(userId, out var lastCheckIn);

        var status = new IoTDeviceStatus(
            UserId: userId,
            ActiveAlerts: activeAlerts.Count,
            NearbyResponders: Random.Shared.Next(5, 15),
            LastCheckIn: lastCheckIn == default ? null : lastCheckIn,
            RegisteredDevices: devices.AsReadOnly(),
            ActiveAlertDetails: activeAlerts.AsReadOnly());

        _logger.LogInformation(
            "[MOCK IoT STATUS] UserId={UserId}, Devices={Devices}, ActiveAlerts={Alerts}",
            userId, devices.Count, activeAlerts.Count);

        return Task.FromResult(status);
    }

    public Task<IoTAlertResult> CancelIoTAlertAsync(
        string alertId, string reason, CancellationToken ct = default)
    {
        if (!_activeAlerts.TryRemove(alertId, out var alert))
        {
            _logger.LogWarning("[MOCK IoT CANCEL] Alert not found: {AlertId}", alertId);
            return Task.FromResult(new IoTAlertResult(
                AlertId: alertId,
                RequestId: alertId,
                Status: IoTAlertStatus.Error,
                RespondersNotified: 0,
                Message: "Alert not found or already resolved."));
        }

        _logger.LogInformation(
            "[MOCK IoT CANCEL] Alert {AlertId} cancelled. Reason: {Reason}",
            alertId, reason);

        var result = new IoTAlertResult(
            AlertId: alertId,
            RequestId: alert.PlatformRequestId ?? alertId,
            Status: IoTAlertStatus.Cancelled,
            RespondersNotified: 0,
            Message: "Alert cancelled. All responders have been notified to stand down.");

        _alertResults[alertId] = result;
        return Task.FromResult(result);
    }

    // ═══════════════════════════════════════════════════════════════
    // Device Management
    // ═══════════════════════════════════════════════════════════════

    public Task<IoTDeviceRegistration> RegisterIoTDeviceAsync(
        IoTDeviceRegistration registration, CancellationToken ct = default)
    {
        var deviceId = string.IsNullOrEmpty(registration.DeviceId)
            ? $"iot-dev-{Guid.NewGuid():N}"[..16]
            : registration.DeviceId;

        var reg = registration with
        {
            DeviceId = deviceId,
            RegisteredAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow
        };

        _devices[deviceId] = reg;

        _logger.LogInformation(
            "[MOCK IoT DEVICE] Registered: {DeviceId} ({DeviceName}) for user {UserId}, Source={Source}",
            deviceId, reg.DeviceName, reg.UserId, reg.Source);

        return Task.FromResult(reg);
    }

    public Task<bool> UnregisterIoTDeviceAsync(string deviceId, CancellationToken ct = default)
    {
        var removed = _devices.TryRemove(deviceId, out var device);

        _logger.LogInformation(
            "[MOCK IoT DEVICE] Unregister {DeviceId}: {Result}",
            deviceId, removed ? $"REMOVED ({device!.DeviceName})" : "NOT_FOUND");

        return Task.FromResult(removed);
    }

    public Task<IReadOnlyList<IoTDeviceRegistration>> GetRegisteredDevicesAsync(
        string userId, CancellationToken ct = default)
    {
        var devices = _devices.Values
            .Where(d => d.UserId == userId)
            .OrderBy(d => d.InstallationZone)
            .ThenBy(d => d.DeviceName)
            .ToList();

        return Task.FromResult<IReadOnlyList<IoTDeviceRegistration>>(devices.AsReadOnly());
    }

    // ═══════════════════════════════════════════════════════════════
    // User Mapping
    // ═══════════════════════════════════════════════════════════════

    public Task<IoTUserMapping> MapExternalUserAsync(
        IoTUserMapping mapping, CancellationToken ct = default)
    {
        var key = $"{mapping.Source}:{mapping.ExternalUserId}";
        var stored = mapping with { LinkedAt = DateTime.UtcNow };
        _userMappings[key] = stored;

        _logger.LogInformation(
            "[MOCK IoT MAP] {Source}:{ExternalUser} → TheWatch user {TheWatchUser}",
            mapping.Source, mapping.ExternalUserId, mapping.TheWatchUserId);

        return Task.FromResult(stored);
    }

    public Task<IoTUserMapping?> ResolveExternalUserAsync(
        IoTSource source, string externalUserId, CancellationToken ct = default)
    {
        var key = $"{source}:{externalUserId}";
        _userMappings.TryGetValue(key, out var mapping);

        _logger.LogDebug(
            "[MOCK IoT RESOLVE] {Source}:{ExternalUser} → {Result}",
            source, externalUserId, mapping?.TheWatchUserId ?? "NOT_FOUND");

        return Task.FromResult(mapping);
    }

    public Task<bool> RevokeExternalUserMappingAsync(
        IoTSource source, string externalUserId, CancellationToken ct = default)
    {
        var key = $"{source}:{externalUserId}";
        var removed = _userMappings.TryRemove(key, out _);

        _logger.LogInformation(
            "[MOCK IoT REVOKE] {Source}:{ExternalUser}: {Result}",
            source, externalUserId, removed ? "REVOKED" : "NOT_FOUND");

        return Task.FromResult(removed);
    }

    public Task<IReadOnlyList<IoTUserMapping>> GetUserMappingsAsync(
        string theWatchUserId, CancellationToken ct = default)
    {
        var mappings = _userMappings.Values
            .Where(m => m.TheWatchUserId == theWatchUserId)
            .OrderBy(m => m.Source)
            .ToList();

        return Task.FromResult<IReadOnlyList<IoTUserMapping>>(mappings.AsReadOnly());
    }

    // ═══════════════════════════════════════════════════════════════
    // Test Helpers
    // ═══════════════════════════════════════════════════════════════

    public IReadOnlyDictionary<string, IoTAlertRequest> GetActiveAlerts() => _activeAlerts;
    public IReadOnlyDictionary<string, IoTDeviceRegistration> GetAllDevices() => _devices;
    public IReadOnlyDictionary<string, IoTUserMapping> GetAllMappings() => _userMappings;
}
