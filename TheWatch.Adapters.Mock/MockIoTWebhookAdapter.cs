// =============================================================================
// Mock IoT Webhook Adapter — permanent first-class in-memory implementation.
// =============================================================================
// Simulates webhook processing for all supported IoT platforms.
// Validates signatures (mock validation — always succeeds unless payload is empty),
// parses platform-specific payloads, and routes to the alert pipeline.
//
// In production, each platform has distinct signature validation:
//   - Alexa: X.509 certificate chain from Amazon's signing service
//   - Google: JWT validation with Google's public key set
//   - SmartThings: HMAC-SHA256 with SmartApp secret
//   - IFTTT: IFTTT-Service-Key header match
//   - Custom: HMAC-SHA256 with user-configured shared secret
//
// This mock adapter accepts all signatures (for development) but logs warnings
// if headers are missing, so developers can test their integration code.
//
// WAL: Mock adapter stores webhook registrations in-memory.
//      No external HTTP calls are made. All signature validation is simulated.
//      Production adapters MUST implement real signature validation.
// =============================================================================

using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Adapters.Mock;

public class MockIoTWebhookAdapter : IIoTWebhookPort
{
    private readonly ILogger<MockIoTWebhookAdapter> _logger;
    private readonly IIoTAlertPort _alertPort;
    private readonly ConcurrentDictionary<string, WebhookRegistration> _registrations = new();
    private readonly ConcurrentDictionary<string, long> _eventCounts = new();

    public MockIoTWebhookAdapter(
        ILogger<MockIoTWebhookAdapter> logger,
        IIoTAlertPort alertPort)
    {
        _logger = logger;
        _alertPort = alertPort;
        SeedMockRegistrations();
    }

    private void SeedMockRegistrations()
    {
        var registrations = new[]
        {
            new WebhookRegistration(
                WebhookId: "wh-alexa-001",
                Source: IoTSource.Alexa,
                UserId: "mock-user-001",
                EndpointUrl: "https://api.thewatch.app/api/iot/webhook/alexa/wh-alexa-001",
                SharedSecret: null, // Alexa uses cert-based validation
                IsActive: true,
                CreatedAt: DateTime.UtcNow.AddDays(-60),
                LastReceivedAt: DateTime.UtcNow.AddHours(-2),
                EventCount: 142),
            new WebhookRegistration(
                WebhookId: "wh-google-002",
                Source: IoTSource.GoogleHome,
                UserId: "mock-user-002",
                EndpointUrl: "https://api.thewatch.app/api/iot/webhook/googlehome/wh-google-002",
                SharedSecret: null, // Google uses JWT validation
                IsActive: true,
                CreatedAt: DateTime.UtcNow.AddDays(-30),
                LastReceivedAt: DateTime.UtcNow.AddHours(-6),
                EventCount: 87),
            new WebhookRegistration(
                WebhookId: "wh-st-003",
                Source: IoTSource.SmartThings,
                UserId: "mock-user-003",
                EndpointUrl: "https://api.thewatch.app/api/iot/webhook/smartthings/wh-st-003",
                SharedSecret: "mock-shared-secret-smartthings-003",
                IsActive: true,
                CreatedAt: DateTime.UtcNow.AddDays(-14),
                LastReceivedAt: DateTime.UtcNow.AddDays(-1),
                EventCount: 34)
        };

        foreach (var r in registrations)
            _registrations[r.WebhookId] = r;
    }

    // ═══════════════════════════════════════════════════════════════
    // Webhook Processing
    // ═══════════════════════════════════════════════════════════════

