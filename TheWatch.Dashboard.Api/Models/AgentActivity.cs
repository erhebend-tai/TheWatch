using TheWatch.Shared.Enums;

namespace TheWatch.Dashboard.Api.Models;

public class AgentActivity
{
    public AgentType AgentType { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? BranchName { get; set; }
    public Platform? Platform { get; set; }
}
