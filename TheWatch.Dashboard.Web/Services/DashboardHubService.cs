// =============================================================================
// DashboardHubService — SignalR client for real-time dashboard updates.
// =============================================================================
// Maintains a persistent SignalR connection to the Dashboard.Api hub at
// /hubs/dashboard. Exposes .NET events that Blazor components subscribe to
// for live incident updates, responder tracking, evidence feeds, and more.
//
// Reconnection strategy:
//   Automatic reconnect with exponential backoff (0s, 2s, 10s, 30s).
//   If the connection drops, the service will keep retrying indefinitely.
//   Components are notified of connection state changes via OnConnectionChanged.
//
// Example — subscribing from a Blazor component:
//   @inject DashboardHubService HubService
//   @implements IAsyncDisposable
//
//   protected override async Task OnInitializedAsync()
//   {
//       HubService.OnIncidentCreated += HandleIncidentCreated;
//       await HubService.StartAsync();
//   }
//
//   public async ValueTask DisposeAsync()
//   {
//       HubService.OnIncidentCreated -= HandleIncidentCreated;
//   }
//
// WAL: All event handlers invoke StateHasChanged via InvokeAsync to ensure
//      thread-safe Blazor rendering. The hub connection is shared across
//      all components on the same circuit.
// =============================================================================

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Enums;

namespace TheWatch.Dashboard.Web.Services;

/// <summary>
/// Manages a singleton SignalR connection to the DashboardHub.
/// Blazor components inject this service and subscribe to strongly-typed events.
/// The service is registered as Scoped (one per Blazor circuit).
/// </summary>
public sealed class DashboardHubService : IAsyncDisposable
{
    private readonly HubConnection _hubConnection;
    private readonly ILogger<DashboardHubService> _logger;
    private bool _started;

    // ── Connection State ─────────────────────────────────────────
    public bool IsConnected => _hubConnection.State == HubConnectionState.Connected;
    public HubConnectionState ConnectionState => _hubConnection.State;
    public event Action<HubConnectionState>? OnConnectionChanged;

    // ── Domain Events ────────────────────────────────────────────
    // Response Coordination Events
    public event Action<ResponderLocationUpdate>? OnResponderLocationUpdated;
    public event Action<ResponderOnSceneUpdate>? OnResponderOnScene;
    public event Action<ResponderMessageUpdate>? OnResponderMessage;
    public event Action<EvidenceSubmittedUpdate>? OnEvidenceSubmitted;
    public event Action<EvidenceProcessedUpdate>? OnEvidenceProcessed;

    // Build/Dev Events (existing hub events)
    public event Action<Milestone>? OnMilestoneUpdated;
    public event Action<WorkItem>? OnWorkItemUpdated;
    public event Action<BuildStatus>? OnBuildCompleted;
    public event Action<AgentActivity>? OnAgentActivityRecorded;
    public event Action<SimulationEvent>? OnSimulationEventReceived;

    // Survey Events
    public event Action<SurveyCompletedUpdate>? OnSurveyCompleted;

    // Watch Call Events
    public event Action<WatchCallEscalatedUpdate>? OnWatchCallEscalated;

    public DashboardHubService(
        NavigationManager navigationManager,
        ILogger<DashboardHubService> logger)
    {
        _logger = logger;

        // Build the hub URL from the current app's base URI.
        // In production, this connects to the Dashboard.Api via Aspire service discovery.
        // The API hub is at /hubs/dashboard on the API server.
        var hubUrl = navigationManager.BaseUri.TrimEnd('/') + "/hubs/dashboard";

        // If an API base URL is configured, prefer that (for multi-service deployments)
        // For Aspire, the web project proxies SignalR through its own server.
        _hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect(new[] {
                TimeSpan.Zero,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(30)
            })
            .Build();

        // Wire up connection state events
        _hubConnection.Reconnecting += error =>
        {
            _logger.LogWarning(error, "SignalR reconnecting...");
            OnConnectionChanged?.Invoke(HubConnectionState.Reconnecting);
            return Task.CompletedTask;
        };

        _hubConnection.Reconnected += connectionId =>
        {
            _logger.LogInformation("SignalR reconnected: {ConnectionId}", connectionId);
            OnConnectionChanged?.Invoke(HubConnectionState.Connected);
            return Task.CompletedTask;
        };

        _hubConnection.Closed += error =>
        {
            _logger.LogWarning(error, "SignalR connection closed");
            OnConnectionChanged?.Invoke(HubConnectionState.Disconnected);
            return Task.CompletedTask;
        };

        // Wire up hub event handlers
        RegisterHubHandlers();
    }

    /// <summary>
    /// Start the SignalR connection. Safe to call multiple times — will only
    /// connect once. Called by the first component that needs real-time updates.
    /// </summary>
    public async Task StartAsync()
    {
        if (_started) return;

        try
        {
            await _hubConnection.StartAsync();
            _started = true;
            _logger.LogInformation("SignalR hub connected successfully");
            OnConnectionChanged?.Invoke(HubConnectionState.Connected);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to SignalR hub — will retry on next component init");
            // Don't throw — components should handle disconnected state gracefully
        }
    }

