using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Dtos;

public record WorkItemDto(
    string Id,
    string Title,
    string Description,
    string Milestone,
    Platform Platform,
    string? AssignedAgent,
    WorkItemStatus Status,
    WorkItemPriority Priority,
    WorkItemType Type,
    string? BranchName,
    string? PrUrl,
    DateTime CreatedAt,
    DateTime UpdatedAt
);
