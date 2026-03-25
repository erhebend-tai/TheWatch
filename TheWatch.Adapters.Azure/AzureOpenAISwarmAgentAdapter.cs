// =============================================================================
// AzureOpenAISwarmAgentAdapter — implements ISwarmAgentPort using Azure OpenAI
// =============================================================================
// Provides an interactive conversational agent that helps users navigate
// TheWatch's swarm system. Uses Azure OpenAI Chat Completions with a system
// prompt that encodes knowledge of all swarm commands, presets, and patterns.
//
// RAG Integration:
//   When an IContextRetrievalPort is provided, each user message triggers a
//   vector search across all configured stores. Retrieved context chunks are
//   injected as a system message between the base system prompt and the
//   conversation history, giving the LLM domain-specific grounding.
//
// The agent guides users through:
//   - Choosing or designing a swarm topology
//   - Selecting the right preset template
//   - Formulating task inputs
//   - Understanding swarm output and metrics
//
// Example:
//   var adapter = new AzureOpenAISwarmAgentAdapter(endpoint, key, logger, contextPort: ragPort);
//   var greeting = await adapter.GetGreetingAsync();
//   var response = await adapter.SendMessageAsync("I need to handle SOS alerts", history);
//
// WAL: The system prompt is intentionally comprehensive — it includes all CLI
//      commands and presets so the agent can suggest exact commands to run.
//      RAG context is injected as a separate system message so the LLM can
//      distinguish between static knowledge and retrieved context.
//      Conversation history is passed in full on each call for stateless adapter design.
// =============================================================================

using System.Text;
using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Logging;
using OpenAI.Chat;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Adapters.Azure;

public class AzureOpenAISwarmAgentAdapter : ISwarmAgentPort
{
    private readonly AzureOpenAIClient _client;
    private readonly ILogger<AzureOpenAISwarmAgentAdapter> _logger;
    private readonly string _deploymentName;
    private readonly IContextRetrievalPort? _contextRetrieval;

    // RAG configuration
    private const int RagMaxTokens = 4000;
    private const int RagTopK = 8;
    private const float RagMinScore = 0.3f;
    private static readonly string[] RagNamespaces = ["swarms", "architecture", "codebase", "features", "infrastructure", "standards"];

    // WAL: System prompt encodes full knowledge of TheWatch swarm CLI so the agent
    //      can suggest exact commands. This is a conversational agent, not a planner —
    //      it helps users decide what to do, not generate swarm JSON.
    private const string SystemPrompt = """
        You are the TheWatch Swarm Agent — an interactive assistant built into the TheWatch CLI.
        Your job is to help users work with TheWatch's multi-agent swarm system.

        WHAT YOU KNOW:
        TheWatch is a life-safety emergency response application that coordinates volunteer
        responders, 911 escalation, evidence collection, guard reports, CCTV analysis,
        geospatial lookups, and compliance audits. It uses Azure OpenAI multi-agent swarms
        (based on the OpenAI Swarm pattern) to orchestrate complex tasks across specialized agents.

        AVAILABLE CLI COMMANDS:
        - `swarm list`                         — List all registered swarms
        - `swarm presets`                      — Show available preset templates
        - `swarm create <preset-id>`           — Create a swarm from a preset
        - `swarm create --name "..." --agents` — Create a custom swarm
        - `swarm show <swarm-id>`              — Show swarm topology and config
        - `swarm delete <swarm-id>`            — Delete a swarm
        - `swarm run <id> --input "..."`       — Run a task through a swarm
        - `swarm run <id> --input "..." --stream` — Run with real-time streaming
        - `swarm tasks [swarm-id]`             — List recent task executions
        - `swarm task <task-id>`               — Show detailed task trace
        - `swarm validate <swarm-id>`          — Validate swarm topology
        - `swarm add-agent <id> --agent-id ... --name ... --role ... --instructions ...`
        - `swarm remove-agent <swarm-id> <agent-id>`
        - `plan "task description"`            — AI-assisted swarm planning (Claude or Azure)
        - `plan "..." --backend azure --create` — Plan and auto-create the swarm

        AVAILABLE PRESETS:
        - safety-report-pipeline: Multi-agent pipeline for safety incident reports (triage → threat assessment → evidence → location → review → aggregation)
        - neighborhood-watch: Community safety monitoring with volunteer coordination
        - compliance-audit: ISO/IEC compliance checking pipeline
        - evidence-chain: Evidence collection and chain-of-custody management
        - emergency-dispatch: 911-integrated emergency response coordination

        AGENT ROLES:
        Triage, Specialist, Reviewer, Supervisor, Orchestrator, Aggregator, Custom

        HOW TO HELP:
        1. Ask what the user wants to accomplish
        2. Recommend the best approach (preset vs custom, which commands to use)
        3. Suggest specific CLI commands they can copy and run
        4. Explain swarm concepts if asked (handoffs, agent topology, tool calls)
        5. Help troubleshoot issues with existing swarms
        6. Use the RETRIEVED CONTEXT (if present) to give accurate, grounded answers about TheWatch

        STYLE:
        - Be concise and action-oriented
        - When suggesting a command, format it on its own line prefixed with "▶ "
        - If you can determine the exact command, include it in your response
        - Ask clarifying questions when the user's goal is ambiguous
        - Don't over-explain — the user is a developer who knows their system
        - When referencing retrieved context, cite the source briefly

        When the conversation naturally concludes (user has their answer/command), end with
        a brief closing line. Don't force the conversation to continue.
        """;

