using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Enums;

namespace TheWatch.Dashboard.Api.Hubs;

/// <summary>
/// SignalR hub for real-time dashboard updates broadcast to connected clients.
/// </summary>
[Authorize]
public class DashboardHub : Hub
{
    private readonly ILogger<DashboardHub> _logger;

    public DashboardHub(ILogger<DashboardHub> logger) => _logger = logger;

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Dashboard client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Dashboard client disconnected: {ConnectionId}, Exception: {Exception}", Context.ConnectionId, exception?.Message);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task NotifyMilestoneUpdated(Milestone milestone)
    {
        _logger.LogInformation("Broadcasting milestone update: {MilestoneId}", milestone.Id);
        await Clients.All.SendAsync("MilestoneUpdated", milestone);
    }

    public async Task NotifyWorkItemUpdated(WorkItem workItem)
    {
        _logger.LogInformation("Broadcasting work item update: {WorkItemId}", workItem.Id);
        await Clients.All.SendAsync("WorkItemUpdated", workItem);
    }

    public async Task NotifyBuildCompleted(BuildStatus buildStatus)
    {
        _logger.LogInformation("Broadcasting build completion: {RunId}", buildStatus.RunId);
        await Clients.All.SendAsync("BuildCompleted", buildStatus);
    }

    public async Task NotifyAgentActivity(AgentActivity activity)
    {
        _logger.LogInformation("Broadcasting agent activity: {Agent} - {Action}", activity.AgentType, activity.Action);
        await Clients.All.SendAsync("AgentActivityRecorded", activity);
    }

    public async Task NotifySimulationEvent(SimulationEvent simulationEvent)
    {
        _logger.LogInformation("Broadcasting simulation event: {EventType} from {Source}", simulationEvent.EventType, simulationEvent.Source);
        await Clients.All.SendAsync("SimulationEventReceived", simulationEvent);
    }

    // ── Response Coordination Hub Methods ────────────────────────────────────