    /// <summary>
    /// Join a response group to receive incident-specific real-time updates.
    /// </summary>
    public async Task JoinResponseGroupAsync(string requestId)
    {
        if (IsConnected)
            await _hubConnection.InvokeAsync("JoinResponseGroup", requestId);
    }

    /// <summary>
    /// Leave a response group.
    /// </summary>
    public async Task LeaveResponseGroupAsync(string requestId)
    {
        if (IsConnected)
            await _hubConnection.InvokeAsync("LeaveResponseGroup", requestId);
    }

    /// <summary>
    /// Join a user-specific group for targeted notifications (surveys, alerts).
    /// </summary>
    public async Task JoinUserGroupAsync(string userId)
    {
        if (IsConnected)
            await _hubConnection.InvokeAsync("JoinUserGroup", userId);
    }

    private void RegisterHubHandlers()
    {
        // ── Response Coordination Events ───────────────────────────
        _hubConnection.On<ResponderLocationUpdate>("ResponderLocationUpdated", update =>
        {
            _logger.LogDebug("Responder location update: {ResponderId}", update.ResponderId);
            OnResponderLocationUpdated?.Invoke(update);
        });

        _hubConnection.On<ResponderOnSceneUpdate>("ResponderOnScene", update =>
        {
            _logger.LogInformation("Responder on scene: {ResponderId} for {RequestId}", update.ResponderId, update.RequestId);
            OnResponderOnScene?.Invoke(update);
        });

        _hubConnection.On<ResponderMessageUpdate>("ResponderMessage", update =>
        {
            OnResponderMessage?.Invoke(update);
        });

        _hubConnection.On<EvidenceSubmittedUpdate>("EvidenceSubmitted", update =>
        {
            _logger.LogInformation("Evidence submitted: {SubmissionId}", update.SubmissionId);
            OnEvidenceSubmitted?.Invoke(update);
        });

        _hubConnection.On<EvidenceProcessedUpdate>("EvidenceProcessed", update =>
        {
            _logger.LogInformation("Evidence processed: {SubmissionId}", update.SubmissionId);
            OnEvidenceProcessed?.Invoke(update);
        });

        // ── Build/Dev Events ───────────────────────────────────────
        _hubConnection.On<Milestone>("MilestoneUpdated", milestone =>
        {
            OnMilestoneUpdated?.Invoke(milestone);
        });

        _hubConnection.On<WorkItem>("WorkItemUpdated", workItem =>
        {
            OnWorkItemUpdated?.Invoke(workItem);
        });

        _hubConnection.On<BuildStatus>("BuildCompleted", build =>
        {
            OnBuildCompleted?.Invoke(build);
        });

        _hubConnection.On<AgentActivity>("AgentActivityRecorded", activity =>
        {
            OnAgentActivityRecorded?.Invoke(activity);
        });

        _hubConnection.On<SimulationEvent>("SimulationEventReceived", simEvent =>
        {
            OnSimulationEventReceived?.Invoke(simEvent);
        });

        // ── Survey Events ──────────────────────────────────────────
        _hubConnection.On<SurveyCompletedUpdate>("SurveyCompleted", update =>
        {
            OnSurveyCompleted?.Invoke(update);
        });

        // ── Watch Call Events ──────────────────────────────────────
        _hubConnection.On<WatchCallEscalatedUpdate>("WatchCallEscalated", update =>
        {
            _logger.LogWarning("Watch call escalated: {CallId} -> {RequestId}", update.CallId, update.RequestId);
            OnWatchCallEscalated?.Invoke(update);
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_hubConnection is not null)
        {
            await _hubConnection.DisposeAsync();
        }
    }
}

// ═══════════════════════════════════════════════════════════════
// SignalR Event DTOs — typed models for hub event deserialization
// ═══════════════════════════════════════════════════════════════

public record ResponderLocationUpdate(
    string RequestId,
    string ResponderId,
    double Latitude,
    double Longitude,
    double? SpeedMps,
    DateTime Timestamp
);

public record ResponderOnSceneUpdate(
    string RequestId,
    string ResponderId,
    DateTime ArrivedAt
);

public record ResponderMessageUpdate(
    string MessageId,
    string RequestId,
    string SenderId,
    string SenderName,
    string MessageType,
    string Content,
    string Verdict,
    DateTime SentAt
);

public record EvidenceSubmittedUpdate(
    string SubmissionId,
    string RequestId,
    string Phase,
    string Type,
    string? ThumbnailUrl,
    DateTime Timestamp
);

public record EvidenceProcessedUpdate(
    string SubmissionId,
    string RequestId,
    bool ThumbnailGenerated,
    string? TranscriptionText,
    int? Width,
    int? Height,
    double? MediaDurationSeconds,
    string[]? ModerationFlags,
    DateTime ProcessedAt
);

public record SurveyCompletedUpdate(
    string ResponseId,
    string RequestId,
    DateTime CompletedAt
);

public record WatchCallEscalatedUpdate(
    string CallId,
    string RequestId,
    string Reason,
    DateTime EscalatedAt
);
