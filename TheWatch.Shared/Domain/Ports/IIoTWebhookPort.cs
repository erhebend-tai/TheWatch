// =============================================================================
// IIoTWebhookPort — port interface for generic IoT webhook processing.
// =============================================================================
// Each IoT platform sends webhooks in different formats with different auth:
//
//   Alexa Skills Kit:
//     - POST with signed request body (X-Amz-Signature header)
//     - Signature chain validates against Alexa's public cert
//     - Request envelope contains IntentRequest, LaunchRequest, SessionEndedRequest
//     - Response must include SSML speech output + optional card/APL for Echo Show
//
//   Google Actions / Dialogflow:
//     - POST with Google-signed JWT in Authorization header
//     - Fulfillment webhook receives conv object with intent + parameters
//     - Response includes Simple Response, Cards, Suggestions
//
//   SmartThings SmartApp:
//     - POST with HMAC-SHA256 signature (X-ST-Signature header)
//     - Lifecycle events: INSTALL, UPDATE, EVENT, UNINSTALL, CONFIGURATION
//     - Must respond to PING with challenge within 2.5s
//
//   IFTTT:
//     - POST with IFTTT-Service-Key header
//     - Trigger fields defined in IFTTT service configuration
//     - Must respond with 200 + data array
//
//   Home Assistant:
//     - POST with Bearer token (long-lived access token)
//     - Automation webhook trigger with configurable payload
//
//   Custom Webhook:
//     - POST with HMAC-SHA256 or API key header
//     - Payload schema defined by user in TheWatch webhook configuration
//
// Example: Alexa Smart Home Skill Discovery
//   Alexa sends: { "directive": { "header": { "namespace": "Alexa.Discovery", ... } } }
//   We respond with: list of virtual devices (panic button, status checker)
//
// Example: SmartThings lifecycle
//   SmartThings sends: { "lifecycle": "EVENT", "eventData": { "events": [...] } }
//   We process device events and may trigger alerts
//
// WAL: Webhook signatures MUST be validated before processing.
//      Replay attacks are prevented by checking timestamp freshness (< 150s for Alexa).
//      All webhook payloads are logged to audit trail (PII redacted).
// =============================================================================

namespace TheWatch.Shared.Domain.Ports;

// ═══════════════════════════════════════════════════════════════
// Webhook Models
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Registration for an inbound webhook endpoint.
/// Created when a user sets up a custom integration or when an IoT platform
/// completes the SmartApp/Skill installation flow.
/// </summary>
public record WebhookRegistration(
    /// <summary>Unique webhook endpoint ID — used in the URL path.</summary>
    string WebhookId,

    /// <summary>Which IoT platform this webhook serves.</summary>
    IoTSource Source,

    /// <summary>The TheWatch user who owns this webhook.</summary>
    string UserId,

    /// <summary>
    /// The full webhook URL that the platform should POST to.
    /// e.g., "https://api.thewatch.app/api/iot/webhook/alexa/{webhookId}"
    /// </summary>
    string EndpointUrl,

    /// <summary>
    /// The shared secret used to validate webhook signatures.
    /// HMAC-SHA256 for SmartThings/Custom, API key for IFTTT.
    /// Null for platforms that use their own cert-based validation (Alexa, Google).
    /// </summary>
    string? SharedSecret,

    /// <summary>Whether this webhook is currently active and processing events.</summary>
    bool IsActive,

    DateTime CreatedAt,
    DateTime? LastReceivedAt,

    /// <summary>Number of events processed through this webhook.</summary>
    long EventCount = 0,

    /// <summary>Number of failed signature validations (potential attack indicator).</summary>
    long FailedValidationCount = 0
);

/// <summary>
/// Result of processing a webhook payload.
/// </summary>
public record WebhookResult(
    /// <summary>Whether the webhook was successfully processed.</summary>
    bool Success,

    /// <summary>HTTP status code to return to the calling platform.</summary>
    int StatusCode,

    /// <summary>Response body to return (may include SSML for Alexa, JSON for others).</summary>
    string? ResponseBody,

    /// <summary>If the webhook resulted in an alert, the alert ID.</summary>
    string? AlertId = null,

    /// <summary>Error message if processing failed.</summary>
    string? ErrorMessage = null,

    /// <summary>Processing duration for performance monitoring.</summary>
    TimeSpan? ProcessingDuration = null
);

/// <summary>
/// Signature validation result — includes forensic details for failed attempts.
/// </summary>
public record WebhookSignatureValidation(
    bool IsValid,
    IoTSource Source,
    string? WebhookId,

    /// <summary>Reason for failure if invalid.</summary>
    string? FailureReason = null,

    /// <summary>Timestamp from the request (for replay detection).</summary>
    DateTime? RequestTimestamp = null,

    /// <summary>Whether this looks like a replay attack (timestamp too old).</summary>
    bool SuspectedReplay = false,

    /// <summary>Remote IP for audit logging.</summary>
    string? RemoteIp = null
);

// ═══════════════════════════════════════════════════════════════
// Port Interface
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Port for generic webhook processing across all IoT platforms.
/// Adapters: MockIoTWebhookAdapter (dev), platform-specific adapters (prod).
///
/// Production adapters will implement platform-specific validation:
///   - Alexa: verify request signing certificate chain
///   - Google: validate JWT with Google's public keys
///   - SmartThings: HMAC-SHA256 with app secret
///   - IFTTT: validate IFTTT-Service-Key
///   - Custom: HMAC-SHA256 with user-configured shared secret
/// </summary>
public interface IIoTWebhookPort
{
    /// <summary>
    /// Process a raw webhook payload from an IoT platform.
    /// Validates signature, parses platform-specific format, and routes to appropriate handler.
    /// </summary>
    /// <param name="source">Which platform sent this webhook.</param>
    /// <param name="webhookId">The registered webhook ID (from URL path).</param>
    /// <param name="headers">HTTP headers (contains signatures, content-type, etc.).</param>
    /// <param name="body">Raw request body bytes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result including response body to return to the platform.</returns>
    Task<WebhookResult> ProcessWebhookAsync(
        IoTSource source,
        string? webhookId,
        IDictionary<string, string> headers,
        byte[] body,
        CancellationToken ct = default);

    /// <summary>
    /// Validate a webhook signature without processing the payload.
    /// Used for security auditing and pre-flight checks.
    /// </summary>
    Task<WebhookSignatureValidation> ValidateWebhookSignatureAsync(
        IoTSource source,
        string? webhookId,
        IDictionary<string, string> headers,
        byte[] body,
        CancellationToken ct = default);

    /// <summary>
    /// Register a new webhook endpoint for an IoT platform.
    /// Returns the full endpoint URL to configure in the platform's developer console.
    /// </summary>
    Task<WebhookRegistration> RegisterWebhookEndpointAsync(
        IoTSource source,
        string userId,
        string? sharedSecret = null,
        CancellationToken ct = default);

    /// <summary>
    /// Deactivate a webhook endpoint. Stops processing events but preserves audit trail.
    /// </summary>
    Task<bool> DeactivateWebhookEndpointAsync(
        string webhookId,
        CancellationToken ct = default);

    /// <summary>
    /// Get all webhook registrations for a user.
    /// </summary>
    Task<IReadOnlyList<WebhookRegistration>> GetWebhookRegistrationsAsync(
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Get a specific webhook registration by ID.
    /// </summary>
    Task<WebhookRegistration?> GetWebhookRegistrationAsync(
        string webhookId,
        CancellationToken ct = default);
}
