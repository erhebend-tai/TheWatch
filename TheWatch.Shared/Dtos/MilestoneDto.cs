namespace TheWatch.Shared.Dtos;

public record MilestoneDto(
    string Id,
    string Name,
    string Description,
    DateTime DueDate,
    int TotalIssues,
    int ClosedIssues,
    int PercentComplete
);
