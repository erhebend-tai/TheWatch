using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Enums;

namespace TheWatch.Adapters.Mock;

/// <summary>
/// Mock implementation of IGitHubPort for testing and development.
/// Returns seeded mock data including milestones, work items, branches, and agent activity.
/// </summary>
public class MockGitHubAdapter : IGitHubPort
{
    public Task<List<Milestone>> GetMilestonesAsync(CancellationToken ct = default)
    {
        var milestones = new List<Milestone>
        {
            new Milestone
            {
                Id = "M0",
                Name = "Foundation",
                Description = "Core infrastructure setup",
                DueDate = DateTime.UtcNow.AddMonths(1),
                TotalIssues = 12,
                ClosedIssues = 8
            },
            new Milestone
            {
                Id = "M1",
                Name = "Alpha Release",
                Description = "Initial feature set",
                DueDate = DateTime.UtcNow.AddMonths(2),
                TotalIssues = 25,
                ClosedIssues = 12
            },
            new Milestone
            {
                Id = "M2",
                Name = "Beta Release",
                Description = "Extended features and optimization",
                DueDate = DateTime.UtcNow.AddMonths(3),
                TotalIssues = 18,
                ClosedIssues = 5
            },
            new Milestone
            {
                Id = "M3",
                Name = "GA Release",
                Description = "Production-ready release",
                DueDate = DateTime.UtcNow.AddMonths(4),
                TotalIssues = 22,
                ClosedIssues = 0
            },
            new Milestone
            {
                Id = "M4",
                Name = "Post-GA Improvements",
                Description = "Performance and stability enhancements",
                DueDate = DateTime.UtcNow.AddMonths(5),
                TotalIssues = 15,
                ClosedIssues = 0
            },
            new Milestone
            {
                Id = "M5",
                Name = "Advanced Features",
                Description = "Complex feature implementations",
                DueDate = DateTime.UtcNow.AddMonths(6),
                TotalIssues = 20,
                ClosedIssues = 0
            },
            new Milestone
            {
                Id = "M6",
                Name = "Integration Phase",
                Description = "Third-party integrations",
                DueDate = DateTime.UtcNow.AddMonths(7),
                TotalIssues = 14,
                ClosedIssues = 0
            },
            new Milestone
            {
                Id = "M7",
                Name = "Long-term Vision",
                Description = "Future capabilities roadmap",
                DueDate = DateTime.UtcNow.AddMonths(12),
                TotalIssues = 30,
                ClosedIssues = 0
            }
        };

        return Task.FromResult(milestones);
    }

    public Task<List<WorkItem>> GetIssuesByMilestoneAsync(string milestone, CancellationToken ct = default)
    {
        var items = new List<WorkItem>
        {
            new WorkItem
            {
                Id = "GH-101",
                Title = "Setup project structure",
                Description = "Initialize repository with base project structure",
                Milestone = milestone,
                Platform = Platform.GitHub,
                AssignedAgent = "ClaudeCode",
                Status = WorkItemStatus.Done,
                Priority = WorkItemPriority.High,
                Type = WorkItemType.Task,
                BranchName = "agent/claude-code/feature/project-setup",
                CreatedAt = DateTime.UtcNow.AddDays(-20),
                UpdatedAt = DateTime.UtcNow.AddDays(-15)
            },
            new WorkItem
            {
                Id = "GH-102",
                Title = "Configure CI/CD pipeline",
                Description = "Setup GitHub Actions for automated builds",
                Milestone = milestone,
                Platform = Platform.GitHub,
                AssignedAgent = "GeminiPro",
                Status = WorkItemStatus.InProgress,
                Priority = WorkItemPriority.High,
                Type = WorkItemType.Task,
                BranchName = "agent/gemini-pro/feature/cicd-setup",
                CreatedAt = DateTime.UtcNow.AddDays(-15),
                UpdatedAt = DateTime.UtcNow.AddDays(-2)
            },
            new WorkItem
            {
                Id = "GH-103",
                Title = "Implement core API endpoints",
                Description = "Build REST API for primary operations",
                Milestone = milestone,
                Platform = Platform.GitHub,
                AssignedAgent = "AzureOpenAI",
                Status = WorkItemStatus.InReview,
                Priority = WorkItemPriority.High,
                Type = WorkItemType.Feature,
                BranchName = "agent/azure-openai/feature/api-endpoints",
                PrUrl = "https://github.com/example/repo/pull/42",
                CreatedAt = DateTime.UtcNow.AddDays(-10),
                UpdatedAt = DateTime.UtcNow.AddDays(-1)
            },
            new WorkItem
            {
                Id = "GH-104",
                Title = "Add unit tests",
                Description = "Write comprehensive unit tests for core modules",
                Milestone = milestone,
                Platform = Platform.GitHub,
                AssignedAgent = "Copilot",
                Status = WorkItemStatus.Ready,
                Priority = WorkItemPriority.Medium,
                Type = WorkItemType.Task,
                CreatedAt = DateTime.UtcNow.AddDays(-8),
                UpdatedAt = DateTime.UtcNow.AddDays(-3)
            },
            new WorkItem
            {
                Id = "GH-105",
                Title = "Documentation update",
                Description = "Update README and API documentation",
                Milestone = milestone,
                Platform = Platform.GitHub,
                AssignedAgent = "JetBrainsAI",
                Status = WorkItemStatus.Backlog,
                Priority = WorkItemPriority.Low,
                Type = WorkItemType.Task,
                CreatedAt = DateTime.UtcNow.AddDays(-5),
                UpdatedAt = DateTime.UtcNow.AddDays(-5)
            }
        };

        return Task.FromResult(items);
    }

