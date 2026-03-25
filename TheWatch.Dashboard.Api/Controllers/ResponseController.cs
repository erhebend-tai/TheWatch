// ResponseController — REST endpoints for the SOS response coordination pipeline.
// Mobile clients and the Azure Functions webhook receiver both drive this controller.
//
// Endpoints:
//   POST /api/response/trigger      — Create a new SOS response (from mobile trigger)
//   POST /api/response/{id}/ack     — Record a responder acknowledgment
//   POST /api/response/{id}/cancel  — Cancel an active response ("I'm OK")
//   POST /api/response/{id}/resolve — Mark a response as resolved
//   GET  /api/response/{id}         — Get current situation (request + acks + escalation)
//   GET  /api/response/active/{uid} — Get all active responses for a user
//   GET  /api/response/participation/{uid} — Get participation preferences
//   PUT  /api/response/participation       — Update participation preferences
//
// WAL: All heavy lifting delegated to IResponseCoordinationService.
//      Controller is thin — validation + delegation only.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TheWatch.Dashboard.Api.Auth;
using TheWatch.Dashboard.Api.Services;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Dashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ResponseController : ControllerBase
{
    private readonly IResponseCoordinationService _coordinationService;
    private readonly IParticipationPort _participationPort;
    private readonly SosBypassTokenService _sosBypass;
    private readonly ILogger<ResponseController> _logger;

    public ResponseController(
        IResponseCoordinationService coordinationService,
        IParticipationPort participationPort,
        SosBypassTokenService sosBypass,
        ILogger<ResponseController> logger)
    {
        _coordinationService = coordinationService;
        _participationPort = participationPort;
        _sosBypass = sosBypass;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────
    // SOS Bypass Token
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Issues a short-lived SOS bypass token. Mobile apps should call this
    /// after authentication and cache the token locally. The token allows
    /// SOS triggers even when the primary auth token (Firebase) has expired.
    /// </summary>
    [HttpPost("sos-token")]
    public IActionResult IssueSosToken()
    {
        var uid = User.FindFirst("uid")?.Value;
        if (string.IsNullOrEmpty(uid))
            return Unauthorized(new { error = "User identity not found" });

        var token = _sosBypass.GenerateToken(uid);
        return Ok(new { token, expiresIn = "24h" });
    }

    // ─────────────────────────────────────────────────────────────
    // SOS Trigger
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Initiates a new SOS response. Called by mobile clients when an SOS is triggered
    /// (phrase detection, quick-tap, or manual button press).
    /// Accepts either: full Firebase auth (Bearer token) OR X-SOS-Bypass-Token header.
    /// </summary>
    [HttpPost("trigger")]
    [AllowAnonymous] // Auth handled manually — accepts both full auth and SOS bypass tokens
    public async Task<IActionResult> TriggerResponse(
        [FromBody] TriggerResponseRequest request,
        CancellationToken ct)
    {
        // Validate identity: prefer full auth, fall back to SOS bypass token
        var uid = User.FindFirst("uid")?.Value;
        if (string.IsNullOrEmpty(uid))
        {
            var bypassToken = Request.Headers["X-SOS-Bypass-Token"].ToString();
            if (!string.IsNullOrEmpty(bypassToken))
            {
                var (valid, bypassUid) = _sosBypass.ValidateToken(bypassToken);
                if (valid && !string.IsNullOrEmpty(bypassUid))
                {
                    uid = bypassUid;
                    _logger.LogWarning("SOS trigger via bypass token for {Uid}", uid);
                }
            }
        }

        // If neither auth method worked, still allow with the request's UserId
        // (life-safety: never block an SOS) but log the anomaly
        if (string.IsNullOrEmpty(uid))
        {
            uid = request.UserId;
            _logger.LogWarning("SOS trigger with NO authentication for UserId={UserId} — allowing for life-safety", request.UserId);
        }

        if (string.IsNullOrWhiteSpace(request.UserId))
            return BadRequest(new { error = "UserId is required" });

        _logger.LogWarning(
            "SOS TRIGGER received: UserId={UserId}, Scope={Scope}, Source={Source}",
            request.UserId, request.Scope, request.TriggerSource);

        var response = await _coordinationService.CreateResponseAsync(
            request.UserId,
            request.Scope,
            request.Latitude,
            request.Longitude,
            request.Description,
            request.TriggerSource,
            ct);

        return Accepted(new
        {
            response.RequestId,
            Scope = response.Scope.ToString(),
            Strategy = response.Strategy.ToString(),
            Escalation = response.Escalation.ToString(),
            Status = response.Status.ToString(),
            response.RadiusMeters,
            response.DesiredResponderCount,
            response.CreatedAt
        });
    }

    // ─────────────────────────────────────────────────────────────
    // Responder Acknowledgment
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Records a responder's acknowledgment. Called when a responder taps
    /// "I'm on my way" in their notification or app.
    /// </summary>
    [HttpPost("{requestId}/ack")]
    public async Task<IActionResult> AcknowledgeResponse(
        string requestId,
        [FromBody] AcknowledgeRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ResponderId))
            return BadRequest(new { error = "ResponderId is required" });

        var result = await _coordinationService.AcknowledgeResponseAsync(
            requestId,
            request.ResponderId,
            request.ResponderName ?? "Unknown",
            request.ResponderRole ?? "VOLUNTEER",
            request.Latitude,
            request.Longitude,
            request.DistanceMeters,
            request.HasVehicle,
            request.EstimatedArrivalMinutes,
            ct);

        return Ok(new
        {
            result.Acknowledgment.AckId,
            result.Acknowledgment.RequestId,
            result.Acknowledgment.ResponderId,
            Status = result.Acknowledgment.Status.ToString(),
            result.Acknowledgment.EstimatedArrival,
            // Navigation directions — mobile clients use these to launch turn-by-turn nav
            Directions = new
            {
                result.Directions.TravelMode,
                result.Directions.DistanceMeters,
                result.Directions.EstimatedTravelTime,
                result.Directions.GoogleMapsUrl,
                result.Directions.AppleMapsUrl,
                result.Directions.WazeUrl
            }
        });
    }

    // ─────────────────────────────────────────────────────────────
    // Cancel / Resolve
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Cancels an active response. Called when the user presses "I'm OK",
    /// speaks a clear word, or the situation is resolved before responders arrive.
    /// </summary>
    [HttpPost("{requestId}/cancel")]
    public async Task<IActionResult> CancelResponse(
        string requestId,
        [FromBody] CancelRequest request,
        CancellationToken ct)
    {
        var cancelled = await _coordinationService.CancelResponseAsync(
            requestId,
            request.Reason ?? "User cancelled",
            ct);

        return Ok(new
        {
            cancelled.RequestId,
            Status = cancelled.Status.ToString()
        });
    }

    /// <summary>
    /// Marks a response as resolved. Called when the situation has been handled
    /// (responders arrived, emergency services on scene, etc.).
    /// </summary>
    [HttpPost("{requestId}/resolve")]
    public async Task<IActionResult> ResolveResponse(
        string requestId,
        [FromBody] ResolveRequest request,
        CancellationToken ct)
    {
        var resolved = await _coordinationService.ResolveResponseAsync(
            requestId,
            request.ResolvedBy ?? "system",
            ct);

        return Ok(new
        {
            resolved.RequestId,
            Status = resolved.Status.ToString()
        });
    }

    // ─────────────────────────────────────────────────────────────
    // Query
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Gets the full situation for a response — request details, all responder
    /// acknowledgments, and escalation history. Used by the dashboard sitrep view.
    /// </summary>
    [HttpGet("{requestId}")]
    public async Task<IActionResult> GetSituation(string requestId, CancellationToken ct)
    {
        var situation = await _coordinationService.GetSituationAsync(requestId, ct);
        if (situation is null)
            return NotFound(new { error = $"Response {requestId} not found" });

        return Ok(situation);
    }

    /// <summary>
    /// Gets all active responses for a user. Used by the mobile app to show
    /// in-progress SOS requests on the home screen.
    /// </summary>
    [HttpGet("active/{userId}")]
    public async Task<IActionResult> GetActiveResponses(string userId, CancellationToken ct)
    {
        var responses = await _coordinationService.GetActiveResponsesAsync(userId, ct);
        return Ok(responses);
    }

    // ─────────────────────────────────────────────────────────────
    // Participation Preferences
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Gets a user's participation preferences (opt-in/out per scope, availability,
    /// certifications, quiet hours, etc.).
    /// </summary>
    [HttpGet("participation/{userId}")]
    public async Task<IActionResult> GetParticipation(string userId, CancellationToken ct)
    {
        var prefs = await _participationPort.GetPreferencesAsync(userId, ct);
        if (prefs is null)
            return NotFound(new { error = $"No participation preferences for user {userId}" });

        return Ok(prefs);
    }

    /// <summary>
    /// Updates a user's participation preferences.
    /// </summary>
    [HttpPut("participation")]
    public async Task<IActionResult> UpdateParticipation(
        [FromBody] ParticipationPreferences prefs,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(prefs.UserId))
            return BadRequest(new { error = "UserId is required" });

        var updated = await _participationPort.UpdatePreferencesAsync(prefs, ct);
        return Ok(updated);
    }

    /// <summary>
    /// Sets a user's current availability (available/unavailable with optional duration).
    /// Quick toggle from the mobile app.
    /// </summary>
    [HttpPost("participation/{userId}/availability")]
    public async Task<IActionResult> SetAvailability(
        string userId,
        [FromBody] AvailabilityRequest request,
        CancellationToken ct)
    {
        await _participationPort.SetAvailabilityAsync(
            userId, request.IsAvailable, request.Duration, ct);

        return Ok(new { userId, request.IsAvailable, request.Duration });
    }

    // ─────────────────────────────────────────────────────────────
    // Responder Communication
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Send a message in an incident's responder channel.
    /// Messages are filtered through server-side guardrails before delivery.
    /// Only acknowledged responders for this incident can send messages.
    ///
    /// Guardrails pipeline:
    ///   1. Rate limit (30 msg/min)
    ///   2. PII detection → auto-redact SSN, phone, email, credit card
    ///   3. Profanity filter → block
    ///   4. Threat detection → block
    /// </summary>
    [HttpPost("{requestId}/messages")]
    public async Task<IActionResult> SendMessage(
        string requestId,
        [FromBody] SendMessageRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.SenderId))
            return BadRequest(new { error = "SenderId is required" });
        if (string.IsNullOrWhiteSpace(request.Content) && request.MessageType != ResponderMessageType.LocationShare)
            return BadRequest(new { error = "Content is required" });

        var (message, guardrails) = await _coordinationService.SendResponderMessageAsync(
            requestId,
            request.SenderId,
            request.SenderName ?? "Unknown",
            request.SenderRole,
            request.MessageType,
            request.Content ?? "",
            request.Latitude,
            request.Longitude,
            request.QuickResponseCode,
            ct);

        // Return the guardrails verdict to the sender so they can show appropriate UI
        return Ok(new
        {
            message.MessageId,
            message.RequestId,
            message.SenderId,
            MessageType = message.MessageType.ToString(),
            Verdict = guardrails.Verdict.ToString(),
            guardrails.Reason,
            guardrails.RedactedContent,
            guardrails.PiiDetected,
            guardrails.PiiTypes,
            guardrails.ProfanityDetected,
            guardrails.ThreatDetected,
            guardrails.RateLimited,
            guardrails.MessagesSentInWindow,
            guardrails.RateLimitMax,
            message.SentAt
        });
    }

    /// <summary>
    /// Get message history for an incident's responder channel.
    /// Only returns messages that passed guardrails (Approved or Redacted).
    /// </summary>
    [HttpGet("{requestId}/messages")]
    public async Task<IActionResult> GetMessages(
        string requestId,
        [FromQuery] int limit = 100,
        [FromQuery] DateTime? since = null,
        CancellationToken ct = default)
    {
        var messages = await _coordinationService.GetResponderMessagesAsync(requestId, limit, since, ct);
        return Ok(messages.Select(m => new
        {
            m.MessageId,
            m.RequestId,
            m.SenderId,
            m.SenderName,
            m.SenderRole,
            MessageType = m.MessageType.ToString(),
            // Serve redacted content if the message was redacted, original otherwise
            Content = m.Verdict == GuardrailsVerdict.Redacted ? m.RedactedContent ?? m.Content : m.Content,
            m.Latitude,
            m.Longitude,
            m.QuickResponseCode,
            Verdict = m.Verdict.ToString(),
            m.SentAt
        }));
    }

    /// <summary>
    /// Get available quick responses — pre-defined safe messages that responders
    /// can send with one tap (e.g., "On my way", "Need medical", "All clear").
    /// </summary>
    [HttpGet("quick-responses")]
    public IActionResult GetQuickResponses()
    {
        var responses = _coordinationService.GetQuickResponses();
        return Ok(responses.Select(r => new
        {
            r.Code,
            r.DisplayText,
            r.Category
        }));
    }
}

// ─────────────────────────────────────────────────────────────
// Request DTOs
// ─────────────────────────────────────────────────────────────

public record TriggerResponseRequest(
    string UserId,
    ResponseScope Scope,
    double Latitude,
    double Longitude,
    string? Description = null,
    string? TriggerSource = null  // "PHRASE", "QUICK_TAP", "MANUAL_BUTTON"
);

public record AcknowledgeRequest(
    string ResponderId,
    string? ResponderName = null,
    string? ResponderRole = null,
    double Latitude = 0,
    double Longitude = 0,
    double DistanceMeters = 0,
    bool HasVehicle = true,
    int? EstimatedArrivalMinutes = null
);

public record CancelRequest(string? Reason = null);
public record ResolveRequest(string? ResolvedBy = null);
public record AvailabilityRequest(bool IsAvailable, TimeSpan? Duration = null);

public record SendMessageRequest(
    string SenderId,
    string? SenderName = null,
    string? SenderRole = null,
    ResponderMessageType MessageType = ResponderMessageType.Text,
    string? Content = null,
    double? Latitude = null,
    double? Longitude = null,
    string? QuickResponseCode = null  // "ON_MY_WAY", "NEED_MEDICAL", "ALL_CLEAR", etc.
);