    public string ProviderName => _contextRetrieval is not null ? "AzureOpenAI+RAG" : "AzureOpenAI";

    public AzureOpenAISwarmAgentAdapter(
        string endpoint, string apiKey, ILogger<AzureOpenAISwarmAgentAdapter> logger,
        string deploymentName = "gpt-4o",
        IContextRetrievalPort? contextRetrieval = null)
    {
        _client = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
        _logger = logger;
        _deploymentName = deploymentName;
        _contextRetrieval = contextRetrieval;
    }

    public AzureOpenAISwarmAgentAdapter(
        AzureOpenAIClient client, ILogger<AzureOpenAISwarmAgentAdapter> logger,
        string deploymentName = "gpt-4o",
        IContextRetrievalPort? contextRetrieval = null)
    {
        _client = client;
        _logger = logger;
        _deploymentName = deploymentName;
        _contextRetrieval = contextRetrieval;
    }

    public Task<string> GetGreetingAsync(CancellationToken ct = default)
    {
        var ragStatus = _contextRetrieval is not null ? " (RAG-enabled)" : "";
        return Task.FromResult(
            $"TheWatch Swarm Agent ready{ragStatus}. What would you like to do?\n" +
            "I can help you create, run, or manage multi-agent swarms.\n" +
            "Type your request, or 'help' for available commands.");
    }

    public async Task<SwarmAgentResponse> SendMessageAsync(
        string userMessage,
        IReadOnlyList<SwarmAgentMessage> conversationHistory,
        CancellationToken ct = default)
    {
        _logger.LogDebug("[WAL-SWARMAGENT-AOAI] SendMessageAsync: {MessageLength} chars, {HistoryCount} prior messages, RAG={RagEnabled}",
            userMessage.Length, conversationHistory.Count, _contextRetrieval is not null);

        try
        {
            var chatClient = _client.GetChatClient(_deploymentName);

            // Build message list: system + RAG context + history + current user message
            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(SystemPrompt)
            };

            // RAG: retrieve relevant context for the user's query
            if (_contextRetrieval is not null)
            {
                var ragContext = await RetrieveContextAsync(userMessage, ct);
                if (!string.IsNullOrEmpty(ragContext))
                {
                    messages.Add(new SystemChatMessage(
                        "RETRIEVED CONTEXT (from TheWatch knowledge base — use this to ground your answers):\n\n" +
                        ragContext +
                        "\n\nEnd of retrieved context. Use it to inform your response but don't repeat it verbatim."));
                }
            }

            foreach (var msg in conversationHistory)
            {
                messages.Add(msg.Role switch
                {
                    "user" => new UserChatMessage(msg.Content),
                    "assistant" => new AssistantChatMessage(msg.Content),
                    _ => new UserChatMessage(msg.Content)
                });
            }

            messages.Add(new UserChatMessage(userMessage));

            var options = new ChatCompletionOptions
            {
                Temperature = 0.6f,
                MaxOutputTokenCount = 2048
            };

            var completion = await chatClient.CompleteChatAsync(messages, options, ct);
            var responseText = completion.Value.Content[0].Text;

            // Extract suggested command if present (lines starting with "▶ ")
            string? suggestedCommand = null;
            foreach (var line in responseText.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("▶ ") || trimmed.StartsWith("> "))
                {
                    suggestedCommand = trimmed[2..].Trim();
                    break;
                }
            }

            // Detect natural end of conversation
            var endPhrases = new[] { "good luck", "let me know", "happy to help", "you're all set", "that should" };
            var isEnding = endPhrases.Any(p =>
                responseText.Contains(p, StringComparison.OrdinalIgnoreCase));

            _logger.LogDebug("[WAL-SWARMAGENT-AOAI] Response: {Length} chars, suggestedCmd={Cmd}, ending={End}",
                responseText.Length, suggestedCommand ?? "(none)", isEnding);

            return new SwarmAgentResponse(responseText, suggestedCommand, isEnding);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WAL-SWARMAGENT-AOAI] Chat completion failed");
            return new SwarmAgentResponse(
                $"Error communicating with Azure OpenAI: {ex.Message}\n" +
                "Check your --aoai-endpoint and --aoai-key configuration.",
                EndConversation: true);
        }
    }

    /// <summary>
    /// Retrieve context from the RAG pipeline and format it for injection into the prompt.
    /// Returns null if no relevant context is found or retrieval fails.
    /// </summary>
    private async Task<string?> RetrieveContextAsync(string query, CancellationToken ct)
    {
        try
        {
            var result = await _contextRetrieval!.RetrieveContextAsync(
                query, RagNamespaces, RagMaxTokens, RagTopK, RagMinScore, ct);

            if (!result.Success || result.Data is null || result.Data.Count == 0)
            {
                _logger.LogDebug("[WAL-SWARMAGENT-AOAI] RAG returned no results for query");
                return null;
            }

            var sb = new StringBuilder();
            foreach (var chunk in result.Data)
            {
                sb.AppendLine($"[Source: {chunk.Source} | Score: {chunk.RelevanceScore:F2} | Namespace: {chunk.Namespace}]");
                sb.AppendLine(chunk.Content);
                sb.AppendLine();
            }

            _logger.LogDebug("[WAL-SWARMAGENT-AOAI] RAG injected {Count} chunks, ~{Tokens} tokens",
                result.Data.Count, result.Data.Sum(c => c.EstimatedTokens));

            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[WAL-SWARMAGENT-AOAI] RAG retrieval failed, continuing without context");
            return null;
        }
    }
}
