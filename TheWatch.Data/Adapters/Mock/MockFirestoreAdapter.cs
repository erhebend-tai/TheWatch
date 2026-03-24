// MockFirestoreAdapter — in-memory agent activity log for dev/test.
// Example:
//   services.AddSingleton<IFirestorePort, MockFirestoreAdapter>();
//   await firestore.LogAgentActivityAsync(activity);
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Dtos;
using TheWatch.Shared.Enums;

namespace TheWatch.Data.Adapters.Mock;

public class MockFirestoreAdapter : IFirestorePort
{
    private readonly List<AgentActivity> _activities = new();
    private readonly List<WorkItem> _syncedItems = new();
    private readonly object _lock = new();

    public MockFirestoreAdapter()
    {
        // Seed with initial data
        _activities.AddRange(new[]
        {
            new AgentActivity { AgentType = AgentType.Claude, Action = "scaffold", Description = "Created hexagonal architecture", Timestamp = DateTime.UtcNow.AddHours(-2), Platform = Platform.Backend },
            new AgentActivity { AgentType = AgentType.GitHubActions, Action = "deploy", Description = "Deployed to staging", Timestamp = DateTime.UtcNow.AddHours(-1), Platform = Platform.Backend },
        });
    }

    public Task LogAgentActivityAsync(AgentActivity activity, CancellationToken ct = default)
    {
        lock (_lock)
        {
            activity.Timestamp = DateTime.UtcNow;
            _activities.Add(activity);
        }
        return Task.CompletedTask;
    }

    public Task<List<AgentActivityDto>> GetRecentActivityAsync(int limit = 50, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var dtos = _activities
                .OrderByDescending(a => a.Timestamp)
                .Take(limit)
                .Select(a => new AgentActivityDto(
                    a.AgentType,
                    a.Action,
                    a.Description,
                    a.Timestamp,
                    a.BranchName,
                    a.Platform
                ))
                .ToList();
            return Task.FromResult(dtos);
        }
    }

    public Task SyncWorkItemsAsync(List<WorkItem> items, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _syncedItems.Clear();
            _syncedItems.AddRange(items);
        }
        return Task.CompletedTask;
    }
}
