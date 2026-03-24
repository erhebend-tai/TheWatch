using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Dtos;

public record BuildStatusDto(
    string WorkflowName,
    string RunId,
    BuildResult Status,
    Platform Platform,
    int DurationSeconds,
    string TriggeredBy,
    string Url,
    DateTime StartedAt
);
