// ISwarmPort — domain port for Azure OpenAI multi-agent swarm orchestration.
// NO AI SDK imports allowed in this file.
//
// Architecture:
//   CLI Command → ISwarmPort.CreateSwarmAsync / RunTaskAsync / ListSwarmsAsync
//     → Adapter (AzureOpenAISwarmAdapter) → Azure OpenAI Chat Completions API
//       → Agent loop: prompt → tool calls → handoffs → next agent → ... → final response
//
// The swarm pattern (from OpenAI Swarm SDK) implemented via Azure OpenAI:
//   1. Each agent = system prompt + tools + handoff functions
//   2. Handoff = a tool call like transfer_to_evidence_analyst() that switches the active agent
//   3. Context variables flow between agents (shared state)
//   4. The loop runs until an agent produces a text response without tool calls
//
// Example:
//   var swarm = SwarmPresets.SafetyReportPipeline();
//   await swarmPort.CreateSwarmAsync(swarm, ct);
//   var task = new SwarmTask { SwarmId = swarm.SwarmId, Input = "SOS triggered at 38.9, -77.0" };
//   var result = await swarmPort.RunTaskAsync(task, ct);
//   Console.WriteLine(result.Data!.Output);

using TheWatch.Shared.Domain.Models;

namespace TheWatch.Shared.Domain.Ports;

public interface ISwarmPort
{
    // ── Swarm Lifecycle ─────────────────────────────────────────────

    /// <summary>Register a swarm definition. Validates topology before storing.</summary>
    Task<StorageResult<SwarmDefinition>> CreateSwarmAsync(SwarmDefinition definition, CancellationToken ct = default);

    /// <summary>Update an existing swarm definition (agents, config, etc.).</summary>
    Task<StorageResult<SwarmDefinition>> UpdateSwarmAsync(SwarmDefinition definition, CancellationToken ct = default);

    /// <summary>Delete a swarm definition by ID.</summary>
    Task<StorageResult<bool>> DeleteSwarmAsync(string swarmId, CancellationToken ct = default);

    /// <summary>Get a swarm definition by ID.</summary>
    Task<StorageResult<SwarmDefinition>> GetSwarmAsync(string swarmId, CancellationToken ct = default);

    /// <summary>List all registered swarm definitions.</summary>
    Task<StorageResult<List<SwarmDefinition>>> ListSwarmsAsync(CancellationToken ct = default);

    // ── Task Execution ──────────────────────────────────────────────

    /// <summary>
    /// Run a task through a swarm. This is the main execution loop:
    ///   1. Start at the entry point agent
    ///   2. Send the task input as a user message
    ///   3. Process tool calls (including handoffs) in a loop
    ///   4. Return when an agent produces a final text response or max depth is reached
    /// </summary>
    Task<StorageResult<SwarmTask>> RunTaskAsync(SwarmTask task, CancellationToken ct = default);

    /// <summary>
    /// Run a task with streaming — progress callbacks fire on each agent turn, tool call, and handoff.
    /// </summary>
    Task<StorageResult<SwarmTask>> RunTaskStreamingAsync(
        SwarmTask task,
        Action<SwarmMessage>? onMessage = null,
        Action<SwarmHandoffRecord>? onHandoff = null,
        Action<SwarmToolCall>? onToolCall = null,
        CancellationToken ct = default);

    /// <summary>Cancel a running task.</summary>
    Task<StorageResult<bool>> CancelTaskAsync(string taskId, CancellationToken ct = default);

    /// <summary>Get the current state of a task.</summary>
    Task<StorageResult<SwarmTask>> GetTaskAsync(string taskId, CancellationToken ct = default);

    /// <summary>List recent tasks, optionally filtered by swarm ID.</summary>
    Task<StorageResult<List<SwarmTask>>> ListTasksAsync(string? swarmId = null, int limit = 50, CancellationToken ct = default);

    // ── Metrics & Observability ────────────────────────────────────

    /// <summary>Get aggregated run metrics for a completed task.</summary>
    Task<StorageResult<SwarmRunSummary>> GetRunSummaryAsync(string taskId, CancellationToken ct = default);

    // ── Tool Execution Hook ────────────────────────────────────────

    /// <summary>
    /// Register a tool handler that the swarm runtime calls when an agent invokes a tool.
    /// If no handler is registered for a tool, the runtime returns a "tool not implemented" error to the agent.
    /// </summary>
    void RegisterToolHandler(string toolName, Func<string, CancellationToken, Task<string>> handler);

    /// <summary>List all registered tool handler names.</summary>
    IReadOnlyList<string> GetRegisteredToolHandlers();
}
