// AzureOpenAISwarmAdapter — implements ISwarmPort using Azure OpenAI Chat Completions API.
//
// Architecture:
//   This adapter implements the OpenAI Swarm pattern on top of Azure OpenAI:
//     1. Each SwarmAgentDefinition → system prompt + function tools + handoff functions
//     2. Handoff = a tool call like transfer_to_{agentId}() that switches the active agent
//     3. Context variables are injected into system prompts via {{variable}} replacement
//     4. The agent loop runs: prompt → completion → tool calls → execute → repeat
//     5. Terminates when an agent returns text with no tool calls, or max depth reached
//
//   Azure OpenAI specifics:
//     - Uses ChatCompletionsClient from Azure.AI.OpenAI
//     - Deployment name = SwarmAgentDefinition.Model
//     - API version configurable per swarm (default 2024-10-21)
//     - Supports parallel tool calls natively
//
// Example:
//   var adapter = new AzureOpenAISwarmAdapter(endpoint, credential, logger);
//   var swarm = SwarmPresets.SafetyReportPipeline();
//   await adapter.CreateSwarmAsync(swarm);
//   var task = new SwarmTask { SwarmId = swarm.SwarmId, Input = "SOS at 38.9, -77.0" };
//   var result = await adapter.RunTaskAsync(task);
//
// WAL: Tool handlers registered via RegisterToolHandler() are stored in-memory.
//      For distributed execution, tool handlers should be replaced with HTTP callbacks
//      or message queue dispatches. This is the local-execution path.

using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure;
using Azure.AI.OpenAI;
using OpenAI.Chat;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Enums;
using Microsoft.Extensions.Logging;

namespace TheWatch.Adapters.Azure;

public class AzureOpenAISwarmAdapter : ISwarmPort
{
    private readonly AzureOpenAIClient _client;
    private readonly ILogger<AzureOpenAISwarmAdapter> _logger;

    // In-memory stores (swap for persistent storage in production)
    private readonly ConcurrentDictionary<string, SwarmDefinition> _swarms = new();
    private readonly ConcurrentDictionary<string, SwarmTask> _tasks = new();
    private readonly ConcurrentDictionary<string, Func<string, CancellationToken, Task<string>>> _toolHandlers = new();

    public AzureOpenAISwarmAdapter(string endpoint, string apiKey, ILogger<AzureOpenAISwarmAdapter> logger)
    {
        _client = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
        _logger = logger;
    }

    public AzureOpenAISwarmAdapter(AzureOpenAIClient client, ILogger<AzureOpenAISwarmAdapter> logger)
    {
        _client = client;
        _logger = logger;
    }

    // ── Swarm Lifecycle ─────────────────────────────────────────────

    public Task<StorageResult<SwarmDefinition>> CreateSwarmAsync(SwarmDefinition definition, CancellationToken ct = default)
    {
        var errors = definition.Validate();
        if (errors.Count > 0)
            return Task.FromResult(StorageResult<SwarmDefinition>.Fail($"Validation failed: {string.Join("; ", errors)}"));

        // Inject handoff tool definitions for each agent
        foreach (var agent in definition.Agents)
        {
            foreach (var targetId in agent.HandoffTargets)
            {
                var targetAgent = definition.GetAgent(targetId);
                if (targetAgent is null) continue;

                // Only add if not already present
                if (!agent.Tools.Any(t => t.IsHandoff && t.HandoffTargetAgentId == targetId))
                {
                    agent.Tools.Add(new SwarmToolDefinition
                    {
                        Name = $"transfer_to_{targetId.Replace("-", "_")}",
                        Description = $"Hand off the current task to the {targetAgent.Name} ({targetAgent.Role}). " +
                                      $"Call this when the task should be handled by {targetAgent.Name}.",
                        ParametersJson = """{"type":"object","properties":{"reason":{"type":"string","description":"Why you are handing off to this agent"}},"required":["reason"]}""",
                        IsHandoff = true,
                        HandoffTargetAgentId = targetId
                    });
                }
            }
        }

        definition.Status = SwarmStatus.Created;
        definition.UpdatedAt = DateTime.UtcNow;
        _swarms[definition.SwarmId] = definition;

        _logger.LogInformation("Swarm '{SwarmName}' created with {AgentCount} agents", definition.Name, definition.Agents.Count);
        return Task.FromResult(StorageResult<SwarmDefinition>.Ok(definition));
    }

