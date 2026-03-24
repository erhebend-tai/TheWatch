// SwarmTask — a unit of work flowing through a swarm.
// Tasks enter at the entry point agent, get classified, and flow through handoffs
// until a terminal agent produces a final response.
//
// Example:
//   var task = new SwarmTask
//   {
//       Input = "User triggered SOS at 38.9072, -77.0369 with phrase 'help me now'",
//       SwarmId = "safety-report-pipeline"
//   };
//   var result = await swarmPort.RunTaskAsync(task, ct);

using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Domain.Models;

public class SwarmTask
{
    /// <summary>Unique task identifier.</summary>
    public string TaskId { get; set; } = Guid.NewGuid().ToString("N")[..12];

    /// <summary>Which swarm definition to run this task through.</summary>
    public string SwarmId { get; set; } = string.Empty;

    /// <summary>The user's input / prompt that kicks off the swarm.</summary>
    public string Input { get; set; } = string.Empty;

    /// <summary>Current status of this task.</summary>
    public SwarmTaskStatus Status { get; set; } = SwarmTaskStatus.Queued;

    /// <summary>Agent currently processing this task.</summary>
    public string? CurrentAgentId { get; set; }

    /// <summary>Final output from the swarm (set when Status = Completed).</summary>
    public string? Output { get; set; }

    /// <summary>Error message if the task failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Conversation history — all messages across all agent turns.</summary>
    public List<SwarmMessage> Messages { get; set; } = [];

    /// <summary>Ordered log of which agents handled this task and when.</summary>
    public List<SwarmHandoffRecord> HandoffHistory { get; set; } = [];

    /// <summary>Number of handoffs so far (used to enforce MaxHandoffDepth).</summary>
    public int HandoffCount { get; set; }

    /// <summary>Total tokens consumed across all agent turns.</summary>
    public int TotalTokensUsed { get; set; }

    /// <summary>Total cost in USD (estimated from token counts and model pricing).</summary>
    public decimal EstimatedCostUsd { get; set; }

    /// <summary>When the task was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>When the task completed or failed.</summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>Duration from creation to completion.</summary>
    public TimeSpan? Duration => CompletedAt.HasValue ? CompletedAt.Value - CreatedAt : null;

    /// <summary>Optional context variables passed between agents (like swarm context_variables).</summary>
    public Dictionary<string, string> ContextVariables { get; set; } = [];

    /// <summary>Optional metadata for the task.</summary>
    public Dictionary<string, string> Metadata { get; set; } = [];
}

/// <summary>A single message in the swarm conversation (maps to ChatMessage).</summary>
public class SwarmMessage
{
    /// <summary>Role: "system", "user", "assistant", "tool".</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>Message content.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Which agent produced this message (null for user messages).</summary>
    public string? AgentId { get; set; }

    /// <summary>Tool call ID if this is a tool response.</summary>
    public string? ToolCallId { get; set; }

    /// <summary>Tool calls requested by the assistant.</summary>
    public List<SwarmToolCall>? ToolCalls { get; set; }

    /// <summary>Timestamp.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>A tool call requested by an agent.</summary>
public class SwarmToolCall
{
    public string Id { get; set; } = string.Empty;
    public string FunctionName { get; set; } = string.Empty;
    public string ArgumentsJson { get; set; } = "{}";
}

/// <summary>Records a handoff from one agent to another.</summary>
public class SwarmHandoffRecord
{
    public string FromAgentId { get; set; } = string.Empty;
    public string ToAgentId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
