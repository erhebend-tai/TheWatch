// SwarmRole — defines the role an agent plays within a swarm.
// Example:
//   var agent = new SwarmAgentDefinition { Role = SwarmRole.Triage };
//   // Triage agent routes incoming tasks to specialized agents

namespace TheWatch.Shared.Enums;

public enum SwarmRole
{
    /// <summary>Entry point agent that classifies and routes tasks to specialists.</summary>
    Triage,

    /// <summary>Executes a specific domain task (e.g., "evidence analyst", "location resolver").</summary>
    Specialist,

    /// <summary>Reviews and validates outputs from other agents before returning to user.</summary>
    Reviewer,

    /// <summary>Coordinates multi-step workflows across multiple specialists.</summary>
    Orchestrator,

    /// <summary>Monitors swarm health, retries failures, and escalates stuck tasks.</summary>
    Supervisor,

    /// <summary>Generates or transforms code artifacts.</summary>
    CodeGen,

    /// <summary>Handles RAG retrieval and context assembly for other agents.</summary>
    ContextProvider,

    /// <summary>Performs safety and compliance checks on outputs.</summary>
    SafetyGuard,

    /// <summary>Summarizes and aggregates results from multiple agents.</summary>
    Aggregator,

    /// <summary>Custom role defined by user instructions.</summary>
    Custom
}
