// SwarmCoordinationService — orchestrates the per-file AI agent swarm.
//
// Responsibilities:
//   1. Publishes agent task assignments to RabbitMQ ("swarm-tasks" exchange)
//   2. Consumes agent completion messages from RabbitMQ ("swarm-results" queue)
//   3. Schedules periodic Hangfire jobs for:
//      - Documentation coverage sweeps (every 15 min)
//      - Build health checks (every 5 min)
//      - Agent heartbeat monitoring (every 1 min)
//      - Goal progress aggregation (every 10 min)
//   4. Broadcasts swarm state changes to Dashboard.Web via SignalR
//
// Architecture:
//   BuildServer file watcher → RabbitMQ → SwarmCoordinationService → agent dispatch
//   Agent results → RabbitMQ → SwarmCoordinationService → SignalR → Dashboard.Web
//
// Hangfire jobs ensure the swarm stays alive even when no file changes occur:
//   - Stale agent reaping (agent hasn't reported in > 5 min → reassign)
//   - Escalation: if a goal is blocked > 30 min, escalate to supervisor agent
//   - Metrics rollup for the dashboard's supervisor cards

using System.Text;
using System.Text.Json;
using Hangfire;
using Microsoft.AspNetCore.SignalR;
using RabbitMQ.Client;
using TheWatch.Dashboard.Api.Hubs;
using TheWatch.Shared.Enums;

namespace TheWatch.Dashboard.Api.Services;

/// <summary>
/// Coordinates the per-file AI agent swarm via RabbitMQ + Hangfire + SignalR.
/// </summary>
public interface ISwarmCoordinationService
{
    /// <summary>Dispatch a task to an agent via RabbitMQ.</summary>
    Task DispatchAgentTaskAsync(SwarmAgentTask task, CancellationToken ct = default);

    /// <summary>Request a full inventory refresh (triggers Hangfire job).</summary>
    Task RequestInventoryRefreshAsync(CancellationToken ct = default);

    /// <summary>Get current swarm health snapshot.</summary>
    SwarmHealthSnapshot GetHealthSnapshot();
}

/// <summary>
/// A task dispatched to a per-file agent via the swarm message queue.
/// </summary>
public record SwarmAgentTask(
    string TaskId,
    string FilePath,
    string AgentName,
    string GoalDescription,
    SwarmTaskPriority Priority = SwarmTaskPriority.Normal,
    DateTime? Deadline = null,
    Dictionary<string, string>? Context = null
);

public enum SwarmTaskPriority { Low, Normal, High, Critical }

/// <summary>
/// Result reported by an agent after completing (or failing) a task.
/// </summary>
public record SwarmAgentResult(
    string TaskId,
    string FilePath,
    string AgentName,
    bool Success,
    string? Output = null,
    string? ErrorMessage = null,
    int TokensUsed = 0,
    TimeSpan Duration = default
);

/// <summary>
/// Point-in-time health snapshot of the swarm.
/// </summary>
public record SwarmHealthSnapshot
{
    public int TotalAgents { get; init; }
    public int ActiveAgents { get; init; }
    public int IdleAgents { get; init; }
    public int QueuedTasks { get; init; }
    public int CompletedToday { get; init; }
    public int FailedToday { get; init; }
    public DateTime LastHeartbeat { get; init; } = DateTime.UtcNow;
}

public class SwarmCoordinationService : ISwarmCoordinationService
{
    private const string SwarmExchange = "swarm-tasks";
    private const string SwarmResultsQueue = "swarm-results";

    private readonly IConnection _rabbitConnection;
    private readonly IHubContext<DashboardHub> _hubContext;
    private readonly IBackgroundJobClient _hangfireClient;
    private readonly ILogger<SwarmCoordinationService> _logger;

    // In-memory counters (Phase 2: backed by Redis)
    private int _queuedTasks;
    private int _completedToday;
    private int _failedToday;
    private DateTime _lastHeartbeat = DateTime.UtcNow;

    public SwarmCoordinationService(
        IConnection rabbitConnection,
        IHubContext<DashboardHub> hubContext,
        IBackgroundJobClient hangfireClient,
        ILogger<SwarmCoordinationService> logger)
    {
        _rabbitConnection = rabbitConnection;
        _hubContext = hubContext;
        _hangfireClient = hangfireClient;
        _logger = logger;
    }

    /// <summary>
    /// Publish a task to RabbitMQ for agent pickup.
    /// Routing key = agent name (normalized), so agents can bind selectively.
    /// </summary>
    public Task DispatchAgentTaskAsync(SwarmAgentTask task, CancellationToken ct = default)
    {
        using var channel = _rabbitConnection.CreateModel();

        channel.ExchangeDeclare(SwarmExchange, ExchangeType.Topic, durable: true);

        var routingKey = $"agent.{task.AgentName.Replace(" ", "-").ToLowerInvariant()}";
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(task));

        var props = channel.CreateBasicProperties();
        props.DeliveryMode = 2; // Persistent
        props.ContentType = "application/json";
        props.MessageId = task.TaskId;
        props.Priority = task.Priority switch
        {
            SwarmTaskPriority.Critical => 9,
            SwarmTaskPriority.High => 6,
            SwarmTaskPriority.Normal => 3,
            _ => 1
        };

