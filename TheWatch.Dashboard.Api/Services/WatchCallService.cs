// =============================================================================
// WatchCallService — orchestrates Watch Call lifecycle, scene narration, and
// escalation to full SOS via IResponseCoordinationService.
// =============================================================================
// This is an application-layer service (NOT a port). It composes:
//   - IWatchCallPort: session CRUD, enrollment, participant management
//   - ISceneNarrationPort: AI vision narration of video frames
//   - IResponseCoordinationService: SOS dispatch on escalation
//   - IAuditTrail: tamper-evident logging of every state transition
//   - SignalR (DashboardHub): real-time narration + signaling broadcast
//
// Call lifecycle orchestrated here:
//   InitiateCall → JoinCall → SubmitFrame → NarrateFrame → Broadcast
//     → ResolveCall (safe) OR EscalateCall (SOS)
//
// Progressive escalation chain:
//   Guard Report → Watch Call → SOS ResponseRequest
//   Each step is logged to the audit trail with a shared CorrelationId.
//
// WAL: No raw video frames are persisted. Only narration text is stored.
//      Frame bytes are passed in-memory to the narration port and discarded.
// =============================================================================

using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using TheWatch.Dashboard.Api.Hubs;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Enums;

namespace TheWatch.Dashboard.Api.Services;

/// <summary>
/// Application service for Watch Call lifecycle management.
/// Coordinates IWatchCallPort, ISceneNarrationPort, IAuditTrail, and SignalR.
/// </summary>
public interface IWatchCallService
{
    // ── Call Lifecycle ────────────────────────────────────────────────
    Task<WatchCall> InitiateCallAsync(InitiateWatchCallRequest request, CancellationToken ct = default);
    Task<WatchCallParticipant> JoinCallAsync(string callId, string userId, CancellationToken ct = default);
    Task LeaveCallAsync(string callId, string userId, CancellationToken ct = default);
    Task<WatchCall> EndCallAsync(string callId, string resolution, CancellationToken ct = default);
    Task<WatchCall?> GetCallStatusAsync(string callId, CancellationToken ct = default);

    // ── Narration ────────────────────────────────────────────────────
    Task<SceneNarration> SubmitFrameAsync(string callId, byte[] frameData, CancellationToken ct = default);
    Task<IReadOnlyList<TimestampedNarration>> GetNarrationTranscriptAsync(string callId, CancellationToken ct = default);

    // ── Escalation ───────────────────────────────────────────────────
    Task<WatchCallEscalationResult> EscalateCallAsync(string callId, string escalatedBy, string? reason = null, CancellationToken ct = default);

    // ── Enrollment ───────────────────────────────────────────────────
    Task<WatchCallEnrollment> EnrollAsync(WatchCallEnrollment enrollment, CancellationToken ct = default);
    Task<WatchCallEnrollment?> GetEnrollmentAsync(string userId, CancellationToken ct = default);

    // ── Mock Training ────────────────────────────────────────────────
    Task<WatchCall> StartMockCallAsync(string userId, string scenarioId, CancellationToken ct = default);
    Task<WatchCallEnrollment> CompleteMockCallAsync(string callId, CancellationToken ct = default);
    Task<IReadOnlyList<MockCallScenario>> GetMockScenariosAsync(CancellationToken ct = default);

    // ── ICE Servers ──────────────────────────────────────────────────
    Task<IReadOnlyList<IceServerConfig>> GetIceServersAsync(CancellationToken ct = default);
}

/// <summary>
/// Result of escalating a Watch Call to a full SOS ResponseRequest.
/// </summary>
public record WatchCallEscalationResult(
    WatchCall EscalatedCall,
    ResponseRequest ResponseRequest,
    string Message
);

/// <summary>
/// Request DTO for initiating a new Watch Call.
/// </summary>
public record InitiateWatchCallRequest(
    string InitiatorUserId,
    double Latitude,
    double Longitude,
    double RadiusMeters = 2000,
    int MaxParticipants = 5,
    bool RecordingEnabled = false,
    string? LinkedGuardReportId = null,
    string? LinkedRequestId = null,
    string? Description = null
);

public class WatchCallService : IWatchCallService
{
    private readonly IWatchCallPort _watchCallPort;
    private readonly ISceneNarrationPort _narrationPort;
    private readonly IResponseCoordinationService _coordinationService;
    private readonly IAuditTrail _auditTrail;
    private readonly IHubContext<DashboardHub> _hubContext;
    private readonly ILogger<WatchCallService> _logger;