    public Task<List<WorkItem>> GetPullRequestsAsync(CancellationToken ct = default)
    {
        var prs = new List<WorkItem>
        {
            new WorkItem
            {
                Id = "PR-42",
                Title = "Core API Implementation",
                Description = "Implements primary REST API endpoints",
                Milestone = "M1",
                Platform = Platform.GitHub,
                AssignedAgent = "AzureOpenAI",
                Status = WorkItemStatus.InReview,
                Priority = WorkItemPriority.High,
                Type = WorkItemType.Feature,
                BranchName = "agent/azure-openai/feature/api-endpoints",
                PrUrl = "https://github.com/example/repo/pull/42",
                CreatedAt = DateTime.UtcNow.AddDays(-3),
                UpdatedAt = DateTime.UtcNow.AddDays(-1)
            },
            new WorkItem
            {
                Id = "PR-43",
                Title = "CI/CD Pipeline Configuration",
                Description = "GitHub Actions workflow setup",
                Milestone = "M0",
                Platform = Platform.GitHub,
                AssignedAgent = "GeminiPro",
                Status = WorkItemStatus.InReview,
                Priority = WorkItemPriority.High,
                Type = WorkItemType.Task,
                BranchName = "agent/gemini-pro/feature/cicd-setup",
                PrUrl = "https://github.com/example/repo/pull/43",
                CreatedAt = DateTime.UtcNow.AddDays(-2),
                UpdatedAt = DateTime.UtcNow.AddHours(-6)
            }
        };

        return Task.FromResult(prs);
    }

    public Task<List<BranchInfo>> GetBranchesAsync(CancellationToken ct = default)
    {
        var branches = new List<BranchInfo>
        {
            new BranchInfo
            {
                Name = "main",
                Agent = "System",
                Platform = "GitHub",
                IsActive = true,
                LastCommitDate = DateTime.UtcNow.AddHours(-2),
                PrStatus = null
            },
            new BranchInfo
            {
                Name = "develop",
                Agent = "System",
                Platform = "GitHub",
                IsActive = true,
                LastCommitDate = DateTime.UtcNow.AddHours(-4),
                PrStatus = null
            },
            new BranchInfo
            {
                Name = "agent/claude-code/feature/project-setup",
                Agent = "ClaudeCode",
                Platform = "GitHub",
                IsActive = false,
                LastCommitDate = DateTime.UtcNow.AddDays(-15),
                PrStatus = "Merged"
            },
            new BranchInfo
            {
                Name = "agent/gemini-pro/feature/cicd-setup",
                Agent = "GeminiPro",
                Platform = "GitHub",
                IsActive = true,
                LastCommitDate = DateTime.UtcNow.AddHours(-6),
                PrStatus = "Open"
            },
            new BranchInfo
            {
                Name = "agent/azure-openai/feature/api-endpoints",
                Agent = "AzureOpenAI",
                Platform = "GitHub",
                IsActive = true,
                LastCommitDate = DateTime.UtcNow.AddHours(-1),
                PrStatus = "Open"
            },
            new BranchInfo
            {
                Name = "agent/copilot/feature/unit-tests",
                Agent = "Copilot",
                Platform = "GitHub",
                IsActive = true,
                LastCommitDate = DateTime.UtcNow.AddHours(-8),
                PrStatus = null
            },
            new BranchInfo
            {
                Name = "agent/jetbrains-ai/feature/docs",
                Agent = "JetBrainsAI",
                Platform = "GitHub",
                IsActive = true,
                LastCommitDate = DateTime.UtcNow.AddDays(-1),
                PrStatus = null
            },
            new BranchInfo
            {
                Name = "agent/junie/bugfix/memory-leak",
                Agent = "Junie",
                Platform = "GitHub",
                IsActive = true,
                LastCommitDate = DateTime.UtcNow.AddHours(-12),
                PrStatus = null
            }
        };

        return Task.FromResult(branches);
    }

