// =============================================================================
// WatchCallController — REST endpoints for live-video Watch Call lifecycle.
// =============================================================================
// Endpoints:
//   POST /api/watchcall/initiate                      — Start a new watch call
//   POST /api/watchcall/{sessionId}/join               — Join an existing call
//   POST /api/watchcall/{sessionId}/end                — End a call (resolve)
//   GET  /api/watchcall/{sessionId}                    — Get call status
//   GET  /api/watchcall/{sessionId}/narration           — Get AI scene narration transcript
//   POST /api/watchcall/{sessionId}/narrate             — Submit a frame for narration
//   POST /api/watchcall/{sessionId}/escalate            — Escalate to SOS
//
//   POST /api/watchcall/enroll                          — Enroll in Watch Call program
//   GET  /api/watchcall/enrollment/{userId}             — Get enrollment status
//   GET  /api/watchcall/ice-servers                     — Get STUN/TURN config for WebRTC
//
//   GET  /api/watchcall/scenarios                       — Get mock training scenarios
//   POST /api/watchcall/mock/start                      — Start a mock training call
//   POST /api/watchcall/mock/{callId}/complete          — Complete a mock training call
//
// Design:
//   - Thin controller: validation + delegation to IWatchCallService
//   - All external deps behind ports (adapter pattern)
//   - WebRTC signaling runs through SignalR (DashboardHub), not REST
//   - Video frames submitted as base64 or multipart/form-data
//   - Scene narration is neutral, factual, bias-free (see ISceneNarrationPort)
//
// Progressive escalation:
//   Guard Report → Watch Call → SOS ResponseRequest
//   Guard reports can initiate a watch call via LinkedGuardReportId.
//   Watch calls can escalate to a full SOS via the /escalate endpoint.
//   Each step is audit-trailed with a shared CorrelationId.
//
// WAL: No raw video is stored. Only narration text persists. Frame bytes are
//      processed in-memory by the narration port and immediately discarded.
// =============================================================================

using Microsoft.AspNetCore.Mvc;
using TheWatch.Dashboard.Api.Services;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Dashboard.Api.Controllers;

[ApiController]
[Route("api/watchcall")]
public class WatchCallController : ControllerBase
{
    private readonly IWatchCallService _watchCallService;
    private readonly ILogger<WatchCallController> _logger;

    public WatchCallController(
        IWatchCallService watchCallService,
        ILogger<WatchCallController> logger)
    {
        _watchCallService = watchCallService;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────
    // Call Lifecycle
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// POST /api/watchcall/initiate
    /// Start a new Watch Call. Creates a session, notifies nearby enrolled watchers,
    /// and returns signaling info (call ID, ICE servers, participant alias).
    ///
    /// Example request:
    ///   POST /api/watchcall/initiate
    ///   {
    ///     "initiatorUserId": "user-123",
    ///     "latitude": 30.2672,
    ///     "longitude": -97.7431,
    ///     "radiusMeters": 2000,
    ///     "description": "Unusual activity near loading dock"
    ///   }
    ///
    /// Example response:
    ///   {
    ///     "callId": "call-abc123",
    ///     "status": "Requested",
    ///     "iceServers": [{ "urls": ["stun:stun.l.google.com:19302"] }],
    ///     "signalingGroup": "watchcall-call-abc123"
    ///   }
    /// </summary>
    [HttpPost("initiate")]
    public async Task<IActionResult> InitiateCall(
        [FromBody] InitiateWatchCallRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.InitiatorUserId))
            return BadRequest(new { error = "InitiatorUserId is required" });
        if (request.Latitude == 0 && request.Longitude == 0)
            return BadRequest(new { error = "Valid latitude and longitude are required" });

        _logger.LogInformation(
            "Watch Call initiation requested by {UserId} at ({Lat}, {Lng})",
            request.InitiatorUserId, request.Latitude, request.Longitude);

        var call = await _watchCallService.InitiateCallAsync(request, ct);
        var iceServers = await _watchCallService.GetIceServersAsync(ct);

