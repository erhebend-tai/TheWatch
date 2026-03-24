using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Dtos;

public record AgentActivityDto(
    AgentType AgentType,
    string Action,
    string Description,
    DateTime Timestamp,
    string? BranchName,
    Platform? Platform
);