    public Task<List<BuildStatus>> GetWorkflowRunsAsync(CancellationToken ct = default)
    {
        var builds = new List<BuildStatus>
        {
            new BuildStatus
            {
                WorkflowName = "Build and Test",
                RunId = "run-12345",
                Status = BuildResult.Success,
                Platform = Platform.GitHub,
                DurationSeconds = 180,
                TriggeredBy = "ClaudeCode",
                Url = "https://github.com/example/repo/actions/runs/12345",
                StartedAt = DateTime.UtcNow.AddHours(-2)
            },
            new BuildStatus
            {
                WorkflowName = "Build and Test",
                RunId = "run-12346",
                Status = BuildResult.InProgress,
                Platform = Platform.GitHub,
                DurationSeconds = 45,
                TriggeredBy = "GeminiPro",
                Url = "https://github.com/example/repo/actions/runs/12346",
                StartedAt = DateTime.UtcNow.AddMinutes(-45)
            },
            new BuildStatus
            {
                WorkflowName = "Deploy",
                RunId = "run-12344",
                Status = BuildResult.Success,
                Platform = Platform.GitHub,
                DurationSeconds = 240,
                TriggeredBy = "System",
                Url = "https://github.com/example/repo/actions/runs/12344",
                StartedAt = DateTime.UtcNow.AddHours(-6)
            }
        };

        return Task.FromResult(builds);
    }

    public Task<List<AgentActivity>> GetAgentActivityAsync(CancellationToken ct = default)
    {
        var agentTypes = new[]
        {
            AgentType.ClaudeCode,
            AgentType.GeminiPro,
            AgentType.AzureOpenAI,
            AgentType.Copilot,
            AgentType.JetBrainsAI,
            AgentType.Junie
        };

        var activities = new List<AgentActivity>();
        var baseTime = DateTime.UtcNow;

        foreach (var agent in agentTypes)
        {
            activities.Add(new AgentActivity
            {
                AgentType = agent,
                Action = "pushed code",
                Description = $"{agent} pushed changes to feature branch",
                Timestamp = baseTime.AddHours(-Random.Shared.Next(1, 24)),
                BranchName = $"agent/{agent.ToString().ToLowerInvariant()}/feature/task-{Random.Shared.Next(100, 200)}",
                Platform = Platform.GitHub
            });

            activities.Add(new AgentActivity
            {
                AgentType = agent,
                Action = "created PR",
                Description = $"{agent} opened a pull request",
                Timestamp = baseTime.AddHours(-Random.Shared.Next(1, 12)),
                BranchName = $"agent/{agent.ToString().ToLowerInvariant()}/feature/task-{Random.Shared.Next(100, 200)}",
                Platform = Platform.GitHub
            });

            activities.Add(new AgentActivity
            {
                AgentType = agent,
                Action = "resolved issue",
                Description = $"{agent} marked issue as resolved",
                Timestamp = baseTime.AddHours(-Random.Shared.Next(2, 48)),
                Platform = Platform.GitHub
            });
        }

        return Task.FromResult(activities.OrderByDescending(a => a.Timestamp).ToList());
    }
}