    public async Task<WebhookResult> ProcessWebhookAsync(
        IoTSource source, string? webhookId,
        IDictionary<string, string> headers, byte[] body,
        CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;

        _logger.LogInformation(
            "[MOCK WEBHOOK] Processing: Source={Source}, WebhookId={WebhookId}, BodySize={Size} bytes",
            source, webhookId, body.Length);

        // Validate signature first
        var validation = await ValidateWebhookSignatureAsync(source, webhookId, headers, body, ct);
        if (!validation.IsValid)
        {
            _logger.LogWarning(
                "[MOCK WEBHOOK] Signature INVALID: Source={Source}, Reason={Reason}",
                source, validation.FailureReason);

            return new WebhookResult(
                Success: false,
                StatusCode: 401,
                ResponseBody: JsonSerializer.Serialize(new { error = "Invalid signature" }),
                ErrorMessage: validation.FailureReason);
        }

        // Parse body
        string bodyText;
        try
        {
            bodyText = Encoding.UTF8.GetString(body);
        }
        catch
        {
            return new WebhookResult(
                Success: false,
                StatusCode: 400,
                ResponseBody: JsonSerializer.Serialize(new { error = "Invalid body encoding" }),
                ErrorMessage: "Body is not valid UTF-8");
        }

        if (string.IsNullOrWhiteSpace(bodyText))
        {
            return new WebhookResult(
                Success: false,
                StatusCode: 400,
                ResponseBody: JsonSerializer.Serialize(new { error = "Empty body" }),
                ErrorMessage: "Webhook body is empty");
        }

        // Route to platform-specific handler
        WebhookResult result;
        try
        {
            result = source switch
            {
                IoTSource.Alexa => await ProcessAlexaWebhookAsync(bodyText, ct),
                IoTSource.GoogleHome => await ProcessGoogleHomeWebhookAsync(bodyText, ct),
                IoTSource.SmartThings => await ProcessSmartThingsWebhookAsync(bodyText, ct),
                IoTSource.IFTTT => await ProcessIFTTTWebhookAsync(bodyText, ct),
                _ => await ProcessGenericWebhookAsync(source, bodyText, ct)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MOCK WEBHOOK] Processing error: Source={Source}", source);
            result = new WebhookResult(
                Success: false,
                StatusCode: 500,
                ResponseBody: JsonSerializer.Serialize(new { error = "Internal processing error" }),
                ErrorMessage: ex.Message);
        }

        // Update registration stats
        if (webhookId is not null && _registrations.TryGetValue(webhookId, out var reg))
        {
            var eventCount = _eventCounts.AddOrUpdate(webhookId, 1, (_, c) => c + 1);
            _registrations[webhookId] = reg with
            {
                LastReceivedAt = DateTime.UtcNow,
                EventCount = reg.EventCount + 1
            };
        }

        var duration = DateTime.UtcNow - startTime;
        return result with { ProcessingDuration = duration };
    }

    private async Task<WebhookResult> ProcessAlexaWebhookAsync(string body, CancellationToken ct)
    {
        // In production: parse Alexa request envelope, extract IntentRequest,
        // resolve slot values, trigger alert, return SSML response.
        _logger.LogInformation("[MOCK WEBHOOK ALEXA] Processing Alexa Skills Kit request");

        // Mock: try to parse as generic JSON with externalUserId
        var alertResult = await TriggerAlertFromWebhook(IoTSource.Alexa, body, "VOICE_COMMAND", "ECHO_DEVICE", ct);

        // Return Alexa-style SSML response
        var ssmlMessage = alertResult.Status == IoTAlertStatus.Dispatched
            ? $"<speak>Emergency alert sent. {alertResult.RespondersNotified} responders have been notified. Help is on the way.</speak>"
            : $"<speak>{alertResult.Message}</speak>";

        var alexaResponse = new
        {
            version = "1.0",
            response = new
            {
                outputSpeech = new { type = "SSML", ssml = ssmlMessage },
                shouldEndSession = true
            }
        };

        return new WebhookResult(
            Success: alertResult.Status == IoTAlertStatus.Dispatched,
            StatusCode: 200,
            ResponseBody: JsonSerializer.Serialize(alexaResponse),
            AlertId: alertResult.AlertId);
    }

    private async Task<WebhookResult> ProcessGoogleHomeWebhookAsync(string body, CancellationToken ct)
    {
        _logger.LogInformation("[MOCK WEBHOOK GOOGLE] Processing Google Actions fulfillment");

        var alertResult = await TriggerAlertFromWebhook(IoTSource.GoogleHome, body, "VOICE_COMMAND", "GOOGLE_HOME_DEVICE", ct);

        var googleResponse = new
        {
            fulfillmentText = alertResult.Message,
            payload = new
            {
                google = new
                {
                    expectUserResponse = false,
                    richResponse = new
                    {
                        items = new[]
                        {
                            new { simpleResponse = new { textToSpeech = alertResult.Message } }
                        }
                    }
                }
            }
        };

        return new WebhookResult(
            Success: alertResult.Status == IoTAlertStatus.Dispatched,
            StatusCode: 200,
            ResponseBody: JsonSerializer.Serialize(googleResponse),
            AlertId: alertResult.AlertId);
    }

