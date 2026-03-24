namespace TheWatch.Shared.Dtos;

public record BranchInfoDto(
    string Name,
    string Agent,
    string Platform,
    bool IsActive,
    DateTime LastCommitDate,
    string? PrStatus
);
