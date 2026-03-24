// IoTAlertController — REST endpoints for IoT device alert ingestion and management.
//
// Handles inbound alerts from ALL supported IoT platforms (Alexa, Google Home,
// SmartThings, HomeKit, IFTTT, custom webhooks, Ring, Wyze, Tuya, Zigbee, Z-Wave, Matter).
//
// Endpoints:
//   POST /api/iot/alert                — Ingest an alert from any IoT source
//   POST /api/iot/checkin              — Process a check-in from an IoT device
//   GET  /api/iot/status/{userId}      — Get IoT device status for a user
//   POST /api/iot/cancel               — Cancel an active IoT alert
//   POST /api/iot/devices/register     — Register a new IoT device
//   DELETE /api/iot/devices/{deviceId} — Unregister an IoT device
//   GET  /api/iot/devices/{userId}     — Get all registered devices for a user
//   POST /api/iot/users/map            — Map an external IoT user to TheWatch user
//   POST /api/iot/webhook/{source}     — Process a raw webhook from an IoT platform
//
// All alert ingestion triggers a SignalR broadcast to the dashboard via DashboardHub.
// The controller is thin — validation + delegation to IIoTAlertPort and IIoTWebhookPort.
//
// WAL: No PII is logged beyond external user IDs and device identifiers.
//      Audio data NEVER passes through this controller.
//      All webhook payloads are signature-validated before processing.

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using TheWatch.Dashboard.Api.Hubs;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Dashboard.Api.Controllers;

[ApiController]
[Route("api/iot")]
public class IoTAlertController : ControllerBase
{
    private readonly IIoTAlertPort _alertPort;
    private readonly IIoTWebhookPort _webhookPort;
    private readonly IHubContext<DashboardHub> _hubContext;
    private readonly ILogger<IoTAlertController> _logger;

    public IoTAlertController(
        IIoTAlertPort alertPort,
        IIoTWebhookPort webhookPort,
        IHubContext<DashboardHub> hubContext,
        ILogger<IoTAlertController> logger)
    {
        _alertPort = alertPort;
        _webhookPort = webhookPort;
        _hubContext = hubContext;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────
    // Alert Ingestion
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Ingest an alert from any IoT source. The normalized IoTAlertRequest is
    /// platform-agnostic — the calling service (Alexa Lambda, Google Cloud Function,
    /// SmartThings connector) normalizes the platform-specific payload before calling.
    /// </summary>
    [HttpPost("alert")]
    public async Task<IActionResult> TriggerAlert(
        [FromBody] IoTAlertRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ExternalUserId))
            return BadRequest(new { error = "ExternalUserId is required" });

        if (string.IsNullOrWhiteSpace(request.TriggerMethod))
            return BadRequest(new { error = "TriggerMethod is required" });

        _logger.LogWarning(
            "IoT ALERT received: Source={Source}, ExternalUser={ExternalUser}, " +
            "Trigger={Trigger}, Scope={Scope}",
            request.Source, request.ExternalUserId, request.TriggerMethod, request.Scope);

        var result = await _alertPort.TriggerIoTAlertAsync(request, ct);

        // Broadcast to dashboard via SignalR
        if (result.Status == IoTAlertStatus.Dispatched)
        {
            await _hubContext.Clients.All.SendAsync("IoTAlertReceived", new
            {
                result.AlertId,
                result.RequestId,
                Source = request.Source.ToString(),
                request.TriggerMethod,
                request.DeviceType,
                Scope = request.Scope.ToString(),
                Status = result.Status.ToString(),
                result.RespondersNotified,
                result.ResponseRequestId,
                Timestamp = DateTime.UtcNow
            }, ct);

            _logger.LogInformation(
                "IoT alert broadcasted to dashboard: AlertId={AlertId}, Responders={Responders}",
                result.AlertId, result.RespondersNotified);
        }

