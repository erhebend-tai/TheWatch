using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Domain.Models;

public class WorkItem
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Milestone { get; set; } = string.Empty;
    public Platform Platform { get; set; }
    public string? AssignedAgent { get; set; }
    public WorkItemStatus Status { get; set; }
    public WorkItemPriority Priority { get; set; }
    public WorkItemType Type { get; set; }
    public string? BranchName { get; set; }
    public string? PrUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
