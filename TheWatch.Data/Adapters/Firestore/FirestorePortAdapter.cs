// FirestorePortAdapter — IFirestorePort wrapping Google Cloud FirestoreDb.
// Example:
//   services.AddScoped<IFirestorePort, FirestorePortAdapter>();
using System.Text.Json;
using Google.Cloud.Firestore;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Dtos;
using TheWatch.Shared.Enums;

namespace TheWatch.Data.Adapters.Firestore;

public class FirestorePortAdapter : IFirestorePort
{
    private readonly FirestoreDb _db;

    public FirestorePortAdapter(FirestoreDb db) => _db = db;

    public async Task LogAgentActivityAsync(AgentActivity activity, CancellationToken ct = default)
    {
        var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(activity));
        if (dict is not null)
            await _db.Collection("agent_activities").AddAsync(dict, ct);
    }

    public async Task<List<AgentActivityDto>> GetRecentActivityAsync(int limit = 50, CancellationToken ct = default)
    {
        var snapshot = await _db.Collection("agent_activities")
            .OrderByDescending("Timestamp")
            .Limit(limit)
            .GetSnapshotAsync(ct);

        return snapshot.Documents.Select(d =>
        {
            var dict = d.ToDictionary();
            var agentTypeStr = dict.GetValueOrDefault("AgentType")?.ToString() ?? "Human";
            var platformStr = dict.GetValueOrDefault("Platform")?.ToString();

            var agentType = Enum.TryParse<AgentType>(agentTypeStr, true, out var parsedAgentType)
                ? parsedAgentType
                : AgentType.Human;

            var platform = platformStr != null && Enum.TryParse<Platform>(platformStr, true, out var parsedPlatform)
                ? parsedPlatform
                : null as Platform?;

            return new AgentActivityDto(
                AgentType: agentType,
                Action: dict.GetValueOrDefault("Action")?.ToString() ?? "",
                Description: dict.GetValueOrDefault("Description")?.ToString() ?? "",
                Timestamp: d.GetValue<DateTime>("Timestamp"),
                BranchName: dict.GetValueOrDefault("BranchName")?.ToString(),
                Platform: platform
            );
        }).ToList();
    }

    public async Task SyncWorkItemsAsync(List<WorkItem> items, CancellationToken ct = default)
    {
        var batch = _db.StartBatch();
        foreach (var item in items)
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(JsonSerializer.Serialize(item));
            if (dict is not null)
                batch.Set(_db.Collection("work_items").Document(item.Id), dict);
        }
        await batch.CommitAsync(ct);
    }
}