        return result.Status switch
        {
            IoTAlertStatus.Dispatched => Accepted(result),
            IoTAlertStatus.PendingConfirmation => Ok(result),
            IoTAlertStatus.UserNotMapped => UnprocessableEntity(result),
            IoTAlertStatus.Throttled => StatusCode(429, result),
            _ => StatusCode(500, result)
        };
    }

    // ─────────────────────────────────────────────────────────────
    // Check-In
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Process a check-in from an IoT device.
    /// "Alexa, tell The Watch I'm okay" or automatic daily wellness check.
    /// </summary>
    [HttpPost("checkin")]
    public async Task<IActionResult> ProcessCheckIn(
        [FromBody] IoTCheckInRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ExternalUserId))
            return BadRequest(new { error = "ExternalUserId is required" });

        _logger.LogInformation(
            "IoT CHECK-IN: Source={Source}, ExternalUser={ExternalUser}, Status={Status}",
            request.Source, request.ExternalUserId, request.Status);

        var result = await _alertPort.ProcessIoTCheckInAsync(request, ct);

        // Broadcast escalations to dashboard
        if (result.Status == IoTCheckInResultStatus.EscalationTriggered)
        {
            await _hubContext.Clients.All.SendAsync("IoTCheckInEscalation", new
            {
                result.CheckInId,
                Source = request.Source.ToString(),
                CheckInStatus = request.Status.ToString(),
                result.Message,
                Timestamp = DateTime.UtcNow
            }, ct);
        }

        return result.Status switch
        {
            IoTCheckInResultStatus.Recorded => Ok(result),
            IoTCheckInResultStatus.EscalationTriggered => Accepted(result),
            IoTCheckInResultStatus.UserNotMapped => UnprocessableEntity(result),
            _ => StatusCode(500, result)
        };
    }

    // ─────────────────────────────────────────────────────────────
    // Status & Cancel
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Get the current IoT device status for a user — active alerts, registered
    /// devices, nearby responders, last check-in time.
    /// </summary>
    [HttpGet("status/{userId}")]
    public async Task<IActionResult> GetDeviceStatus(string userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return BadRequest(new { error = "userId is required" });

        var status = await _alertPort.GetIoTDeviceStatusAsync(userId, ct);
        return Ok(status);
    }

    /// <summary>
    /// Cancel an active IoT-originated alert.
    /// Called when user says "Alexa, tell The Watch I'm okay" or presses cancel.
    /// </summary>
    [HttpPost("cancel")]
    public async Task<IActionResult> CancelAlert(
        [FromBody] IoTCancelRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.AlertId))
            return BadRequest(new { error = "AlertId is required" });

        _logger.LogInformation(
            "IoT CANCEL: AlertId={AlertId}, Reason={Reason}",
            request.AlertId, request.Reason);

        var result = await _alertPort.CancelIoTAlertAsync(
            request.AlertId, request.Reason ?? "User cancelled via IoT device", ct);

        if (result.Status == IoTAlertStatus.Cancelled)
        {
            await _hubContext.Clients.All.SendAsync("IoTAlertCancelled", new
            {
                result.AlertId,
                result.RequestId,
                Reason = request.Reason,
                Timestamp = DateTime.UtcNow
            }, ct);
        }

        return Ok(result);
    }

    // ─────────────────────────────────────────────────────────────
    // Device Management
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Register a new IoT device for a user.
    /// Called during device setup, discovery, or SmartApp installation.
    /// </summary>
    [HttpPost("devices/register")]
    public async Task<IActionResult> RegisterDevice(
        [FromBody] IoTDeviceRegistration registration,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(registration.UserId))
            return BadRequest(new { error = "UserId is required" });

        if (string.IsNullOrWhiteSpace(registration.DeviceName))
            return BadRequest(new { error = "DeviceName is required" });

        _logger.LogInformation(
            "IoT DEVICE REGISTER: UserId={UserId}, Source={Source}, Device={DeviceName}",
            registration.UserId, registration.Source, registration.DeviceName);

        var result = await _alertPort.RegisterIoTDeviceAsync(registration, ct);
        return Created($"/api/iot/devices/{result.DeviceId}", result);
    }

    /// <summary>
    /// Unregister an IoT device. Called when user removes device from their account.
    /// </summary>
    [HttpDelete("devices/{deviceId}")]
    public async Task<IActionResult> UnregisterDevice(string deviceId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return BadRequest(new { error = "deviceId is required" });

        _logger.LogInformation("IoT DEVICE UNREGISTER: {DeviceId}", deviceId);

        var removed = await _alertPort.UnregisterIoTDeviceAsync(deviceId, ct);
        return removed
            ? NoContent()
            : NotFound(new { error = $"Device {deviceId} not found" });
    }

    /// <summary>
    /// Get all registered IoT devices for a user.
    /// Used by the settings screen and dashboard device management view.
    /// </summary>
    [HttpGet("devices/{userId}")]
    public async Task<IActionResult> GetRegisteredDevices(string userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return BadRequest(new { error = "userId is required" });

        var devices = await _alertPort.GetRegisteredDevicesAsync(userId, ct);
        return Ok(devices);
    }

    // ─────────────────────────────────────────────────────────────
    // User Mapping
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Map an external IoT platform user to a TheWatch user.
    /// Called during OAuth2 account linking flow completion.
    /// </summary>
    [HttpPost("users/map")]
    public async Task<IActionResult> MapExternalUser(
        [FromBody] IoTUserMapping mapping,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(mapping.ExternalUserId))
            return BadRequest(new { error = "ExternalUserId is required" });

        if (string.IsNullOrWhiteSpace(mapping.TheWatchUserId))
            return BadRequest(new { error = "TheWatchUserId is required" });

        _logger.LogInformation(
            "IoT USER MAP: {Source}:{ExternalUser} → {TheWatchUser}",
            mapping.Source, mapping.ExternalUserId, mapping.TheWatchUserId);

        var result = await _alertPort.MapExternalUserAsync(mapping, ct);
        return Created($"/api/iot/users/{mapping.Source}/{mapping.ExternalUserId}", result);
    }

    // ─────────────────────────────────────────────────────────────
    // Webhook Ingestion
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Process a raw webhook from an IoT platform. The source is extracted from the URL path.
    /// The raw body and headers are passed to IIoTWebhookPort for platform-specific processing.
    ///
    /// URL pattern: POST /api/iot/webhook/{source}
    /// Optional query param: ?webhookId=xxx for registered webhook endpoints
    /// </summary>
    [HttpPost("webhook/{source}")]
    public async Task<IActionResult> ProcessWebhook(
        string source,
        [FromQuery] string? webhookId,
        CancellationToken ct)
    {
        if (!Enum.TryParse<IoTSource>(source, ignoreCase: true, out var iotSource))
            return BadRequest(new { error = $"Unknown IoT source: {source}" });

        // Read raw body
        using var memStream = new MemoryStream();
        await Request.Body.CopyToAsync(memStream, ct);
        var body = memStream.ToArray();

        // Collect headers
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in Request.Headers)
        {
            headers[header.Key] = header.Value.ToString();
        }

        // Add remote IP for audit
        headers["X-Forwarded-For"] = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        _logger.LogInformation(
            "IoT WEBHOOK: Source={Source}, WebhookId={WebhookId}, BodySize={Size} bytes",
            iotSource, webhookId, body.Length);

        var result = await _webhookPort.ProcessWebhookAsync(iotSource, webhookId, headers, body, ct);

        // Broadcast successful alerts to dashboard
        if (result.Success && result.AlertId is not null)
        {
            await _hubContext.Clients.All.SendAsync("IoTWebhookAlertReceived", new
            {
                result.AlertId,
                Source = iotSource.ToString(),
                WebhookId = webhookId,
                ProcessingMs = result.ProcessingDuration?.TotalMilliseconds,
                Timestamp = DateTime.UtcNow
            }, ct);
        }

        // Return the platform-specific response body
        if (!string.IsNullOrEmpty(result.ResponseBody))
        {
            return new ContentResult
            {
                Content = result.ResponseBody,
                ContentType = "application/json",
                StatusCode = result.StatusCode
            };
        }

        return StatusCode(result.StatusCode);
    }
}

// ─────────────────────────────────────────────────────────────
// Request DTOs
// ─────────────────────────────────────────────────────────────

public record IoTCancelRequest(
    string AlertId,
    string? Reason = null
);