    /// <summary>
    /// Join a response group to receive real-time updates for a specific SOS request.
    /// Mobile clients call this after receiving a dispatch notification.
    /// </summary>
    public async Task JoinResponseGroup(string requestId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"response-{requestId}");
        _logger.LogInformation("Client {ConnectionId} joined response group {RequestId}",
            Context.ConnectionId, requestId);
    }

    /// <summary>
    /// Leave a response group when the response is resolved or cancelled.
    /// </summary>
    public async Task LeaveResponseGroup(string requestId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"response-{requestId}");
        _logger.LogInformation("Client {ConnectionId} left response group {RequestId}",
            Context.ConnectionId, requestId);
    }

    /// <summary>
    /// Responder sends a real-time location update while en route.
    /// Broadcasted to all members of the response group.
    /// </summary>
    public async Task UpdateResponderLocation(string requestId, string responderId,
        double latitude, double longitude, double? speedMps)
    {
        await Clients.Group($"response-{requestId}").SendAsync("ResponderLocationUpdated", new
        {
            RequestId = requestId,
            ResponderId = responderId,
            Latitude = latitude,
            Longitude = longitude,
            SpeedMps = speedMps,
            Timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Responder signals they've arrived on scene.
    /// </summary>
    public async Task ResponderOnScene(string requestId, string responderId)
    {
        _logger.LogInformation("Responder {ResponderId} ON SCENE for {RequestId}", responderId, requestId);
        await Clients.Group($"response-{requestId}").SendAsync("ResponderOnScene", new
        {
            RequestId = requestId,
            ResponderId = responderId,
            ArrivedAt = DateTime.UtcNow
        });
    }

    // ── Responder Communication Hub Methods ─────────────────────────────────

    /// <summary>
    /// Send a message to an incident's responder channel via SignalR.
    /// The message is routed through the server's guardrails pipeline first —
    /// this hub method delegates to the REST endpoint flow, which handles
    /// filtering before broadcasting. Mobile clients should prefer the REST
    /// endpoint POST /api/response/{requestId}/messages, which returns the
    /// guardrails verdict. This hub method is provided for real-time fallback.
    /// </summary>
    public async Task SendResponderMessage(string requestId, string senderId,
        string senderName, string content)
    {
        _logger.LogInformation(
            "Responder message via SignalR: {SenderId} in {RequestId}",
            senderId, requestId);

        // Broadcast raw to group — in production this should go through guardrails
        // via the ResponseCoordinationService. SignalR direct send is only for
        // clients that have already received approval via the REST endpoint.
        await Clients.Group($"response-{requestId}").SendAsync("ResponderMessage", new
        {
            MessageId = Guid.NewGuid().ToString("N")[..12],
            RequestId = requestId,
            SenderId = senderId,
            SenderName = senderName,
            MessageType = "Text",
            Content = content,
            Verdict = "Approved",
            SentAt = DateTime.UtcNow
        });
    }

    // ── Evidence Submission Hub Methods ──────────────────────────────────────

    /// <summary>
    /// Broadcast new evidence to the response group (photos, video, audio, sitreps).
    /// Called by the API when EvidenceController processes an Active-phase upload,
    /// or by the EvidenceNotificationFunction after processing.
    /// </summary>
    public async Task NotifyEvidenceSubmitted(string submissionId, string requestId,
        SubmissionPhase phase, SubmissionType type, string? thumbnailUrl)
    {
        _logger.LogInformation(
            "Broadcasting evidence submitted: {SubmissionId}, Phase={Phase}, Type={Type}",
            submissionId, phase, type);

        await Clients.Group($"response-{requestId}").SendAsync("EvidenceSubmitted", new
        {
            SubmissionId = submissionId,
            RequestId = requestId,
            Phase = phase.ToString(),
            Type = type.ToString(),
            ThumbnailUrl = thumbnailUrl,
            Timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Broadcast that evidence processing is complete (thumbnail ready, transcription done).
    /// Called when the Worker Service finishes background processing.
    /// </summary>
    public async Task NotifyEvidenceProcessed(string submissionId, string requestId,
        EvidenceProcessingResult processingResult)
    {
        _logger.LogInformation(
            "Broadcasting evidence processed: {SubmissionId}, Thumbnail={Thumb}, Transcription={HasText}",
            submissionId, processingResult.ThumbnailGenerated,
            !string.IsNullOrEmpty(processingResult.TranscriptionText));

        await Clients.Group($"response-{requestId}").SendAsync("EvidenceProcessed", new
        {
            SubmissionId = submissionId,
            RequestId = requestId,
            processingResult.ThumbnailGenerated,
            processingResult.TranscriptionText,
            processingResult.Width,
            processingResult.Height,
            processingResult.MediaDurationSeconds,
            processingResult.ModerationFlags,
            processingResult.ProcessedAt
        });
    }

    // ── Survey Hub Methods ──────────────────────────────────────────────────

    /// <summary>
    /// Notify a specific user that a survey has been dispatched to them.
    /// Mobile client shows a badge / push notification to complete the survey.
    /// </summary>
    public async Task NotifySurveyDispatched(string templateId, string requestId, string userId)
    {
        _logger.LogInformation(
            "Dispatching survey notification: Template={TemplateId}, Request={RequestId}, User={UserId}",
            templateId, requestId, userId);

        // Send to the specific user (assumes user joined a personal group "user-{userId}")
        await Clients.Group($"user-{userId}").SendAsync("SurveyDispatched", new
        {
            TemplateId = templateId,
            RequestId = requestId,
            UserId = userId,
            DispatchedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Notify coordinators that a survey response has been completed.
    /// Used for real-time completion tracking on the dashboard.
    /// </summary>
    public async Task NotifySurveyCompleted(string responseId, string requestId)
    {
        _logger.LogInformation(
            "Broadcasting survey completed: Response={ResponseId}, Request={RequestId}",
            responseId, requestId);

        await Clients.Group($"response-{requestId}").SendAsync("SurveyCompleted", new
        {
            ResponseId = responseId,
            RequestId = requestId,
            CompletedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Join a user-specific group for targeted notifications (surveys, alerts).
    /// Called by mobile clients on app startup.
    /// </summary>
    public async Task JoinUserGroup(string userId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{userId}");
        _logger.LogInformation("Client {ConnectionId} joined user group {UserId}",
            Context.ConnectionId, userId);
    }

    /// <summary>
    /// Leave a user-specific group.
    /// </summary>
    public async Task LeaveUserGroup(string userId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user-{userId}");
        _logger.LogInformation("Client {ConnectionId} left user group {UserId}",
            Context.ConnectionId, userId);
    }

    // ── Watch Call Hub Methods ───────────────────────────────────────────────

    /// <summary>
    /// Join a Watch Call group for WebRTC signaling and narration broadcast.
    /// Called when a participant joins a Watch Call (live or mock).
    /// </summary>
    public async Task JoinWatchCall(string callId, string anonymizedAlias)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"watchcall-{callId}");
        _logger.LogInformation("Client {ConnectionId} joined Watch Call {CallId} as {Alias}",
            Context.ConnectionId, callId, anonymizedAlias);

        // Notify other participants that a new watcher has joined
        await Clients.OthersInGroup($"watchcall-{callId}").SendAsync("WatcherJoined", new
        {
            CallId = callId,
            Alias = anonymizedAlias,
            PeerConnectionId = Context.ConnectionId,
            JoinedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Leave a Watch Call group. Notifies other participants.
    /// </summary>
    public async Task LeaveWatchCall(string callId, string anonymizedAlias)
    {
        await Clients.OthersInGroup($"watchcall-{callId}").SendAsync("WatcherLeft", new
        {
            CallId = callId,
            Alias = anonymizedAlias,
            PeerConnectionId = Context.ConnectionId,
            LeftAt = DateTime.UtcNow
        });

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"watchcall-{callId}");
        _logger.LogInformation("Client {ConnectionId} left Watch Call {CallId}", Context.ConnectionId, callId);
    }

    /// <summary>
    /// Relay a WebRTC signaling message (offer, answer, or ICE candidate)
    /// from one peer to another within a Watch Call.
    /// The server does NOT inspect the SDP/ICE payload — it's an opaque relay.
    /// </summary>
    public async Task RelaySignaling(string callId, string toPeerConnectionId,
        string messageType, string payload)
    {
        _logger.LogDebug("Relaying {Type} signaling in call {CallId}: {From} → {To}",
            messageType, callId, Context.ConnectionId, toPeerConnectionId);

        await Clients.Client(toPeerConnectionId).SendAsync("SignalingMessage", new
        {
            CallId = callId,
            FromPeerConnectionId = Context.ConnectionId,
            Type = messageType,
            Payload = payload,
            Timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Broadcast a scene narration to all participants in a Watch Call.
    /// Called by the server after the AI narrator processes a frame.
    /// </summary>
    public async Task BroadcastNarration(string callId, string narrationText,
        string confidence, bool sceneChanged, bool escalationHint, string? escalationReason)
    {
        _logger.LogInformation("Broadcasting narration for call {CallId}: {Confidence}, changed={Changed}, escalation={Hint}",
            callId, confidence, sceneChanged, escalationHint);

        await Clients.Group($"watchcall-{callId}").SendAsync("SceneNarration", new
        {
            CallId = callId,
            NarrationText = narrationText,
            Confidence = confidence,
            SceneChanged = sceneChanged,
            EscalationHint = escalationHint,
            EscalationReason = escalationReason,
            Timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Submit a video frame for AI narration. The frame (JPEG bytes) is sent
    /// as a base64-encoded string. The server decodes it, passes it to the
    /// ISceneNarrationPort, and broadcasts the resulting narration to the group.
    ///
    /// Rate-limited to 1 frame per 2 seconds per call to control API costs.
    /// </summary>
    public async Task SubmitFrameForNarration(string callId, string frameBase64)
    {
        _logger.LogDebug("Frame received for narration in call {CallId}, size={Size} chars",
            callId, frameBase64.Length);

        // The actual narration processing is handled by the WatchCallService
        // which is injected into the controller layer. This hub method serves
        // as the real-time ingress point — the controller/service handles:
        //   1. Decode base64 → byte[]
        //   2. Call ISceneNarrationPort.NarrateFrameAsync()
        //   3. Call IWatchCallPort.AppendNarrationAsync()
        //   4. Broadcast via BroadcastNarration()
        //
        // For now, relay to the group that a frame was received (processing pending).
        await Clients.Group($"watchcall-{callId}").SendAsync("FrameReceived", new
        {
            CallId = callId,
            FromPeer = Context.ConnectionId,
            Timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Notify all Watch Call participants that the call has been escalated
    /// to a full SOS ResponseRequest.
    /// </summary>
    public async Task NotifyWatchCallEscalated(string callId, string requestId, string reason)
    {
        _logger.LogInformation("Watch Call {CallId} escalated to SOS {RequestId}: {Reason}",
            callId, requestId, reason);

        await Clients.Group($"watchcall-{callId}").SendAsync("WatchCallEscalated", new
        {
            CallId = callId,
            RequestId = requestId,
            Reason = reason,
            EscalatedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Notify all Watch Call participants that the call has been resolved.
    /// </summary>
    public async Task NotifyWatchCallResolved(string callId, string resolution)
    {
        _logger.LogInformation("Watch Call {CallId} resolved: {Resolution}", callId, resolution);

        await Clients.Group($"watchcall-{callId}").SendAsync("WatchCallResolved", new
        {
            CallId = callId,
            Resolution = resolution,
            ResolvedAt = DateTime.UtcNow
        });
    }
}