        return Created($"/api/watchcall/{call.CallId}", new
        {
            call.CallId,
            Status = call.Status.ToString(),
            call.InitiatorUserId,
            call.Latitude,
            call.Longitude,
            call.RadiusMeters,
            call.MaxParticipants,
            call.RecordingEnabled,
            call.LinkedGuardReportId,
            call.RequestedAt,
            IceServers = iceServers,
            SignalingGroup = $"watchcall-{call.CallId}",
            Message = "Watch Call created. Nearby watchers are being notified. " +
                      "Join the SignalR group to begin WebRTC signaling."
        });
    }

    /// <summary>
    /// POST /api/watchcall/{sessionId}/join
    /// Join an existing Watch Call as a participant. Returns anonymized alias
    /// and peer connection info for WebRTC setup.
    ///
    /// Example request:
    ///   POST /api/watchcall/call-abc123/join
    ///   { "userId": "user-456" }
    ///
    /// Example response:
    ///   {
    ///     "anonymizedAlias": "Watcher 2",
    ///     "peerConnectionId": "peer-def789",
    ///     "iceServers": [...]
    ///   }
    /// </summary>
    [HttpPost("{sessionId}/join")]
    public async Task<IActionResult> JoinCall(
        string sessionId, [FromBody] JoinWatchCallRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
            return BadRequest(new { error = "UserId is required" });

        var call = await _watchCallService.GetCallStatusAsync(sessionId, ct);
        if (call is null)
            return NotFound(new { error = $"Watch Call {sessionId} not found" });

        if (call.Status is WatchCallStatus.Resolved or WatchCallStatus.Escalated or WatchCallStatus.Expired)
            return Conflict(new { error = $"Watch Call {sessionId} is {call.Status} and cannot be joined" });

        if (call.Participants.Count(p => p.LeftAt == null) >= call.MaxParticipants)
            return Conflict(new { error = "Watch Call is at maximum participant capacity" });

        var participant = await _watchCallService.JoinCallAsync(sessionId, request.UserId, ct);
        var iceServers = await _watchCallService.GetIceServersAsync(ct);

        return Ok(new
        {
            CallId = sessionId,
            participant.AnonymizedAlias,
            participant.PeerConnectionId,
            participant.JoinedAt,
            participant.IsVideoEnabled,
            participant.IsAudioEnabled,
            IceServers = iceServers,
            SignalingGroup = $"watchcall-{sessionId}",
            Message = $"Joined Watch Call as {participant.AnonymizedAlias}. " +
                      "Connect to SignalR group for WebRTC signaling."
        });
    }

    /// <summary>
    /// POST /api/watchcall/{sessionId}/end
    /// End a Watch Call. Marks it as resolved. All participants are notified.
    ///
    /// Example request:
    ///   POST /api/watchcall/call-abc123/end
    ///   { "resolution": "safe", "endedBy": "user-123" }
    /// </summary>
    [HttpPost("{sessionId}/end")]
    public async Task<IActionResult> EndCall(
        string sessionId, [FromBody] EndWatchCallRequest request, CancellationToken ct)
    {
        var call = await _watchCallService.GetCallStatusAsync(sessionId, ct);
        if (call is null)
            return NotFound(new { error = $"Watch Call {sessionId} not found" });

        if (call.Status is WatchCallStatus.Resolved or WatchCallStatus.Escalated)
            return Conflict(new { error = $"Watch Call {sessionId} is already {call.Status}" });

        var resolved = await _watchCallService.EndCallAsync(
            sessionId, request.Resolution ?? "ended_by_user", ct);

        return Ok(new
        {
            resolved.CallId,
            Status = resolved.Status.ToString(),
            resolved.Resolution,
            resolved.ResolvedAt,
            NarrationLines = resolved.NarrationTranscript.Count,
            ParticipantCount = resolved.Participants.Count,
            Message = "Watch Call ended."
        });
    }

    /// <summary>
    /// GET /api/watchcall/{sessionId}
    /// Get the current status and details of a Watch Call.
    /// </summary>
    [HttpGet("{sessionId}")]
    public async Task<IActionResult> GetCallStatus(string sessionId, CancellationToken ct)
    {
        var call = await _watchCallService.GetCallStatusAsync(sessionId, ct);
        if (call is null)
            return NotFound(new { error = $"Watch Call {sessionId} not found" });

        return Ok(new
        {
            call.CallId,
            Status = call.Status.ToString(),
            call.IsMockCall,
            call.InitiatorUserId,
            call.Latitude,
            call.Longitude,
            call.RadiusMeters,
            call.MaxParticipants,
            call.RecordingEnabled,
            call.LinkedGuardReportId,
            call.LinkedRequestId,
            call.RequestedAt,
            call.ConnectedAt,
            call.ResolvedAt,
            call.EscalatedAt,
            call.Resolution,
            call.EscalatedRequestId,
            ActiveParticipants = call.Participants
                .Where(p => p.LeftAt == null)
                .Select(p => new
                {
                    p.AnonymizedAlias,
                    p.PeerConnectionId,
                    p.JoinedAt,
                    p.IsVideoEnabled,
                    p.IsAudioEnabled
                }),
            NarrationLineCount = call.NarrationTranscript.Count,
            LatestNarration = call.NarrationTranscript.LastOrDefault()?.NarrationText
        });
    }

    /// <summary>
    /// GET /api/watchcall/{sessionId}/narration
    /// Get the full AI scene narration transcript for a Watch Call.
    /// Returns timestamped narration lines in chronological order.
    ///
    /// Example response:
    ///   [
    ///     { "offset": "00:03", "narrationText": "I see one person standing near the sidewalk..." },
    ///     { "offset": "00:08", "narrationText": "The person is carrying a bag..." }
    ///   ]
    /// </summary>
    [HttpGet("{sessionId}/narration")]
    public async Task<IActionResult> GetNarration(string sessionId, CancellationToken ct)
    {
        var call = await _watchCallService.GetCallStatusAsync(sessionId, ct);
        if (call is null)
            return NotFound(new { error = $"Watch Call {sessionId} not found" });

        var transcript = await _watchCallService.GetNarrationTranscriptAsync(sessionId, ct);

        return Ok(new
        {
            CallId = sessionId,
            Status = call.Status.ToString(),
            NarrationProvider = "AI Scene Narrator",
            LineCount = transcript.Count,
            Transcript = transcript.Select(n => new
            {
                Offset = n.Offset.ToString(@"mm\:ss"),
                n.NarrationText
            })
        });
    }

    /// <summary>
    /// POST /api/watchcall/{sessionId}/narrate
    /// Submit a video frame for AI narration. The frame is processed by the
    /// ISceneNarrationPort (GPT-4o vision in production, mock in dev) and
    /// the resulting neutral, factual narration is broadcast to all participants.
    ///
    /// Accepts either:
    ///   - JSON body with base64-encoded frame: { "frameBase64": "..." }
    ///   - multipart/form-data with file upload (key: "frame")
    ///
    /// Rate: 1 frame per 2 seconds max (client should throttle).
    ///
    /// WAL: Frame bytes are processed in-memory and never stored.
    /// </summary>
    [HttpPost("{sessionId}/narrate")]
    public async Task<IActionResult> SubmitFrame(
        string sessionId, [FromBody] SubmitFrameRequest request, CancellationToken ct)
    {
        var call = await _watchCallService.GetCallStatusAsync(sessionId, ct);
        if (call is null)
            return NotFound(new { error = $"Watch Call {sessionId} not found" });

        if (call.Status is not (WatchCallStatus.Active or WatchCallStatus.Narrating or WatchCallStatus.Connecting))
            return Conflict(new { error = $"Watch Call {sessionId} is {call.Status}, cannot accept frames" });

        if (string.IsNullOrWhiteSpace(request.FrameBase64))
            return BadRequest(new { error = "FrameBase64 is required" });

        byte[] frameData;
        try
        {
            frameData = Convert.FromBase64String(request.FrameBase64);
        }
        catch (FormatException)
        {
            return BadRequest(new { error = "Invalid base64-encoded frame data" });
        }

        if (frameData.Length < 100)
            return BadRequest(new { error = "Frame data too small to be a valid image" });
        if (frameData.Length > 5_000_000) // 5MB max per frame
            return BadRequest(new { error = "Frame exceeds 5MB limit" });

        var narration = await _watchCallService.SubmitFrameAsync(sessionId, frameData, ct);

        return Ok(new
        {
            CallId = sessionId,
            narration.NarrationText,
            Confidence = narration.Confidence.ToString(),
            narration.SceneChanged,
            narration.PeopleCount,
            narration.VehicleCount,
            narration.EnvironmentNote,
            narration.EscalationHint,
            narration.EscalationHintReason,
            narration.LatencyMs,
            narration.Timestamp,
            Message = narration.EscalationHint
                ? $"ESCALATION SUGGESTED: {narration.EscalationHintReason}"
                : "Frame processed. Narration broadcast to participants."
        });
    }

    /// <summary>
    /// POST /api/watchcall/{sessionId}/escalate
    /// Escalate a Watch Call to a full SOS ResponseRequest. This triggers:
    ///   1. Watch Call status → Escalated
    ///   2. ResponseRequest created via IResponseCoordinationService
    ///   3. Nearby responders dispatched with narration transcript as context
    ///   4. All participants notified of escalation
    ///   5. Audit trail entry with Critical severity
    ///
    /// Example request:
    ///   POST /api/watchcall/call-abc123/escalate
    ///   { "escalatedBy": "user-123", "reason": "Person appears injured, not moving" }
    ///
    /// Example response:
    ///   {
    ///     "callId": "call-abc123",
    ///     "responseRequestId": "req-xyz789",
    ///     "message": "Watch Call escalated to SOS. Responders are being dispatched."
    ///   }
    /// </summary>
    [HttpPost("{sessionId}/escalate")]
    public async Task<IActionResult> EscalateCall(
        string sessionId, [FromBody] EscalateWatchCallRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.EscalatedBy))
            return BadRequest(new { error = "EscalatedBy is required" });

        var call = await _watchCallService.GetCallStatusAsync(sessionId, ct);
        if (call is null)
            return NotFound(new { error = $"Watch Call {sessionId} not found" });

        if (call.Status == WatchCallStatus.Escalated)
            return Conflict(new { error = "Watch Call is already escalated", call.EscalatedRequestId });

        if (call.Status == WatchCallStatus.Resolved)
            return Conflict(new { error = "Watch Call is already resolved" });

        _logger.LogWarning(
            "WATCH CALL ESCALATION: {SessionId} by {UserId}. Reason: {Reason}",
            sessionId, request.EscalatedBy, request.Reason);

        var result = await _watchCallService.EscalateCallAsync(
            sessionId, request.EscalatedBy, request.Reason, ct);

        return Ok(new
        {
            CallId = sessionId,
            Status = result.EscalatedCall.Status.ToString(),
            ResponseRequestId = result.ResponseRequest.RequestId,
            EscalatedBy = request.EscalatedBy,
            Reason = request.Reason,
            result.EscalatedCall.EscalatedAt,
            NarrationLinesAttached = result.EscalatedCall.NarrationTranscript.Count,
            result.Message
        });
    }

    // ─────────────────────────────────────────────────────────────
    // Enrollment
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// POST /api/watchcall/enroll
    /// Enroll a user in the Watch Call program. New enrollments start with
    /// PendingTraining status — the user must complete at least one mock call
    /// before participating in live calls.
    /// </summary>
    [HttpPost("enroll")]
    public async Task<IActionResult> Enroll(
        [FromBody] EnrollWatchCallRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
            return BadRequest(new { error = "UserId is required" });

        var enrollment = new WatchCallEnrollment(
            EnrollmentId: "",
            UserId: request.UserId,
            DisplayAlias: "",
            Status: WatchCallEnrollmentStatus.PendingTraining,
            Latitude: request.Latitude,
            Longitude: request.Longitude,
            WatchRadiusMeters: request.WatchRadiusMeters,
            MockCallsCompleted: 0,
            LastMockCallAt: null,
            TrainingCompletedAt: null,
            LiveCallsParticipated: 0,
            CallsAsSubject: 0,
            AverageBehaviorScore: null,
            SuspensionReason: null,
            SuspendedUntil: null,
            EnrolledAt: DateTime.UtcNow,
            LastUpdated: DateTime.UtcNow);

        var result = await _watchCallService.EnrollAsync(enrollment, ct);

        return Created($"/api/watchcall/enrollment/{result.UserId}", new
        {
            result.EnrollmentId,
            result.UserId,
            result.DisplayAlias,
            Status = result.Status.ToString(),
            result.WatchRadiusMeters,
            result.EnrolledAt,
            NextStep = "Complete a mock training call to activate enrollment.",
            MockCallEndpoint = "/api/watchcall/mock/start"
        });
    }

    /// <summary>
    /// GET /api/watchcall/enrollment/{userId}
    /// Get a user's Watch Call enrollment status.
    /// </summary>
    [HttpGet("enrollment/{userId}")]
    public async Task<IActionResult> GetEnrollment(string userId, CancellationToken ct)
    {
        var enrollment = await _watchCallService.GetEnrollmentAsync(userId, ct);
        if (enrollment is null)
            return NotFound(new { error = $"No Watch Call enrollment found for user {userId}" });

        return Ok(new
        {
            enrollment.EnrollmentId,
            enrollment.UserId,
            enrollment.DisplayAlias,
            Status = enrollment.Status.ToString(),
            enrollment.Latitude,
            enrollment.Longitude,
            enrollment.WatchRadiusMeters,
            enrollment.MockCallsCompleted,
            enrollment.LastMockCallAt,
            enrollment.TrainingCompletedAt,
            enrollment.LiveCallsParticipated,
            enrollment.CallsAsSubject,
            enrollment.AverageBehaviorScore,
            enrollment.SuspensionReason,
            enrollment.SuspendedUntil,
            enrollment.EnrolledAt,
            enrollment.LastUpdated,
            IsEligibleForLiveCalls = enrollment.Status == WatchCallEnrollmentStatus.Active
        });
    }

    // ─────────────────────────────────────────────────────────────
    // ICE Servers (WebRTC configuration)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// GET /api/watchcall/ice-servers
    /// Get STUN/TURN server configurations for WebRTC peer connections.
    /// In dev: Google public STUN. In production: includes Cloudflare TURN
    /// with short-lived credentials for NAT traversal.
    /// </summary>
    [HttpGet("ice-servers")]
    public async Task<IActionResult> GetIceServers(CancellationToken ct)
    {
        var servers = await _watchCallService.GetIceServersAsync(ct);
        return Ok(new
        {
            IceServers = servers,
            Note = "Use these servers for RTCPeerConnection configuration. " +
                   "Credentials expire after 24 hours (TURN only)."
        });
    }

    // ─────────────────────────────────────────────────────────────
    // Mock Training
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// GET /api/watchcall/scenarios
    /// Get available mock call training scenarios. Users must complete at least
    /// one mock call before participating in live Watch Calls.
    /// </summary>
    [HttpGet("scenarios")]
    public async Task<IActionResult> GetScenarios(CancellationToken ct)
    {
        var scenarios = await _watchCallService.GetMockScenariosAsync(ct);
        return Ok(new
        {
            Count = scenarios.Count,
            Scenarios = scenarios.Select(s => new
            {
                s.ScenarioId,
                s.Title,
                s.Description,
                s.SetupNarrative,
                s.DebriefText,
                s.Tags,
                EstimatedDuration = s.EstimatedDuration.ToString(@"mm\:ss"),
                NarrationLineCount = s.ScriptedNarrations.Count
            })
        });
    }

    /// <summary>
    /// POST /api/watchcall/mock/start
    /// Start a mock training call for a user. The client plays the scenario
    /// locally with pre-scripted narrations.
    /// </summary>
    [HttpPost("mock/start")]
    public async Task<IActionResult> StartMockCall(
        [FromBody] StartMockCallRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
            return BadRequest(new { error = "UserId is required" });
        if (string.IsNullOrWhiteSpace(request.ScenarioId))
            return BadRequest(new { error = "ScenarioId is required" });

        try
        {
            var call = await _watchCallService.StartMockCallAsync(request.UserId, request.ScenarioId, ct);
            return Ok(new
            {
                call.CallId,
                Status = call.Status.ToString(),
                call.IsMockCall,
                call.MockScenarioId,
                NarrationScript = call.NarrationTranscript.Select(n => new
                {
                    Offset = n.Offset.ToString(@"mm\:ss"),
                    n.NarrationText
                }),
                Message = "Mock call started. Play the narration script client-side, " +
                          "then call /api/watchcall/mock/{callId}/complete when done."
            });
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    /// <summary>
    /// POST /api/watchcall/mock/{callId}/complete
    /// Complete a mock training call. Updates the user's enrollment (increments
    /// mock call count, activates enrollment if first completion).
    /// </summary>
    [HttpPost("mock/{callId}/complete")]
    public async Task<IActionResult> CompleteMockCall(string callId, CancellationToken ct)
    {
        try
        {
            var enrollment = await _watchCallService.CompleteMockCallAsync(callId, ct);
            return Ok(new
            {
                enrollment.EnrollmentId,
                enrollment.UserId,
                Status = enrollment.Status.ToString(),
                enrollment.MockCallsCompleted,
                enrollment.TrainingCompletedAt,
                IsNowActive = enrollment.Status == WatchCallEnrollmentStatus.Active,
                Message = enrollment.Status == WatchCallEnrollmentStatus.Active
                    ? "Training complete! You are now eligible for live Watch Calls."
                    : $"Mock call completed ({enrollment.MockCallsCompleted} total)."
            });
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }
}

// ─────────────────────────────────────────────────────────────
// Request DTOs
// ─────────────────────────────────────────────────────────────

public record JoinWatchCallRequest(string UserId);

public record EndWatchCallRequest(
    string? Resolution = null,
    string? EndedBy = null
);

public record SubmitFrameRequest(string FrameBase64);

public record EscalateWatchCallRequest(
    string EscalatedBy,
    string? Reason = null
);

public record EnrollWatchCallRequest(
    string UserId,
    double Latitude = 0,
    double Longitude = 0,
    double WatchRadiusMeters = 2000
);

public record StartMockCallRequest(
    string UserId,
    string ScenarioId
);
