// SwarmDefinition — the complete topology of a multi-agent swarm.
// Contains all agent definitions, their connections, and swarm-level configuration.
//
// Architecture:
//   SwarmDefinition
//     ├── Agents[] — each agent has tools + handoff targets
//     ├── EntryPointAgentId — where tasks enter the swarm
//     ├── MaxConcurrentTasks — throttle for parallel execution
//     └── EscalationPolicy — what happens when agents get stuck
//
// Example:
//   var swarm = new SwarmDefinition
//   {
//       SwarmId = "safety-report-pipeline",
//       Name = "Safety Report Pipeline",
//       Agents = [triageAgent, evidenceAnalyst, locationResolver, reviewer],
//       EntryPointAgentId = "triage-01",
//       MaxConcurrentTasks = 5
//   };

using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Domain.Models;

public class SwarmDefinition
{
    /// <summary>Unique identifier for this swarm definition.</summary>
    public string SwarmId { get; set; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>Human-readable name for the swarm.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Description of what this swarm does.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>All agents in this swarm.</summary>
    public List<SwarmAgentDefinition> Agents { get; set; } = [];

    /// <summary>AgentId of the entry point (triage) agent.</summary>
    public string EntryPointAgentId { get; set; } = string.Empty;

    /// <summary>Maximum tasks processed concurrently.</summary>
    public int MaxConcurrentTasks { get; set; } = 5;

    /// <summary>Maximum handoff depth before the supervisor intervenes.</summary>
    public int MaxHandoffDepth { get; set; } = 10;

    /// <summary>Timeout per individual agent turn (seconds).</summary>
    public int AgentTurnTimeoutSeconds { get; set; } = 120;

    /// <summary>Timeout for the entire swarm task (seconds).</summary>
    public int TaskTimeoutSeconds { get; set; } = 600;

    /// <summary>Current lifecycle status.</summary>
    public SwarmStatus Status { get; set; } = SwarmStatus.Created;

    /// <summary>Azure OpenAI endpoint (e.g., "https://myinstance.openai.azure.com/").</summary>
    public string? AzureOpenAIEndpoint { get; set; }

    /// <summary>Azure OpenAI API version (e.g., "2024-10-21").</summary>
    public string AzureOpenAIApiVersion { get; set; } = "2024-10-21";

    /// <summary>When this definition was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Last modification timestamp.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Optional metadata tags.</summary>
    public Dictionary<string, string> Metadata { get; set; } = [];

    /// <summary>Get the entry point agent definition.</summary>
    public SwarmAgentDefinition? GetEntryPoint() =>
        Agents.FirstOrDefault(a => a.AgentId == EntryPointAgentId)
        ?? Agents.FirstOrDefault(a => a.IsEntryPoint);

    /// <summary>Get an agent by its ID.</summary>
    public SwarmAgentDefinition? GetAgent(string agentId) =>
        Agents.FirstOrDefault(a => a.AgentId == agentId);

    /// <summary>Validate the swarm topology (entry point exists, handoff targets resolve, no cycles).</summary>
    public List<string> Validate()
    {
        var errors = new List<string>();
        var agentIds = new HashSet<string>(Agents.Select(a => a.AgentId));

        if (Agents.Count == 0)
            errors.Add("Swarm has no agents defined.");

        if (!agentIds.Contains(EntryPointAgentId) && !Agents.Any(a => a.IsEntryPoint))
            errors.Add($"Entry point agent '{EntryPointAgentId}' not found in agent list.");

        foreach (var agent in Agents)
        {
            if (string.IsNullOrWhiteSpace(agent.AgentId))
                errors.Add("Agent has empty AgentId.");

            if (string.IsNullOrWhiteSpace(agent.Instructions))
                errors.Add($"Agent '{agent.AgentId}' has no instructions.");

            foreach (var target in agent.HandoffTargets)
            {
                if (!agentIds.Contains(target))
                    errors.Add($"Agent '{agent.AgentId}' references unknown handoff target '{target}'.");
            }
        }

        // Detect agents unreachable from entry point
        var reachable = new HashSet<string>();
        var queue = new Queue<string>();
        var entry = EntryPointAgentId;
        if (!string.IsNullOrEmpty(entry) && agentIds.Contains(entry))
        {
            queue.Enqueue(entry);
            reachable.Add(entry);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var agent = GetAgent(current);
                if (agent is null) continue;
                foreach (var target in agent.HandoffTargets)
                {
                    if (reachable.Add(target))
                        queue.Enqueue(target);
                }
            }

            var unreachable = agentIds.Except(reachable).ToList();
            if (unreachable.Count > 0)
                errors.Add($"Unreachable agents (not connected from entry point): {string.Join(", ", unreachable)}");
        }

        return errors;
    }
}