    // Track the last narration per call for scene-change detection
    private readonly ConcurrentDictionary<string, string> _lastNarration = new();

    public WatchCallService(
        IWatchCallPort watchCallPort,
        ISceneNarrationPort narrationPort,
        IResponseCoordinationService coordinationService,
        IAuditTrail auditTrail,
        IHubContext<DashboardHub> hubContext,
        ILogger<WatchCallService> logger)
    {
        _watchCallPort = watchCallPort;
        _narrationPort = narrationPort;
        _coordinationService = coordinationService;
        _auditTrail = auditTrail;
        _hubContext = hubContext;
        _logger = logger;
    }

    // ── Call Lifecycle ────────────────────────────────────────────────

    public async Task<WatchCall> InitiateCallAsync(InitiateWatchCallRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[WAL-WATCHCALL] Initiating call for user {UserId} at ({Lat}, {Lng}), radius={Radius}m",
            request.InitiatorUserId, request.Latitude, request.Longitude, request.RadiusMeters);

        var call = new WatchCall(
            CallId: "",
            Status: WatchCallStatus.Requested,
            IsMockCall: false,
            MockScenarioId: null,
            InitiatorUserId: request.InitiatorUserId,
            LinkedRequestId: request.LinkedRequestId,
            LinkedGuardReportId: request.LinkedGuardReportId,
            Latitude: request.Latitude,
            Longitude: request.Longitude,
            RadiusMeters: request.RadiusMeters,
            Participants: new List<WatchCallParticipant>(),
            MaxParticipants: request.MaxParticipants,
            NarrationTranscript: new List<TimestampedNarration>(),
            RecordingEnabled: request.RecordingEnabled,
            RecordingBlobReference: null,
            RequestedAt: DateTime.UtcNow,
            ConnectedAt: null,
            ResolvedAt: null,
            EscalatedAt: null,
            Resolution: null,
            EscalatedRequestId: null);

        var created = await _watchCallPort.CreateCallAsync(call, ct);

        // Audit: Watch Call initiated
        await _auditTrail.AppendAsync(new AuditEntry
        {
            UserId = request.InitiatorUserId,
            ActorRole = "User",
            Action = AuditAction.WatchCallInitiated,
            EntityType = "WatchCall",
            EntityId = created.CallId,
            CorrelationId = created.LinkedRequestId ?? created.CallId,
            SourceSystem = "Dashboard.Api",
            SourceComponent = "WatchCallService",
            Severity = AuditSeverity.Notice,
            DataClassification = DataClassification.Confidential,
            Outcome = AuditOutcome.Success,
            NewValue = JsonSerializer.Serialize(new
            {
                created.CallId,
                created.Latitude,
                created.Longitude,
                created.RadiusMeters,
                created.LinkedGuardReportId,
                request.Description
            }),
            Reason = request.Description ?? "Watch Call initiated"
        }, ct);

        // Broadcast to dashboard
        await _hubContext.Clients.All.SendAsync("WatchCallInitiated", new
        {
            created.CallId,
            created.InitiatorUserId,
            created.Latitude,
            created.Longitude,
            created.RadiusMeters,
            Status = created.Status.ToString(),
            created.LinkedGuardReportId,
            created.RequestedAt
        }, ct);

        // Find and notify nearby watchers
        var nearbyWatchers = await _watchCallPort.FindNearbyWatchersAsync(
            created.Latitude, created.Longitude, created.RadiusMeters, created.MaxParticipants, ct);

        _logger.LogInformation("[WAL-WATCHCALL] Found {Count} nearby watchers for call {CallId}",
            nearbyWatchers.Count, created.CallId);

        foreach (var watcher in nearbyWatchers)
        {
            await _hubContext.Clients.Group($"user-{watcher.UserId}").SendAsync("WatchCallInvitation", new
            {
                created.CallId,
                created.Latitude,
                created.Longitude,
                created.RadiusMeters,
                InvitedAs = watcher.DisplayAlias,
                created.RequestedAt
            }, ct);
        }

