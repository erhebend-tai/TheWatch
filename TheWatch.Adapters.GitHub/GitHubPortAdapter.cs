using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Adapters.GitHub;

/// <summary>
/// Real GitHub implementation of IGitHubPort.
/// Currently a stub with TODO comments.
/// TODO: Implement real GitHub API integration using Octokit.
/// </summary>
public class GitHubPortAdapter : IGitHubPort
{
    // TODO: Inject IConfiguration and GitHubClient from dependency injection
    // private readonly GitHubClient _githubClient;
    // private readonly IConfiguration _config;

    public Task<List<Milestone>> GetMilestonesAsync(CancellationToken ct = default)
    {
        // TODO: Implement real GitHub API call
        // 1. Get repository from configuration
        // 2. Query GitHub API for milestones
        // 3. Map to Milestone domain model
        // 4. Handle pagination and errors
        throw new NotImplementedException("GitHub adapter requires configuration");
    }

    public Task<List<WorkItem>> GetIssuesByMilestoneAsync(string milestone, CancellationToken ct = default)
    {
        // TODO: Implement real GitHub API call
        // 1. Get repository from configuration
        // 2. Query GitHub API for issues filtered by milestone
        // 3. Parse branch names for agent assignments
        // 4. Map to WorkItem domain model
        // 5. Handle pagination and filtering
        throw new NotImplementedException("GitHub adapter requires configuration");
    }

    public Task<List<WorkItem>> GetPullRequestsAsync(CancellationToken ct = default)
    {
        // TODO: Implement real GitHub API call
        // 1. Get repository from configuration
        // 2. Query GitHub API for pull requests
        // 3. Parse branch naming convention (agent/*/feature/*)
        // 4. Map to WorkItem domain model
        // 5. Include PR URLs and status
        throw new NotImplementedException("GitHub adapter requires configuration");
    }

    public Task<List<BranchInfo>> GetBranchesAsync(CancellationToken ct = default)
    {
        // TODO: Implement real GitHub API call
        // 1. Get repository from configuration
        // 2. Query GitHub API for all branches
        // 3. Parse agent naming convention from branch names
        // 4. Check for associated pull requests
        // 5. Return branch info with agent assignments
        throw new NotImplementedException("GitHub adapter requires configuration");
    }

    public Task<List<BuildStatus>> GetWorkflowRunsAsync(CancellationToken ct = default)
    {
        // TODO: Implement real GitHub API call
        // 1. Get repository from configuration
        // 2. Query GitHub API for workflow runs
        // 3. Get latest runs from each workflow
        // 4. Map to BuildStatus domain model
        // 5. Include run URLs and durations
        throw new NotImplementedException("GitHub adapter requires configuration");
    }

    public Task<List<AgentActivity>> GetAgentActivityAsync(CancellationToken ct = default)
    {
        // TODO: Implement real GitHub API call
        // 1. Get repository from configuration
        // 2. Query GitHub API for recent events (pushes, PRs, issues)
        // 3. Parse branch names to identify agent actors
        // 4. Map to AgentActivity domain model
        // 5. Filter for agent-assigned branches (agent/*/*)
        throw new NotImplementedException("GitHub adapter requires configuration");
    }
}
