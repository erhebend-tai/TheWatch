// SwarmAgentDefinition — declares a single agent within a swarm topology.
// Each agent has a role, a system prompt (instructions), a set of tool/function declarations,
// and a list of agent IDs it can hand off tasks to.
//
// Azure OpenAI maps this to an Assistants API assistant with tool_choice + handoff functions.
// OpenAI Swarm SDK maps this to Agent(name, instructions, functions, handoffs).
//
// Example:
//   var triage = new SwarmAgentDefinition
//   {
//       AgentId = "triage-01",
//       Name = "Triage Agent",
//       Role = SwarmRole.Triage,
//       Model = "gpt-4o",
//       Instructions = "You classify incoming safety reports...",
//       HandoffTargets = ["evidence-analyst", "location-resolver"],
//       Tools = [new SwarmToolDefinition { Name = "classify_report", ... }]
//   };

using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Domain.Models;

public class SwarmAgentDefinition
{
    /// <summary>Unique identifier for this agent within the swarm.</summary>
    public string AgentId { get; set; } = string.Empty;

    /// <summary>Human-readable name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Role this agent plays (Triage, Specialist, Reviewer, etc.).</summary>
    public SwarmRole Role { get; set; }

    /// <summary>Azure OpenAI deployment name or model ID (e.g., "gpt-4o", "gpt-4o-mini").</summary>
    public string Model { get; set; } = "gpt-4o";

    /// <summary>System prompt / instructions for this agent.</summary>
    public string Instructions { get; set; } = string.Empty;

    /// <summary>
    /// Agent IDs this agent can hand off tasks to.
    /// The swarm runtime generates transfer_to_{target} functions automatically.
    /// </summary>
    public List<string> HandoffTargets { get; set; } = [];

    /// <summary>Tool/function definitions this agent can call.</summary>
    public List<SwarmToolDefinition> Tools { get; set; } = [];

    /// <summary>Maximum tokens per completion request.</summary>
    public int MaxTokens { get; set; } = 4096;

    /// <summary>Temperature for this agent's completions (0.0–2.0).</summary>
    public float Temperature { get; set; } = 0.7f;

    /// <summary>Maximum number of consecutive tool calls before forcing a text response.</summary>
    public int MaxToolRounds { get; set; } = 10;

    /// <summary>Optional parallel tool calls (Azure OpenAI supports this).</summary>
    public bool ParallelToolCalls { get; set; } = true;

    /// <summary>Whether this agent is the swarm's entry point.</summary>
    public bool IsEntryPoint { get; set; }

    /// <summary>Optional metadata tags for filtering and grouping.</summary>
    public Dictionary<string, string> Metadata { get; set; } = [];
}