        return created;
    }

    public async Task<WatchCallParticipant> JoinCallAsync(string callId, string userId, CancellationToken ct = default)
    {
        _logger.LogInformation("[WAL-WATCHCALL] User {UserId} joining call {CallId}", userId, callId);

        var participant = await _watchCallPort.JoinCallAsync(callId, userId, ct);

        // Audit: user joined
        await _auditTrail.AppendAsync(new AuditEntry
        {
            UserId = userId,
            ActorRole = "Responder",
            Action = AuditAction.WatchCallJoined,
            EntityType = "WatchCall",
            EntityId = callId,
            CorrelationId = callId,
            SourceSystem = "Dashboard.Api",
            SourceComponent = "WatchCallService",
            Severity = AuditSeverity.Info,
            DataClassification = DataClassification.Confidential,
            Outcome = AuditOutcome.Success,
            NewValue = JsonSerializer.Serialize(new
            {
                participant.AnonymizedAlias,
                participant.PeerConnectionId,
                participant.JoinedAt
            }),
            Reason = $"User joined Watch Call as {participant.AnonymizedAlias}"
        }, ct);

        // Broadcast participant join to the call group
        await _hubContext.Clients.Group($"watchcall-{callId}").SendAsync("WatcherJoined", new
        {
            CallId = callId,
            participant.AnonymizedAlias,
            participant.PeerConnectionId,
            participant.JoinedAt
        }, ct);

        return participant;
    }

    public async Task LeaveCallAsync(string callId, string userId, CancellationToken ct = default)
    {
        await _watchCallPort.LeaveCallAsync(callId, userId, ct);

        await _auditTrail.AppendAsync(new AuditEntry
        {
            UserId = userId,
            ActorRole = "Responder",
            Action = AuditAction.WatchCallLeft,
            EntityType = "WatchCall",
            EntityId = callId,
            CorrelationId = callId,
            SourceSystem = "Dashboard.Api",
            SourceComponent = "WatchCallService",
            Severity = AuditSeverity.Info,
            DataClassification = DataClassification.Confidential,
            Outcome = AuditOutcome.Success,
            Reason = "User left Watch Call"
        }, ct);

        await _hubContext.Clients.Group($"watchcall-{callId}").SendAsync("WatcherLeft", new
        {
            CallId = callId,
            UserId = userId,
            LeftAt = DateTime.UtcNow
        }, ct);
    }

    public async Task<WatchCall> EndCallAsync(string callId, string resolution, CancellationToken ct = default)
    {
        _logger.LogInformation("[WAL-WATCHCALL] Ending call {CallId}: {Resolution}", callId, resolution);

        var resolved = await _watchCallPort.ResolveCallAsync(callId, resolution, ct);

        // Clean up narration tracking
        _lastNarration.TryRemove(callId, out _);

        await _auditTrail.AppendAsync(new AuditEntry
        {
            UserId = "system",
            ActorRole = "System",
            Action = AuditAction.WatchCallResolved,
            EntityType = "WatchCall",
            EntityId = callId,
            CorrelationId = resolved.LinkedRequestId ?? callId,
            SourceSystem = "Dashboard.Api",
            SourceComponent = "WatchCallService",
            Severity = AuditSeverity.Notice,
            DataClassification = DataClassification.Confidential,
            Outcome = AuditOutcome.Success,
            NewValue = JsonSerializer.Serialize(new
            {
                resolved.Status,
                resolved.Resolution,
                resolved.ResolvedAt,
                NarrationCount = resolved.NarrationTranscript.Count
            }),
            Reason = $"Watch Call resolved: {resolution}"
        }, ct);

        // Broadcast resolution
        await _hubContext.Clients.Group($"watchcall-{callId}").SendAsync("WatchCallResolved", new
        {
            CallId = callId,
            Resolution = resolution,
            resolved.ResolvedAt,
            NarrationLineCount = resolved.NarrationTranscript.Count
        }, ct);

        await _hubContext.Clients.All.SendAsync("WatchCallStatusChanged", new
        {
            CallId = callId,
            Status = resolved.Status.ToString(),
            resolved.ResolvedAt
        }, ct);

        return resolved;
    }

    public async Task<WatchCall?> GetCallStatusAsync(string callId, CancellationToken ct = default)
    {
        return await _watchCallPort.GetCallAsync(callId, ct);
    }

    // ── Narration ────────────────────────────────────────────────────

    public async Task<SceneNarration> SubmitFrameAsync(string callId, byte[] frameData, CancellationToken ct = default)
    {
        _logger.LogDebug("[WAL-WATCHCALL] Frame submitted for call {CallId}, size={Size} bytes",
            callId, frameData.Length);

        // Get previous narration for scene-change detection
        _lastNarration.TryGetValue(callId, out var previousNarration);

        // Send frame to AI vision narrator
        var narration = await _narrationPort.NarrateFrameAsync(frameData, callId, previousNarration, ct);

        // Update last narration for next frame
        _lastNarration[callId] = narration.NarrationText;

        // Get call to determine the time offset for the transcript
        var call = await _watchCallPort.GetCallAsync(callId, ct);
        var offset = call?.ConnectedAt.HasValue == true
            ? DateTime.UtcNow - call.ConnectedAt.Value
            : TimeSpan.Zero;

        // Append to call transcript
        var timestamped = new TimestampedNarration(offset, narration.NarrationText);
        await _watchCallPort.AppendNarrationAsync(callId, timestamped, ct);

        // Broadcast narration to all call participants
        await _hubContext.Clients.Group($"watchcall-{callId}").SendAsync("SceneNarration", new
        {
            CallId = callId,
            narration.NarrationText,
            Confidence = narration.Confidence.ToString(),
            narration.SceneChanged,
            narration.PeopleCount,
            narration.VehicleCount,
            narration.EnvironmentNote,
            narration.EscalationHint,
            narration.EscalationHintReason,
            narration.LatencyMs,
            narration.Timestamp
        }, ct);

        // If the narrator hints at escalation, broadcast a suggestion (not auto-escalate)
        if (narration.EscalationHint)
        {
            _logger.LogWarning(
                "[WAL-WATCHCALL] Escalation hint in call {CallId}: {Reason}",
                callId, narration.EscalationHintReason);

            await _hubContext.Clients.Group($"watchcall-{callId}").SendAsync("EscalationSuggested", new
            {
                CallId = callId,
                Reason = narration.EscalationHintReason,
                narration.NarrationText,
                SuggestedAt = DateTime.UtcNow
            }, ct);
        }

        return narration;
    }

    public async Task<IReadOnlyList<TimestampedNarration>> GetNarrationTranscriptAsync(
        string callId, CancellationToken ct = default)
    {
        var call = await _watchCallPort.GetCallAsync(callId, ct);
        return call?.NarrationTranscript ?? (IReadOnlyList<TimestampedNarration>)Array.Empty<TimestampedNarration>();
    }

    // ── Escalation ───────────────────────────────────────────────────

    public async Task<WatchCallEscalationResult> EscalateCallAsync(
        string callId, string escalatedBy, string? reason = null, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "[WAL-WATCHCALL] *** ESCALATING *** Call {CallId} to SOS by {UserId}: {Reason}",
            callId, escalatedBy, reason ?? "no reason given");

        // 1. Escalate the Watch Call (marks it as Escalated, creates placeholder request ID)
        var escalatedCall = await _watchCallPort.EscalateCallAsync(callId, escalatedBy, reason, ct);

        // 2. Create a real SOS ResponseRequest via the coordination service
        var description = $"[Watch Call Escalation] Call {callId}\n" +
            $"Escalated by: {escalatedBy}\n" +
            (reason != null ? $"Reason: {reason}\n" : "") +
            $"Narration transcript ({escalatedCall.NarrationTranscript.Count} lines):\n" +
            string.Join("\n", escalatedCall.NarrationTranscript
                .TakeLast(5)
                .Select(n => $"  [{n.Offset:mm\\:ss}] {n.NarrationText}"));

        var triggerSource = $"WATCH_CALL:{callId}";
        var responseRequest = await _coordinationService.CreateResponseAsync(
            escalatedBy,
            ResponseScope.Neighborhood,
            escalatedCall.Latitude,
            escalatedCall.Longitude,
            description,
            triggerSource,
            ct);

        // 3. Audit: Watch Call escalated
        await _auditTrail.AppendAsync(new AuditEntry
        {
            UserId = escalatedBy,
            ActorRole = "User",
            Action = AuditAction.WatchCallEscalated,
            EntityType = "WatchCall",
            EntityId = callId,
            CorrelationId = responseRequest.RequestId,
            SourceSystem = "Dashboard.Api",
            SourceComponent = "WatchCallService",
            Severity = AuditSeverity.Critical,
            DataClassification = DataClassification.HighlyConfidential,
            Outcome = AuditOutcome.Success,
            NewValue = JsonSerializer.Serialize(new
            {
                callId,
                escalatedBy,
                reason,
                ResponseRequestId = responseRequest.RequestId,
                NarrationLines = escalatedCall.NarrationTranscript.Count
            }),
            Reason = $"Watch Call escalated to SOS: {reason ?? "escalation requested"}"
        }, ct);

        // 4. Notify call participants of escalation
        await _hubContext.Clients.Group($"watchcall-{callId}").SendAsync("WatchCallEscalated", new
        {
            CallId = callId,
            ResponseRequestId = responseRequest.RequestId,
            EscalatedBy = escalatedBy,
            Reason = reason,
            EscalatedAt = escalatedCall.EscalatedAt
        }, ct);

        // 5. Broadcast to dashboard
        await _hubContext.Clients.All.SendAsync("WatchCallStatusChanged", new
        {
            CallId = callId,
            Status = WatchCallStatus.Escalated.ToString(),
            ResponseRequestId = responseRequest.RequestId,
            escalatedCall.EscalatedAt
        }, ct);

        // Clean up narration tracking
        _lastNarration.TryRemove(callId, out _);

        return new WatchCallEscalationResult(
            escalatedCall,
            responseRequest,
            "Watch Call escalated to SOS. Responders are being dispatched.");
    }

    // ── Enrollment ───────────────────────────────────────────────────

    public async Task<WatchCallEnrollment> EnrollAsync(WatchCallEnrollment enrollment, CancellationToken ct = default)
    {
        var result = await _watchCallPort.EnrollAsync(enrollment, ct);

        await _auditTrail.AppendAsync(new AuditEntry
        {
            UserId = result.UserId,
            ActorRole = "User",
            Action = AuditAction.WatchCallEnrollmentCreated,
            EntityType = "WatchCallEnrollment",
            EntityId = result.EnrollmentId,
            CorrelationId = result.EnrollmentId,
            SourceSystem = "Dashboard.Api",
            SourceComponent = "WatchCallService",
            Severity = AuditSeverity.Info,
            DataClassification = DataClassification.Confidential,
            Outcome = AuditOutcome.Success,
            NewValue = JsonSerializer.Serialize(new
            {
                result.EnrollmentId,
                result.UserId,
                result.DisplayAlias,
                result.Status,
                result.WatchRadiusMeters
            }),
            Reason = "User enrolled for Watch Calls"
        }, ct);

        return result;
    }

    public Task<WatchCallEnrollment?> GetEnrollmentAsync(string userId, CancellationToken ct = default)
    {
        return _watchCallPort.GetEnrollmentAsync(userId, ct);
    }

    // ── Mock Training ────────────────────────────────────────────────

    public async Task<WatchCall> StartMockCallAsync(string userId, string scenarioId, CancellationToken ct = default)
    {
        _logger.LogInformation("[WAL-WATCHCALL] Starting mock call for user {UserId}, scenario {ScenarioId}",
            userId, scenarioId);

        var call = await _watchCallPort.StartMockCallAsync(userId, scenarioId, ct);

        await _auditTrail.AppendAsync(new AuditEntry
        {
            UserId = userId,
            ActorRole = "User",
            Action = AuditAction.WatchCallMockStarted,
            EntityType = "WatchCall",
            EntityId = call.CallId,
            CorrelationId = call.CallId,
            SourceSystem = "Dashboard.Api",
            SourceComponent = "WatchCallService",
            Severity = AuditSeverity.Info,
            DataClassification = DataClassification.Internal,
            Outcome = AuditOutcome.Success,
            NewValue = JsonSerializer.Serialize(new { call.CallId, scenarioId }),
            Reason = $"Mock training call started: scenario {scenarioId}"
        }, ct);

        return call;
    }

    public async Task<WatchCallEnrollment> CompleteMockCallAsync(string callId, CancellationToken ct = default)
    {
        var enrollment = await _watchCallPort.CompleteMockCallAsync(callId, ct);

        await _auditTrail.AppendAsync(new AuditEntry
        {
            UserId = enrollment.UserId,
            ActorRole = "User",
            Action = AuditAction.WatchCallMockCompleted,
            EntityType = "WatchCallEnrollment",
            EntityId = enrollment.EnrollmentId,
            CorrelationId = callId,
            SourceSystem = "Dashboard.Api",
            SourceComponent = "WatchCallService",
            Severity = AuditSeverity.Info,
            DataClassification = DataClassification.Internal,
            Outcome = AuditOutcome.Success,
            NewValue = JsonSerializer.Serialize(new
            {
                enrollment.UserId,
                enrollment.MockCallsCompleted,
                enrollment.Status,
                enrollment.TrainingCompletedAt
            }),
            Reason = $"Mock call completed. Total: {enrollment.MockCallsCompleted}. Status: {enrollment.Status}"
        }, ct);

        return enrollment;
    }

    public Task<IReadOnlyList<MockCallScenario>> GetMockScenariosAsync(CancellationToken ct = default)
    {
        return _watchCallPort.GetMockScenariosAsync(ct);
    }

    // ── ICE Servers ──────────────────────────────────────────────────

    public Task<IReadOnlyList<IceServerConfig>> GetIceServersAsync(CancellationToken ct = default)
    {
        return _watchCallPort.GetIceServersAsync(ct);
    }
}
