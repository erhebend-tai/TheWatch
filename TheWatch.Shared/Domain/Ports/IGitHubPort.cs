// IGitHubPort — domain port for GitHub integration (milestones, issues, PRs, builds).
// NO database SDK imports allowed in this file.
// Example:
//   var milestones = await github.GetMilestonesAsync();
//   var builds = await github.GetWorkflowRunsAsync();
using TheWatch.Shared.Domain.Models;

namespace TheWatch.Shared.Domain.Ports;

public interface IGitHubPort
{
    Task<List<Milestone>> GetMilestonesAsync(CancellationToken ct = default);
    Task<List<WorkItem>> GetIssuesByMilestoneAsync(string milestone, CancellationToken ct = default);
    Task<List<WorkItem>> GetPullRequestsAsync(CancellationToken ct = default);
    Task<List<BranchInfo>> GetBranchesAsync(CancellationToken ct = default);
    Task<List<BuildStatus>> GetWorkflowRunsAsync(CancellationToken ct = default);
    Task<List<AgentActivity>> GetAgentActivityAsync(CancellationToken ct = default);
}