    public Task<StorageResult<SwarmDefinition>> UpdateSwarmAsync(SwarmDefinition definition, CancellationToken ct = default)
    {
        if (!_swarms.ContainsKey(definition.SwarmId))
            return Task.FromResult(StorageResult<SwarmDefinition>.Fail($"Swarm '{definition.SwarmId}' not found."));

        definition.UpdatedAt = DateTime.UtcNow;
        _swarms[definition.SwarmId] = definition;
        return Task.FromResult(StorageResult<SwarmDefinition>.Ok(definition));
    }

    public Task<StorageResult<bool>> DeleteSwarmAsync(string swarmId, CancellationToken ct = default)
    {
        var removed = _swarms.TryRemove(swarmId, out _);
        return Task.FromResult(removed
            ? StorageResult<bool>.Ok(true)
            : StorageResult<bool>.Fail($"Swarm '{swarmId}' not found."));
    }

    public Task<StorageResult<SwarmDefinition>> GetSwarmAsync(string swarmId, CancellationToken ct = default)
    {
        return Task.FromResult(_swarms.TryGetValue(swarmId, out var swarm)
            ? StorageResult<SwarmDefinition>.Ok(swarm)
            : StorageResult<SwarmDefinition>.Fail($"Swarm '{swarmId}' not found."));
    }

    public Task<StorageResult<List<SwarmDefinition>>> ListSwarmsAsync(CancellationToken ct = default)
    {
        return Task.FromResult(StorageResult<List<SwarmDefinition>>.Ok(_swarms.Values.ToList()));
    }

    // ── Task Execution ──────────────────────────────────────────────

    public async Task<StorageResult<SwarmTask>> RunTaskAsync(SwarmTask task, CancellationToken ct = default)
    {
        return await RunTaskStreamingAsync(task, ct: ct);
    }