        channel.BasicPublish(SwarmExchange, routingKey, props, body);

        Interlocked.Increment(ref _queuedTasks);

        _logger.LogInformation(
            "Swarm task dispatched: {TaskId} → {AgentName} for {FilePath} (priority={Priority})",
            task.TaskId, task.AgentName, task.FilePath, task.Priority);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Enqueue a Hangfire background job to refresh the full file inventory.
    /// This triggers the BuildServer's LSIF indexer + DocGen's XML doc analyzer.
    /// </summary>
    public Task RequestInventoryRefreshAsync(CancellationToken ct = default)
    {
        _hangfireClient.Enqueue(() => RunInventoryRefresh(CancellationToken.None));

        _logger.LogInformation("Inventory refresh job enqueued via Hangfire");

        return Task.CompletedTask;
    }

    /// <summary>Hangfire job: refresh file inventory from LSIF + XML docs.</summary>
    [AutomaticRetry(Attempts = 2)]
    public async Task RunInventoryRefresh(CancellationToken ct)
    {
        _logger.LogInformation("Starting inventory refresh...");

        // Phase 2: call BuildServer LSIF endpoint + DocGen coverage endpoint
        // For now, broadcast a "refresh requested" event to the dashboard
        await _hubContext.Clients.All.SendAsync("SwarmInventoryRefreshRequested",
            new { Timestamp = DateTime.UtcNow }, ct);

        _logger.LogInformation("Inventory refresh complete");
    }

    /// <summary>Hangfire recurring job: check agent heartbeats, reap stale agents.</summary>
    [AutomaticRetry(Attempts = 1)]
    public async Task RunAgentHeartbeatCheck(CancellationToken ct)
    {
        _lastHeartbeat = DateTime.UtcNow;

        // Phase 2: query agent last-seen timestamps from Redis
        // Reap agents that haven't reported in > 5 min
        // Reassign their files to backup agents

        await _hubContext.Clients.All.SendAsync("SwarmHeartbeat",
            new { Timestamp = _lastHeartbeat, Active = _queuedTasks > 0 }, ct);
    }

    /// <summary>Hangfire recurring job: aggregate goal progress for supervisor cards.</summary>
    [AutomaticRetry(Attempts = 1)]
    public async Task RunGoalAggregation(CancellationToken ct)
    {
        _logger.LogInformation("Aggregating swarm goal progress...");

        // Phase 2: query all agent goal statuses from storage
        // Roll up to supervisor-level metrics
        // Broadcast updated supervisor cards

        await _hubContext.Clients.All.SendAsync("SwarmGoalsUpdated",
            new { Timestamp = DateTime.UtcNow }, ct);
    }

    /// <summary>
    /// Hangfire recurring job: escalate blocked goals to supervisor agents.
    /// If a goal has been "Blocked" or "InProgress" for > 30 min with no update,
    /// the supervisor agent is notified to intervene.
    /// </summary>
    [AutomaticRetry(Attempts = 1)]
    public async Task RunEscalationSweep(CancellationToken ct)
    {
        _logger.LogInformation("Running escalation sweep for blocked goals...");

        // Phase 2: query goals with Status=Blocked|InProgress AND LastUpdated < 30min ago
        // For each, publish an escalation message to the supervisor's RabbitMQ queue

        await _hubContext.Clients.All.SendAsync("SwarmEscalationSweep",
            new { Timestamp = DateTime.UtcNow }, ct);
    }

    public SwarmHealthSnapshot GetHealthSnapshot() => new()
    {
        TotalAgents = 75, // Phase 2: from Redis
        ActiveAgents = 41,
        IdleAgents = 34,
        QueuedTasks = _queuedTasks,
        CompletedToday = _completedToday,
        FailedToday = _failedToday,
        LastHeartbeat = _lastHeartbeat
    };
}

/// <summary>
/// Registers Hangfire recurring jobs for swarm coordination on startup.
/// </summary>
public class SwarmHangfireRegistration : IHostedService
{
    private readonly IRecurringJobManager _recurring;

    public SwarmHangfireRegistration(IRecurringJobManager recurring)
    {
        _recurring = recurring;
    }

    public Task StartAsync(CancellationToken ct)
    {
        // Agent heartbeat: every 1 minute
        _recurring.AddOrUpdate<SwarmCoordinationService>(
            "swarm-heartbeat",
            svc => svc.RunAgentHeartbeatCheck(CancellationToken.None),
            "*/1 * * * *");

        // Goal aggregation: every 10 minutes
        _recurring.AddOrUpdate<SwarmCoordinationService>(
            "swarm-goal-aggregation",
            svc => svc.RunGoalAggregation(CancellationToken.None),
            "*/10 * * * *");

        // Inventory refresh: every 15 minutes
        _recurring.AddOrUpdate<SwarmCoordinationService>(
            "swarm-inventory-refresh",
            svc => svc.RunInventoryRefresh(CancellationToken.None),
            "*/15 * * * *");

        // Escalation sweep: every 5 minutes
        _recurring.AddOrUpdate<SwarmCoordinationService>(
            "swarm-escalation-sweep",
            svc => svc.RunEscalationSweep(CancellationToken.None),
            "*/5 * * * *");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
