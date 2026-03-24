using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Enums;

namespace TheWatch.Dashboard.Api.Services;

/// <summary>
/// GitHub service providing mock data for milestones, issues, PRs, and branches.
/// Implements IGitHubPort — the domain port defined in TheWatch.Shared.
/// In production, this would use Octokit to call GitHub API.
/// </summary>
public class GitHubService : IGitHubPort
{
    private readonly ILogger<GitHubService> _logger;

    public GitHubService(ILogger<GitHubService> logger)
    {
        _logger = logger;
    }

    public Task<List<Milestone>> GetMilestonesAsync(CancellationToken ct = default)
    {
        var milestones = new List<Milestone>
        {
            new() { Id = "M0", Name = "M0: Foundation", Description = "Project setup, repo structure, CI/CD pipeline", DueDate = DateTime.Now.AddDays(-30), TotalIssues = 12, ClosedIssues = 12 },
            new() { Id = "M1", Name = "M1: Core Architecture", Description = "Backend services, domain models, database schema", DueDate = DateTime.Now.AddDays(-10), TotalIssues = 24, ClosedIssues = 22 },
            new() { Id = "M2", Name = "M2: Mobile Foundation", Description = "MAUI app skeleton, UI framework, navigation", DueDate = DateTime.Now.AddDays(10), TotalIssues = 18, ClosedIssues = 14 },
            new() { Id = "M3", Name = "M3: Voice Integration", Description = "Alexa skill, intent handling, SSML responses", DueDate = DateTime.Now.AddDays(30), TotalIssues = 20, ClosedIssues = 8 },
            new() { Id = "M4", Name = "M4: Sensor Integration", Description = "Wearable data sync, sensor abstraction, data validation", DueDate = DateTime.Now.AddDays(50), TotalIssues = 22, ClosedIssues = 5 },
            new() { Id = "M5", Name = "M5: Alerting Pipeline", Description = "Alert generation, escalation logic, notification delivery", DueDate = DateTime.Now.AddDays(70), TotalIssues = 25, ClosedIssues = 2 },
            new() { Id = "M6", Name = "M6: Scale & Optimization", Description = "Performance tuning, load testing, infrastructure hardening", DueDate = DateTime.Now.AddDays(90), TotalIssues = 15, ClosedIssues = 0 },
            new() { Id = "M7", Name = "M7: Launch", Description = "Beta release, documentation, user onboarding", DueDate = DateTime.Now.AddDays(110), TotalIssues = 18, ClosedIssues = 0 },
        };
        return Task.FromResult(milestones);
    }

    public Task<List<WorkItem>> GetIssuesByMilestoneAsync(string milestone, CancellationToken ct = default)
    {
        var issues = new List<WorkItem>
        {
            new() { Id = "GH-001", Title = "Setup GitHub repository and initial project structure", Description = "Create repo, add CI/CD workflows, branch protection rules", Milestone = milestone, Platform = Platform.Infra, AssignedAgent = "ClaudeCode", Status = WorkItemStatus.Done, Priority = WorkItemPriority.Critical, Type = WorkItemType.Feature, BranchName = "agent/claude-code/infra/github-setup", PrUrl = "https://github.com/thewatch/thewatch/pull/1", CreatedAt = DateTime.Now.AddDays(-60), UpdatedAt = DateTime.Now.AddDays(-55) },
            new() { Id = "GH-002", Title = "Define domain models and core entities", Description = "User, Device, Sensor, Alert, Milestone entities with relationships", Milestone = milestone, Platform = Platform.Backend, AssignedAgent = "AzureOpenAI", Status = WorkItemStatus.InProgress, Priority = WorkItemPriority.Critical, Type = WorkItemType.Feature, BranchName = "agent/azure-openai/backend/domain-models", CreatedAt = DateTime.Now.AddDays(-40), UpdatedAt = DateTime.Now.AddDays(-2) },
            new() { Id = "GH-003", Title = "Implement database migrations", Description = "EF Core migrations for PostgreSQL schema", Milestone = milestone, Platform = Platform.Database, AssignedAgent = "Copilot", Status = WorkItemStatus.InReview, Priority = WorkItemPriority.High, Type = WorkItemType.Feature, BranchName = "agent/copilot/database/migrations", PrUrl = "https://github.com/thewatch/thewatch/pull/15", CreatedAt = DateTime.Now.AddDays(-35), UpdatedAt = DateTime.Now.AddDays(-1) },
            new() { Id = "GH-004", Title = "Create API health check endpoint", Description = "Implement comprehensive health checks for all services", Milestone = milestone, Platform = Platform.Backend, AssignedAgent = "JetBrainsAI", Status = WorkItemStatus.Done, Priority = WorkItemPriority.Medium, Type = WorkItemType.Feature, BranchName = "agent/jetbrains-ai/backend/health-checks", PrUrl = "https://github.com/thewatch/thewatch/pull/8", CreatedAt = DateTime.Now.AddDays(-30), UpdatedAt = DateTime.Now.AddDays(-20) },
        };
        return Task.FromResult(issues);
    }