    public async Task<StorageResult<SwarmTask>> RunTaskStreamingAsync(
        SwarmTask task,
        Action<SwarmMessage>? onMessage = null,
        Action<SwarmHandoffRecord>? onHandoff = null,
        Action<SwarmToolCall>? onToolCall = null,
        CancellationToken ct = default)
    {
        if (!_swarms.TryGetValue(task.SwarmId, out var swarm))
            return StorageResult<SwarmTask>.Fail($"Swarm '{task.SwarmId}' not found. Create it first.");

        _tasks[task.TaskId] = task;
        task.Status = SwarmTaskStatus.InProgress;

        var entryPoint = swarm.GetEntryPoint();
        if (entryPoint is null)
            return StorageResult<SwarmTask>.Fail("Swarm has no entry point agent.");

        var currentAgent = entryPoint;
        task.CurrentAgentId = currentAgent.AgentId;

        // Initialize conversation with user message
        task.Messages.Add(new SwarmMessage
        {
            Role = "user",
            Content = task.Input,
            Timestamp = DateTime.UtcNow
        });

        _logger.LogInformation("Task '{TaskId}' starting in swarm '{SwarmId}' at agent '{AgentId}'",
            task.TaskId, task.SwarmId, currentAgent.AgentId);

        try
        {
            while (!ct.IsCancellationRequested && task.HandoffCount <= swarm.MaxHandoffDepth)
            {
                // Build messages for current agent
                var chatMessages = BuildChatMessages(currentAgent, task);

                // Build tools for current agent
                var options = BuildChatOptions(currentAgent);

                // Call Azure OpenAI
                var chatClient = _client.GetChatClient(currentAgent.Model);
                ChatCompletion completion;

                try
                {
                    completion = await chatClient.CompleteChatAsync(chatMessages, options, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Azure OpenAI call failed for agent '{AgentId}'", currentAgent.AgentId);
                    task.Status = SwarmTaskStatus.Failed;
                    task.ErrorMessage = $"Azure OpenAI call failed: {ex.Message}";
                    task.CompletedAt = DateTime.UtcNow;
                    return StorageResult<SwarmTask>.Fail(task.ErrorMessage);
                }

                // Track token usage
                if (completion.Usage is not null)
                {
                    task.TotalTokensUsed += completion.Usage.TotalTokenCount;
                }

                // Check for tool calls
                if (completion.ToolCalls.Count > 0)
                {
                    // Record assistant message with tool calls
                    var assistantMsg = new SwarmMessage
                    {
                        Role = "assistant",
                        Content = completion.Content.FirstOrDefault()?.Text ?? "",
                        AgentId = currentAgent.AgentId,
                        ToolCalls = completion.ToolCalls.Select(tc => new SwarmToolCall
                        {
                            Id = tc.Id,
                            FunctionName = tc.FunctionName,
                            ArgumentsJson = tc.FunctionArguments.ToString()
                        }).ToList(),
                        Timestamp = DateTime.UtcNow
                    };
                    task.Messages.Add(assistantMsg);
                    onMessage?.Invoke(assistantMsg);

                    // Process each tool call
                    var handoffOccurred = false;
                    var allToolCalls = completion.ToolCalls.ToList();

                    for (var i = 0; i < allToolCalls.Count; i++)
                    {
                        var toolCall = allToolCalls[i];
                        var swarmToolCall = new SwarmToolCall
                        {
                            Id = toolCall.Id,
                            FunctionName = toolCall.FunctionName,
                            ArgumentsJson = toolCall.FunctionArguments.ToString()
                        };
                        onToolCall?.Invoke(swarmToolCall);

                        // Check if this is a handoff
                        var handoffTool = currentAgent.Tools.FirstOrDefault(t =>
                            t.IsHandoff && t.Name == toolCall.FunctionName);

                        if (handoffTool is not null && handoffTool.HandoffTargetAgentId is not null)
                        {
                            // Execute handoff
                            var targetAgent = swarm.GetAgent(handoffTool.HandoffTargetAgentId);
                            if (targetAgent is null)
                            {
                                task.Messages.Add(new SwarmMessage
                                {
                                    Role = "tool",
                                    Content = $"Error: Handoff target '{handoffTool.HandoffTargetAgentId}' not found.",
                                    ToolCallId = toolCall.Id,
                                    AgentId = currentAgent.AgentId,
                                    Timestamp = DateTime.UtcNow
                                });
                                continue;
                            }

                            // Parse reason from arguments
                            var reason = "Handoff";
                            try
                            {
                                var args = JsonNode.Parse(toolCall.FunctionArguments.ToString());
                                reason = args?["reason"]?.GetValue<string>() ?? "Handoff";
                            }
                            catch { }

                            var handoffRecord = new SwarmHandoffRecord
                            {
                                FromAgentId = currentAgent.AgentId,
                                ToAgentId = targetAgent.AgentId,
                                Reason = reason,
                                Timestamp = DateTime.UtcNow
                            };
                            task.HandoffHistory.Add(handoffRecord);
                            task.HandoffCount++;
                            onHandoff?.Invoke(handoffRecord);

                            // Add tool response confirming handoff
                            task.Messages.Add(new SwarmMessage
                            {
                                Role = "tool",
                                Content = $"Successfully transferred to {targetAgent.Name}.",
                                ToolCallId = toolCall.Id,
                                AgentId = currentAgent.AgentId,
                                Timestamp = DateTime.UtcNow
                            });

                            // Add dummy responses for any remaining tool calls in this batch
                            // (API requires every tool_call_id to have a response)
                            for (var j = i + 1; j < allToolCalls.Count; j++)
                            {
                                task.Messages.Add(new SwarmMessage
                                {
                                    Role = "tool",
                                    Content = $"Skipped: task transferred to {targetAgent.Name}.",
                                    ToolCallId = allToolCalls[j].Id,
                                    AgentId = currentAgent.AgentId,
                                    Timestamp = DateTime.UtcNow
                                });
                            }

                            _logger.LogInformation("Handoff: {From} → {To} (reason: {Reason})",
                                currentAgent.AgentId, targetAgent.AgentId, reason);

                            currentAgent = targetAgent;
                            task.CurrentAgentId = currentAgent.AgentId;
                            handoffOccurred = true;
                            break; // Restart the agent loop with the new agent
                        }
                        else
                        {
                            // Execute tool via registered handler
                            var toolResult = await ExecuteToolAsync(toolCall.FunctionName,
                                toolCall.FunctionArguments.ToString(), ct);

                            task.Messages.Add(new SwarmMessage
                            {
                                Role = "tool",
                                Content = toolResult,
                                ToolCallId = toolCall.Id,
                                AgentId = currentAgent.AgentId,
                                Timestamp = DateTime.UtcNow
                            });
                        }
                    }

                    if (handoffOccurred) continue;
                }
                else
                {
                    // No tool calls — this is the final response from the current agent
                    var finalContent = completion.Content.FirstOrDefault()?.Text ?? "";
                    var finalMsg = new SwarmMessage
                    {
                        Role = "assistant",
                        Content = finalContent,
                        AgentId = currentAgent.AgentId,
                        Timestamp = DateTime.UtcNow
                    };
                    task.Messages.Add(finalMsg);
                    onMessage?.Invoke(finalMsg);

                    task.Output = finalContent;
                    task.Status = SwarmTaskStatus.Completed;
                    task.CompletedAt = DateTime.UtcNow;

                    _logger.LogInformation("Task '{TaskId}' completed by agent '{AgentId}' after {Handoffs} handoffs",
                        task.TaskId, currentAgent.AgentId, task.HandoffCount);

                    return StorageResult<SwarmTask>.Ok(task);
                }
            }

            if (task.HandoffCount > swarm.MaxHandoffDepth)
            {
                task.Status = SwarmTaskStatus.Failed;
                task.ErrorMessage = $"Max handoff depth ({swarm.MaxHandoffDepth}) exceeded.";
                task.CompletedAt = DateTime.UtcNow;
                return StorageResult<SwarmTask>.Fail(task.ErrorMessage);
            }

            task.Status = SwarmTaskStatus.Cancelled;
            task.CompletedAt = DateTime.UtcNow;
            return StorageResult<SwarmTask>.Ok(task);
        }
        catch (OperationCanceledException)
        {
            task.Status = SwarmTaskStatus.Cancelled;
            task.CompletedAt = DateTime.UtcNow;
            return StorageResult<SwarmTask>.Ok(task);
        }
        catch (Exception ex)
        {
            task.Status = SwarmTaskStatus.Failed;
            task.ErrorMessage = ex.Message;
            task.CompletedAt = DateTime.UtcNow;
            _logger.LogError(ex, "Task '{TaskId}' failed", task.TaskId);
            return StorageResult<SwarmTask>.Fail(ex.Message);
        }
    }

    public Task<StorageResult<bool>> CancelTaskAsync(string taskId, CancellationToken ct = default)
    {
        if (_tasks.TryGetValue(taskId, out var task))
        {
            task.Status = SwarmTaskStatus.Cancelled;
            task.CompletedAt = DateTime.UtcNow;
            return Task.FromResult(StorageResult<bool>.Ok(true));
        }
        return Task.FromResult(StorageResult<bool>.Fail($"Task '{taskId}' not found."));
    }

    public Task<StorageResult<SwarmTask>> GetTaskAsync(string taskId, CancellationToken ct = default)
    {
        return Task.FromResult(_tasks.TryGetValue(taskId, out var task)
            ? StorageResult<SwarmTask>.Ok(task)
            : StorageResult<SwarmTask>.Fail($"Task '{taskId}' not found."));
    }

    public Task<StorageResult<List<SwarmTask>>> ListTasksAsync(string? swarmId = null, int limit = 50, CancellationToken ct = default)
    {
        var tasks = _tasks.Values
            .Where(t => swarmId is null || t.SwarmId == swarmId)
            .OrderByDescending(t => t.CreatedAt)
            .Take(limit)
            .ToList();
        return Task.FromResult(StorageResult<List<SwarmTask>>.Ok(tasks));
    }

    // ── Metrics ────────────────────────────────────────────────────

    public Task<StorageResult<SwarmRunSummary>> GetRunSummaryAsync(string taskId, CancellationToken ct = default)
    {
        if (!_tasks.TryGetValue(taskId, out var task))
            return Task.FromResult(StorageResult<SwarmRunSummary>.Fail($"Task '{taskId}' not found."));

        if (!_swarms.TryGetValue(task.SwarmId, out var swarm))
            return Task.FromResult(StorageResult<SwarmRunSummary>.Fail($"Swarm '{task.SwarmId}' not found."));

        var agentMessages = task.Messages
            .Where(m => m.AgentId is not null)
            .GroupBy(m => m.AgentId!);

        var summary = new SwarmRunSummary
        {
            TaskId = task.TaskId,
            SwarmId = task.SwarmId,
            FinalStatus = task.Status,
            Duration = task.Duration ?? TimeSpan.Zero,
            TotalHandoffs = task.HandoffCount,
            TotalToolCalls = task.Messages.Count(m => m.Role == "tool"),
            TotalTokensUsed = task.TotalTokensUsed,
            EstimatedCostUsd = task.EstimatedCostUsd,
            AgentsInvolved = agentMessages.Count(),
            FinalOutput = task.Output,
            ErrorMessage = task.ErrorMessage,
            AgentMetrics = agentMessages.Select(g =>
            {
                var agentDef = swarm.GetAgent(g.Key);
                return new SwarmAgentMetrics
                {
                    AgentId = g.Key,
                    AgentName = agentDef?.Name ?? g.Key,
                    Role = agentDef?.Role ?? SwarmRole.Custom,
                    TurnsHandled = g.Count(m => m.Role == "assistant"),
                    ToolCallsMade = g.SelectMany(m => m.ToolCalls ?? []).Count(),
                    HandoffsInitiated = task.HandoffHistory.Count(h => h.FromAgentId == g.Key)
                };
            }).ToList()
        };

        return Task.FromResult(StorageResult<SwarmRunSummary>.Ok(summary));
    }

    // ── Tool Handlers ──────────────────────────────────────────────

    public void RegisterToolHandler(string toolName, Func<string, CancellationToken, Task<string>> handler)
    {
        _toolHandlers[toolName] = handler;
        _logger.LogDebug("Tool handler registered: {ToolName}", toolName);
    }

    public IReadOnlyList<string> GetRegisteredToolHandlers() => _toolHandlers.Keys.ToList().AsReadOnly();

    // ── Private Helpers ────────────────────────────────────────────

    private List<ChatMessage> BuildChatMessages(SwarmAgentDefinition agent, SwarmTask task)
    {
        var messages = new List<ChatMessage>();

        // System prompt with context variable injection
        var systemPrompt = agent.Instructions;
        foreach (var (key, value) in task.ContextVariables)
        {
            systemPrompt = systemPrompt.Replace($"{{{{{key}}}}}", value);
        }
        messages.Add(ChatMessage.CreateSystemMessage(systemPrompt));

        // Replay conversation history
        foreach (var msg in task.Messages)
        {
            switch (msg.Role)
            {
                case "user":
                    messages.Add(ChatMessage.CreateUserMessage(msg.Content));
                    break;
                case "assistant":
                    if (msg.ToolCalls is { Count: > 0 })
                    {
                        var toolCalls = msg.ToolCalls.Select(tc =>
                            ChatToolCall.CreateFunctionToolCall(tc.Id, tc.FunctionName,
                                BinaryData.FromString(tc.ArgumentsJson))).ToList();
                        messages.Add(ChatMessage.CreateAssistantMessage(toolCalls));
                    }
                    else
                    {
                        messages.Add(ChatMessage.CreateAssistantMessage(msg.Content));
                    }
                    break;
                case "tool":
                    messages.Add(ChatMessage.CreateToolMessage(msg.ToolCallId!, msg.Content));
                    break;
            }
        }

        return messages;
    }

    private ChatCompletionOptions BuildChatOptions(SwarmAgentDefinition agent)
    {
        var options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = agent.MaxTokens,
            Temperature = agent.Temperature,
        };

        foreach (var tool in agent.Tools)
        {
            options.Tools.Add(ChatTool.CreateFunctionTool(
                tool.Name,
                tool.Description,
                BinaryData.FromString(tool.ParametersJson)));
        }

        // Only set parallel_tool_calls when tools are present (API rejects it otherwise)
        if (options.Tools.Count > 0)
        {
            options.AllowParallelToolCalls = agent.ParallelToolCalls;
        }

        return options;
    }

    private async Task<string> ExecuteToolAsync(string toolName, string argumentsJson, CancellationToken ct)
    {
        if (_toolHandlers.TryGetValue(toolName, out var handler))
        {
            try
            {
                return await handler(argumentsJson, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Tool handler '{ToolName}' threw an exception", toolName);
                return JsonSerializer.Serialize(new { error = ex.Message });
            }
        }

        // Default: return a mock acknowledgment so the agent can continue
        _logger.LogDebug("No handler for tool '{ToolName}', returning mock acknowledgment", toolName);
        return JsonSerializer.Serialize(new
        {
            status = "acknowledged",
            tool = toolName,
            note = "Tool executed successfully (no custom handler registered — using default acknowledgment).",
            arguments = JsonSerializer.Deserialize<JsonElement>(argumentsJson)
        });
    }
}
