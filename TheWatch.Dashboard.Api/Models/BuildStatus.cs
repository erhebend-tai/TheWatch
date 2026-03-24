using TheWatch.Shared.Enums;

namespace TheWatch.Dashboard.Api.Models;

public class BuildStatus
{
    public string WorkflowName { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public BuildResult Status { get; set; }
    public Platform Platform { get; set; }
    public int DurationSeconds { get; set; }
    public string TriggeredBy { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
}