    public Task<List<WorkItem>> GetPullRequestsAsync(CancellationToken ct = default)
    {
        var prs = new List<WorkItem>
        {
            new() { Id = "PR-15", Title = "Database migrations for M1", Milestone = "M1", Platform = Platform.Database, AssignedAgent = "Copilot", Status = WorkItemStatus.InReview, Priority = WorkItemPriority.High, Type = WorkItemType.Feature, BranchName = "agent/copilot/database/migrations", PrUrl = "https://github.com/thewatch/thewatch/pull/15", CreatedAt = DateTime.Now.AddDays(-2), UpdatedAt = DateTime.Now.AddDays(-1) },
            new() { Id = "PR-18", Title = "Mobile UI framework setup", Milestone = "M2", Platform = Platform.Android, AssignedAgent = "GeminiPro", Status = WorkItemStatus.InReview, Priority = WorkItemPriority.High, Type = WorkItemType.Feature, BranchName = "agent/gemini-pro/android/ui-framework", PrUrl = "https://github.com/thewatch/thewatch/pull/18", CreatedAt = DateTime.Now.AddDays(-1), UpdatedAt = DateTime.Now },
        };
        return Task.FromResult(prs);
    }

    public Task<List<BranchInfo>> GetBranchesAsync(CancellationToken ct = default)
    {
        var branches = new List<BranchInfo>
        {
            new() { Name = "agent/claude-code/infra/github-setup", Agent = "ClaudeCode", Platform = "Infra", IsActive = false, LastCommitDate = DateTime.Now.AddDays(-55), PrStatus = "Merged" },
            new() { Name = "agent/azure-openai/backend/domain-models", Agent = "AzureOpenAI", Platform = "Backend", IsActive = true, LastCommitDate = DateTime.Now.AddHours(-2), PrStatus = "Open" },
            new() { Name = "agent/copilot/database/migrations", Agent = "Copilot", Platform = "Database", IsActive = true, LastCommitDate = DateTime.Now.AddHours(-6), PrStatus = "Open (In Review)" },
            new() { Name = "agent/gemini-pro/android/ui-framework", Agent = "GeminiPro", Platform = "Android", IsActive = true, LastCommitDate = DateTime.Now.AddHours(-4), PrStatus = "Open (In Review)" },
            new() { Name = "agent/jetbrains-ai/backend/health-checks", Agent = "JetBrainsAI", Platform = "Backend", IsActive = false, LastCommitDate = DateTime.Now.AddDays(-20), PrStatus = "Merged" },
        };
        return Task.FromResult(branches);
    }

    public Task<List<BuildStatus>> GetWorkflowRunsAsync(CancellationToken ct = default)
    {
        var runs = new List<BuildStatus>
        {
            new() { WorkflowName = "Backend CI/CD", RunId = "run-2024-001", Status = BuildResult.Success, Platform = Platform.Backend, DurationSeconds = 420, TriggeredBy = "push to agent/azure-openai/backend/domain-models", Url = "https://github.com/thewatch/thewatch/actions/runs/001", StartedAt = DateTime.Now.AddHours(-3) },
            new() { WorkflowName = "Mobile CI/CD", RunId = "run-2024-002", Status = BuildResult.InProgress, Platform = Platform.Android, DurationSeconds = 180, TriggeredBy = "push to agent/gemini-pro/android/ui-framework", Url = "https://github.com/thewatch/thewatch/actions/runs/002", StartedAt = DateTime.Now.AddHours(-1) },
            new() { WorkflowName = "Database Migration Test", RunId = "run-2024-003", Status = BuildResult.Success, Platform = Platform.Database, DurationSeconds = 300, TriggeredBy = "PR #15 opened", Url = "https://github.com/thewatch/thewatch/actions/runs/003", StartedAt = DateTime.Now.AddDays(-1) },
            new() { WorkflowName = "Backend CI/CD", RunId = "run-2024-004", Status = BuildResult.Failure, Platform = Platform.Backend, DurationSeconds = 240, TriggeredBy = "push to main", Url = "https://github.com/thewatch/thewatch/actions/runs/004", StartedAt = DateTime.Now.AddDays(-2) },
        };
        return Task.FromResult(runs);
    }

    public Task<List<AgentActivity>> GetAgentActivityAsync(CancellationToken ct = default)
    {
        var activities = new List<AgentActivity>
        {
            new() { AgentType = AgentType.AzureOpenAI, Action = "commit", Description = "Add entity relationships to domain models", Timestamp = DateTime.Now.AddHours(-2), BranchName = "agent/azure-openai/backend/domain-models", Platform = Platform.Backend },
            new() { AgentType = AgentType.GeminiPro, Action = "push", Description = "Implement MAUI Shell navigation", Timestamp = DateTime.Now.AddHours(-4), BranchName = "agent/gemini-pro/android/ui-framework", Platform = Platform.Android },
            new() { AgentType = AgentType.Copilot, Action = "pr", Description = "Created PR for database migrations", Timestamp = DateTime.Now.AddDays(-1), BranchName = "agent/copilot/database/migrations", Platform = Platform.Database },
            new() { AgentType = AgentType.JetBrainsAI, Action = "merge", Description = "Merged health check endpoint implementation", Timestamp = DateTime.Now.AddDays(-20), BranchName = "agent/jetbrains-ai/backend/health-checks", Platform = Platform.Backend },
            new() { AgentType = AgentType.ClaudeCode, Action = "merge", Description = "Merged GitHub setup and CI/CD pipeline", Timestamp = DateTime.Now.AddDays(-55), BranchName = "agent/claude-code/infra/github-setup", Platform = Platform.Infra },
        };
        return Task.FromResult(activities);
    }
}
