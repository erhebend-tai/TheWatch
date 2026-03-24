using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Dtos;
using TheWatch.Shared.Enums;

namespace TheWatch.Adapters.Mock;

/// <summary>
/// Mock implementation of IFirestorePort for testing and development.
/// Maintains an in-memory activity log.
/// </summary>
public class MockFirestoreAdapter : IFirestorePort
{
    private readonly List<AgentActivity> _activityLog = new();

    public Task LogAgentActivityAsync(AgentActivity activity, CancellationToken ct = default)
    {
        if (activity == null)
            throw new ArgumentNullException(nameof(activity));

        _activityLog.Add(activity);
        return Task.CompletedTask;
    }

    public Task<List<AgentActivityDto>> GetRecentActivityAsync(int limit = 50, CancellationToken ct = default)
    {
        var recentActivities = _activityLog
            .OrderByDescending(a => a.Timestamp)
            .Take(limit)
            .Select(a => new AgentActivityDto(
                AgentType: a.AgentType,
                Action: a.Action,
                Description: a.Description,
                Timestamp: a.Timestamp,
                BranchName: a.BranchName ?? string.Empty,
                Platform: a.Platform
            ))
            .ToList();

        // If no activity has been logged, return seeded data
        if (!recentActivities.Any())
        {
            var seedData = GenerateSampleActivity();
            return Task.FromResult(seedData);
        }

        return Task.FromResult(recentActivities);
    }

    public Task SyncWorkItemsAsync(List<WorkItem> items, CancellationToken ct = default)
    {
        if (items == null)
            throw new ArgumentNullException(nameof(items));

        // Mock implementation: just log the sync
        foreach (var item in items)
        {
            _activityLog.Add(new AgentActivity
            {
                AgentType = AgentType.Human,
                Action = "sync-work-item",
                Description = $"Synced work item: {item.Title}",
                Timestamp = DateTime.UtcNow,
                Platform = Platform.GitHub
            });
        }

        return Task.CompletedTask;
    }

    private List<AgentActivityDto> GenerateSampleActivity()
    {
        var activities = new List<AgentActivityDto>
        {
            new AgentActivityDto(
                AgentType: AgentType.ClaudeCode,
                Action: "pushed-code",
                Description: "Pushed implementation for feature/core-api",
                Timestamp: DateTime.UtcNow.AddHours(-2),
                BranchName: "agent/claude-code/feature/core-api",
                Platform: Platform.GitHub
            ),
            new AgentActivityDto(
                AgentType: AgentType.GeminiPro,
                Action: "created-pr",
                Description: "Created pull request for CI/CD pipeline setup",
                Timestamp: DateTime.UtcNow.AddHours(-4),
                BranchName: "agent/gemini-pro/feature/cicd",
                Platform: Platform.GitHub
            ),
            new AgentActivityDto(
                AgentType: AgentType.AzureOpenAI,
                Action: "resolved-issue",
                Description: "Resolved issue: Authentication flow implementation",
                Timestamp: DateTime.UtcNow.AddHours(-6),
                BranchName: "agent/azure-openai/feature/auth",
                Platform: Platform.GitHub
            ),
            new AgentActivityDto(
                AgentType: AgentType.Copilot,
                Action: "added-tests",
                Description: "Added unit tests for API endpoints",
                Timestamp: DateTime.UtcNow.AddHours(-8),
                BranchName: "agent/copilot/feature/tests",
                Platform: Platform.GitHub
            ),
            new AgentActivityDto(
                AgentType: AgentType.JetBrainsAI,
                Action: "updated-docs",
                Description: "Updated API documentation and README",
                Timestamp: DateTime.UtcNow.AddHours(-10),
                BranchName: "agent/jetbrains-ai/feature/docs",
                Platform: Platform.GitHub
            ),
            new AgentActivityDto(
                AgentType: AgentType.Junie,
                Action: "fixed-bug",
                Description: "Fixed memory leak in connection pooling",
                Timestamp: DateTime.UtcNow.AddHours(-12),
                BranchName: "agent/junie/bugfix/memory-leak",
                Platform: Platform.GitHub
            )
        };

        return activities;
    }
}
