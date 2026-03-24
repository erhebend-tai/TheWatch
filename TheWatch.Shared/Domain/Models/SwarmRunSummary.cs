// SwarmRunSummary — aggregated metrics from a completed swarm task or batch.
// Example:
//   var summary = await swarmPort.GetRunSummaryAsync(taskId, ct);
//   Console.WriteLine($"Completed in {summary.Duration} with {summary.TotalHandoffs} handoffs");

using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Domain.Models;

public class SwarmRunSummary
{
    public string TaskId { get; set; } = string.Empty;
    public string SwarmId { get; set; } = string.Empty;
    public SwarmTaskStatus FinalStatus { get; set; }
    public TimeSpan Duration { get; set; }
    public int TotalHandoffs { get; set; }
    public int TotalToolCalls { get; set; }
    public int TotalTokensUsed { get; set; }
    public decimal EstimatedCostUsd { get; set; }
    public int AgentsInvolved { get; set; }
    public List<SwarmAgentMetrics> AgentMetrics { get; set; } = [];
    public string? FinalOutput { get; set; }
    public string? ErrorMessage { get; set; }
}

public class SwarmAgentMetrics
{
    public string AgentId { get; set; } = string.Empty;
    public string AgentName { get; set; } = string.Empty;
    public SwarmRole Role { get; set; }
    public int TurnsHandled { get; set; }
    public int ToolCallsMade { get; set; }
    public int TokensUsed { get; set; }
    public TimeSpan TimeSpent { get; set; }
    public int HandoffsInitiated { get; set; }
}
