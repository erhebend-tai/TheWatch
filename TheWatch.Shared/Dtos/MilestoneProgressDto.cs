using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Dtos;

public record MilestoneProgressDto(
    string MilestoneId,
    string Name,
    Dictionary<string, int> IssuesByStatus,
    Dictionary<string, int> IssuesByPlatform,
    Dictionary<string, int> IssuesByAgent
);
