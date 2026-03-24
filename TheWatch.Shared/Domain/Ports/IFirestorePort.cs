// IFirestorePort — domain port for Firestore agent activity and work item sync.
// NO database SDK imports allowed in this file.
// Example:
//   await firestore.LogAgentActivityAsync(activity);
//   var recent = await firestore.GetRecentActivityAsync(limit: 20);
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Dtos;

namespace TheWatch.Shared.Domain.Ports;

public interface IFirestorePort
{
    Task LogAgentActivityAsync(AgentActivity activity, CancellationToken ct = default);
    Task<List<AgentActivityDto>> GetRecentActivityAsync(int limit = 50, CancellationToken ct = default);
    Task SyncWorkItemsAsync(List<WorkItem> items, CancellationToken ct = default);
}
