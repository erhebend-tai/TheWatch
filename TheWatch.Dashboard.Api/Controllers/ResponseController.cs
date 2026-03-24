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

using Microsoft.AspNetCore.Mvc;
using TheWatch.Dashboard.Api.Services;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Dashboard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ResponseController : ControllerBase
{
    private readonly IResponseCoordinationService _coordinationService;
    private readonly IParticipationPort _participationPort;
    private readonly ILogger<ResponseController> _logger;

    public ResponseController(
        IResponseCoordinationService coordinationService,
        IParticipationPort participationPort,
        ILogger<ResponseController> logger)
    {
        _coordinationService = coordinationService;
        _participationPort = participationPort;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────
    // SOS Trigger
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Initiates a new SOS response. Called by mobile clients when an SOS is triggered
    /// (phrase detection, quick-tap, or manual button press).
    /// </summary>
    [HttpPost("trigger")]
    public async Task<IActionResult> TriggerResponse(
        [FromBody] TriggerResponseRequest request,
        CancellationToken ct)
    {
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