    private async Task<WebhookResult> ProcessSmartThingsWebhookAsync(string body, CancellationToken ct)
    {
        _logger.LogInformation("[MOCK WEBHOOK SMARTTHINGS] Processing SmartApp lifecycle event");

        // SmartThings sends lifecycle events: PING, CONFIGURATION, INSTALL, UPDATE, EVENT, UNINSTALL
        // For PING, respond with challenge. For EVENT, process device events.
        if (body.Contains("\"lifecycle\":\"PING\"", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("\"lifecycle\": \"PING\"", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("[MOCK WEBHOOK SMARTTHINGS] Responding to PING challenge");
            return new WebhookResult(
                Success: true,
                StatusCode: 200,
                ResponseBody: JsonSerializer.Serialize(new { statusCode = 200, pingData = new { challenge = "mock-challenge" } }));
        }

        var alertResult = await TriggerAlertFromWebhook(IoTSource.SmartThings, body, "DEVICE_EVENT", "SMARTTHINGS_DEVICE", ct);

        return new WebhookResult(
            Success: alertResult.Status == IoTAlertStatus.Dispatched,
            StatusCode: 200,
            ResponseBody: JsonSerializer.Serialize(new { statusCode = 200 }),
            AlertId: alertResult.AlertId);
    }

    private async Task<WebhookResult> ProcessIFTTTWebhookAsync(string body, CancellationToken ct)
    {
        _logger.LogInformation("[MOCK WEBHOOK IFTTT] Processing IFTTT applet trigger");

        var alertResult = await TriggerAlertFromWebhook(IoTSource.IFTTT, body, "IFTTT_APPLET", "IFTTT_SERVICE", ct);

        // IFTTT expects { "data": [...] } response
        return new WebhookResult(
            Success: alertResult.Status == IoTAlertStatus.Dispatched,
            StatusCode: 200,
            ResponseBody: JsonSerializer.Serialize(new { data = new[] { new { id = alertResult.AlertId } } }),
            AlertId: alertResult.AlertId);
    }

    private async Task<WebhookResult> ProcessGenericWebhookAsync(IoTSource source, string body, CancellationToken ct)
    {
        _logger.LogInformation("[MOCK WEBHOOK GENERIC] Processing webhook from {Source}", source);

        var alertResult = await TriggerAlertFromWebhook(source, body, "CUSTOM_WEBHOOK", "CUSTOM_DEVICE", ct);

        return new WebhookResult(
            Success: alertResult.Status == IoTAlertStatus.Dispatched,
            StatusCode: 200,
            ResponseBody: JsonSerializer.Serialize(new { alertId = alertResult.AlertId, status = alertResult.Status.ToString() }),
            AlertId: alertResult.AlertId);
    }

    private async Task<IoTAlertResult> TriggerAlertFromWebhook(
        IoTSource source, string body, string triggerMethod, string deviceType, CancellationToken ct)
    {
        // Try to extract externalUserId from the JSON body
        string externalUserId = "unknown-webhook-user";
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("externalUserId", out var uid))
                externalUserId = uid.GetString() ?? externalUserId;
            else if (doc.RootElement.TryGetProperty("userId", out var uid2))
                externalUserId = uid2.GetString() ?? externalUserId;
            else if (doc.RootElement.TryGetProperty("user", out var userObj) &&
                     userObj.TryGetProperty("id", out var uid3))
                externalUserId = uid3.GetString() ?? externalUserId;
        }
        catch (JsonException)
        {
            _logger.LogWarning("[MOCK WEBHOOK] Could not parse body as JSON, using default user ID");
        }

        var request = new IoTAlertRequest(
            Source: source,
            ExternalUserId: externalUserId,
            TriggerMethod: triggerMethod,
            DeviceType: deviceType,
            Location: null,
            EmergencyPhrase: null,
            Scope: ResponseScope.CheckIn,
            Timestamp: DateTime.UtcNow);

        return await _alertPort.TriggerIoTAlertAsync(request, ct);
    }

    // ═══════════════════════════════════════════════════════════════
    // Signature Validation
    // ═══════════════════════════════════════════════════════════════

