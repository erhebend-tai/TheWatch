using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Dtos;
using TheWatch.Shared.Enums;

namespace TheWatch.Dashboard.Api.Services;

/// <summary>
/// Firestore service providing real-time data sync and activity logging.
/// Implements IFirestorePort — the domain port defined in TheWatch.Shared.
/// In production, this would use Google.Cloud.Firestore to call Firestore APIs.
/// </summary>
public class FirestoreService : IFirestorePort
{
    private readonly ILogger<FirestoreService> _logger;
    private readonly List<AgentActivityDto> _activityLog = new();

    public FirestoreService(ILogger<FirestoreService> logger)
    {
        _logger = logger;
        InitializeMockActivityLog();
    }

    public Task LogAgentActivityAsync(AgentActivity activity, CancellationToken ct = default)
    {
        var dto = new AgentActivityDto(
            activity.AgentType,
            activity.Action,
            activity.Description,
            activity.Timestamp,
            activity.BranchName,
            activity.Platform
        );
        _activityLog.Insert(0, dto);
        _logger.LogInformation("Logged agent activity: {Agent} - {Action} on {Branch}", activity.AgentType, activity.Action, activity.BranchName);
        return Task.CompletedTask;
    }

    public Task<List<AgentActivityDto>> GetRecentActivityAsync(int limit = 50, CancellationToken ct = default) =>
        Task.FromResult(_activityLog.Take(limit).ToList());

    public Task SyncWorkItemsAsync(List<WorkItem> items, CancellationToken ct = default)
    {
        _logger.LogInformation("Syncing {Count} work items to Firestore", items.Count);
        return Task.CompletedTask;
    }

    private void InitializeMockActivityLog()
    {
        _activityLog.AddRange(new[]
        {
            new AgentActivityDto(AgentType.AzureOpenAI, "commit", "Add entity relationships to domain models", DateTime.Now.AddHours(-2), "agent/azure-openai/backend/domain-models", Platform.Backend),
            new AgentActivityDto(AgentType.GeminiPro, "push", "Implement MAUI Shell navigation", DateTime.Now.AddHours(-4), "agent/gemini-pro/android/ui-framework", Platform.Android),
            new AgentActivityDto(AgentType.Copilot, "pr", "Created PR for database migrations", DateTime.Now.AddDays(-1), "agent/copilot/database/migrations", Platform.Database),
            new AgentActivityDto(AgentType.JetBrainsAI, "merge", "Merged health check endpoint implementation", DateTime.Now.AddDays(-20), "agent/jetbrains-ai/backend/health-checks", Platform.Backend),
            new AgentActivityDto(AgentType.ClaudeCode, "merge", "Merged GitHub setup and CI/CD pipeline", DateTime.Now.AddDays(-55), "agent/claude-code/infra/github-setup", Platform.Infra),
        });
    }
}
