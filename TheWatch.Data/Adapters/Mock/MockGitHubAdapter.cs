// MockGitHubAdapter — returns seeded milestone/workitem/branch data for dev/test.
// Example:
//   services.AddSingleton<IGitHubPort, MockGitHubAdapter>();
//   var milestones = await github.GetMilestonesAsync();
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Enums;

namespace TheWatch.Data.Adapters.Mock;

public class MockGitHubAdapter : IGitHubPort
{
    public Task<List<Milestone>> GetMilestonesAsync(CancellationToken ct = default) =>
        Task.FromResult(new List<Milestone>
        {
            new() { Id = "M0", Name = "M0 – Infrastructure", Description = "CI/CD, Aspire, databases", DueDate = new DateTime(2025, 3, 15), TotalIssues = 12, ClosedIssues = 12 },
            new() { Id = "M1", Name = "M1 – Auth & Onboarding", Description = "Login, signup, 2FA, EULA", DueDate = new DateTime(2025, 4, 1), TotalIssues = 18, ClosedIssues = 14 },
            new() { Id = "M2", Name = "M2 – Core Safety", Description = "SOS trigger, check-in, responder dispatch", DueDate = new DateTime(2025, 5, 1), TotalIssues = 24, ClosedIssues = 8 },
            new() { Id = "M3", Name = "M3 – Evidence & Audit", Description = "Photo/video capture, tamper-evident audit", DueDate = new DateTime(2025, 6, 1), TotalIssues = 16, ClosedIssues = 2 },
        });

    public Task<List<WorkItem>> GetIssuesByMilestoneAsync(string milestone, CancellationToken ct = default) =>
        Task.FromResult(new List<WorkItem>
        {
            new() { Id = $"{milestone}-1", Title = $"[{milestone}] Setup infrastructure", Milestone = milestone, Status = WorkItemStatus.Done, Platform = Platform.Backend, Priority = WorkItemPriority.Critical, Type = WorkItemType.Feature, CreatedAt = DateTime.UtcNow.AddDays(-30), UpdatedAt = DateTime.UtcNow },
            new() { Id = $"{milestone}-2", Title = $"[{milestone}] Integration tests", Milestone = milestone, Status = WorkItemStatus.InProgress, Platform = Platform.Backend, Priority = WorkItemPriority.High, Type = WorkItemType.Task, CreatedAt = DateTime.UtcNow.AddDays(-20), UpdatedAt = DateTime.UtcNow },
        });

    public Task<List<WorkItem>> GetPullRequestsAsync(CancellationToken ct = default) =>
        Task.FromResult(new List<WorkItem>
        {
            new() { Id = "pr-101", Title = "feat: add SOS trigger endpoint", Status = WorkItemStatus.InReview, Platform = Platform.Backend, Priority = WorkItemPriority.Critical, Type = WorkItemType.Feature, BranchName = "feature/sos-trigger", PrUrl = "https://github.com/thewatch/pulls/101", CreatedAt = DateTime.UtcNow.AddDays(-2), UpdatedAt = DateTime.UtcNow },
        });

    public Task<List<BranchInfo>> GetBranchesAsync(CancellationToken ct = default) =>
        Task.FromResult(new List<BranchInfo>
        {
            new() { Name = "main", Agent = "CI", Platform = "All", IsActive = true, LastCommitDate = DateTime.UtcNow },
            new() { Name = "feature/sos-trigger", Agent = "Claude", Platform = "Backend", IsActive = true, LastCommitDate = DateTime.UtcNow.AddHours(-2), PrStatus = "Open" },
            new() { Name = "feature/spatial-index", Agent = "Claude", Platform = "Backend", IsActive = true, LastCommitDate = DateTime.UtcNow.AddHours(-6) },
        });

    public Task<List<BuildStatus>> GetWorkflowRunsAsync(CancellationToken ct = default) =>
        Task.FromResult(new List<BuildStatus>
        {
            new() { WorkflowName = "CI Build", RunId = "run-1001", Status = BuildResult.Succeeded, Platform = Platform.Backend, DurationSeconds = 142, TriggeredBy = "push", Url = "https://github.com/thewatch/actions/runs/1001", StartedAt = DateTime.UtcNow.AddHours(-1) },
            new() { WorkflowName = "Android Build", RunId = "run-1002", Status = BuildResult.InProgress, Platform = Platform.Android, DurationSeconds = 0, TriggeredBy = "push", Url = "https://github.com/thewatch/actions/runs/1002", StartedAt = DateTime.UtcNow.AddMinutes(-5) },
        });

    public Task<List<AgentActivity>> GetAgentActivityAsync(CancellationToken ct = default) =>
        Task.FromResult(new List<AgentActivity>
        {
            new() { AgentType = AgentType.Claude, Action = "commit", Description = "Implemented hexagonal port/adapter layer", Timestamp = DateTime.UtcNow.AddMinutes(-30), BranchName = "feature/hex-arch", Platform = Platform.Backend },
            new() { AgentType = AgentType.GitHubActions, Action = "build", Description = "CI pipeline succeeded", Timestamp = DateTime.UtcNow.AddMinutes(-15), Platform = Platform.Backend },
        });
}