    public Task<WebhookSignatureValidation> ValidateWebhookSignatureAsync(
        IoTSource source, string? webhookId,
        IDictionary<string, string> headers, byte[] body,
        CancellationToken ct = default)
    {
        // Mock: always valid unless body is empty
        if (body.Length == 0)
        {
            return Task.FromResult(new WebhookSignatureValidation(
                IsValid: false,
                Source: source,
                WebhookId: webhookId,
                FailureReason: "Empty request body",
                RequestTimestamp: DateTime.UtcNow));
        }

        // Log warnings for missing platform-specific headers
        var missingHeader = source switch
        {
            IoTSource.Alexa when !headers.ContainsKey("Signature") && !headers.ContainsKey("signature") =>
                "Missing Alexa Signature header (prod would reject)",
            IoTSource.SmartThings when !headers.ContainsKey("X-ST-Signature") && !headers.ContainsKey("x-st-signature") =>
                "Missing SmartThings X-ST-Signature header (prod would reject)",
            IoTSource.IFTTT when !headers.ContainsKey("IFTTT-Service-Key") && !headers.ContainsKey("ifttt-service-key") =>
                "Missing IFTTT-Service-Key header (prod would reject)",
            IoTSource.GoogleHome when !headers.ContainsKey("Authorization") && !headers.ContainsKey("authorization") =>
                "Missing Google Authorization header (prod would reject)",
            _ => null
        };

        if (missingHeader is not null)
        {
            _logger.LogWarning("[MOCK WEBHOOK VALIDATION] {Warning}", missingHeader);
        }

        return Task.FromResult(new WebhookSignatureValidation(
            IsValid: true,
            Source: source,
            WebhookId: webhookId,
            RequestTimestamp: DateTime.UtcNow,
            RemoteIp: headers.TryGetValue("X-Forwarded-For", out var ip) ? ip : "127.0.0.1"));
    }

    // ═══════════════════════════════════════════════════════════════
    // Webhook Registration
    // ═══════════════════════════════════════════════════════════════

    public Task<WebhookRegistration> RegisterWebhookEndpointAsync(
        IoTSource source, string userId, string? sharedSecret = null,
        CancellationToken ct = default)
    {
        var webhookId = $"wh-{source.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}"[..24];
        var sourceSlug = source.ToString().ToLowerInvariant();

        var registration = new WebhookRegistration(
            WebhookId: webhookId,
            Source: source,
            UserId: userId,
            EndpointUrl: $"https://api.thewatch.app/api/iot/webhook/{sourceSlug}/{webhookId}",
            SharedSecret: sharedSecret,
            IsActive: true,
            CreatedAt: DateTime.UtcNow,
            LastReceivedAt: null);

        _registrations[webhookId] = registration;

        _logger.LogInformation(
            "[MOCK WEBHOOK] Registered: {WebhookId} for {Source}/{UserId} → {Url}",
            webhookId, source, userId, registration.EndpointUrl);

        return Task.FromResult(registration);
    }

    public Task<bool> DeactivateWebhookEndpointAsync(string webhookId, CancellationToken ct = default)
    {
        if (!_registrations.TryGetValue(webhookId, out var reg))
        {
            _logger.LogWarning("[MOCK WEBHOOK] Deactivate NOT FOUND: {WebhookId}", webhookId);
            return Task.FromResult(false);
        }

        _registrations[webhookId] = reg with { IsActive = false };
        _logger.LogInformation("[MOCK WEBHOOK] Deactivated: {WebhookId}", webhookId);
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<WebhookRegistration>> GetWebhookRegistrationsAsync(
        string userId, CancellationToken ct = default)
    {
        var regs = _registrations.Values
            .Where(r => r.UserId == userId)
            .OrderBy(r => r.Source)
            .ToList();

        return Task.FromResult<IReadOnlyList<WebhookRegistration>>(regs.AsReadOnly());
    }

    public Task<WebhookRegistration?> GetWebhookRegistrationAsync(
        string webhookId, CancellationToken ct = default)
    {
        _registrations.TryGetValue(webhookId, out var reg);
        return Task.FromResult(reg);
    }

    // ═══════════════════════════════════════════════════════════════
    // Test Helpers
    // ═══════════════════════════════════════════════════════════════

    public IReadOnlyDictionary<string, WebhookRegistration> GetAllRegistrations() => _registrations;
}
