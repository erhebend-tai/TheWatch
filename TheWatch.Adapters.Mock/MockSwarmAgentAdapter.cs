// =============================================================================
// MockSwarmAgentAdapter — in-memory mock for ISwarmAgentPort
// =============================================================================
// Provides canned conversational responses for development/testing without
// requiring an Azure OpenAI endpoint. Guides users through the same flow
// as the real agent but with deterministic responses.
//
// WAL: Uses [WAL-SWARMAGENT-MOCK] log prefix for all trace output.
// =============================================================================

using Microsoft.Extensions.Logging;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Adapters.Mock;

public class MockSwarmAgentAdapter : ISwarmAgentPort
{
    private readonly ILogger<MockSwarmAgentAdapter> _logger;

    public MockSwarmAgentAdapter(ILogger<MockSwarmAgentAdapter> logger)
    {
        _logger = logger;
    }

    public string ProviderName => "Mock";

    public Task<string> GetGreetingAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("[WAL-SWARMAGENT-MOCK] GetGreetingAsync");
        return Task.FromResult(
            "TheWatch Swarm Agent ready (mock mode — no Azure OpenAI configured).\n" +
            "I can help you explore swarm commands. What would you like to do?\n" +
            "Type 'help' for available commands, or describe your goal.");
    }

    public Task<SwarmAgentResponse> SendMessageAsync(
        string userMessage,
        IReadOnlyList<SwarmAgentMessage> conversationHistory,
        CancellationToken ct = default)
    {
        _logger.LogDebug("[WAL-SWARMAGENT-MOCK] SendMessageAsync: {Message}", userMessage);

        var lower = userMessage.Trim().ToLowerInvariant();

        // Handle common queries with canned responses
        var response = lower switch
        {
            "help" or "?" or "commands" =>
                new SwarmAgentResponse(
                    "Available swarm commands:\n\n" +
                    "  swarm list              — List all registered swarms\n" +
                    "  swarm presets           — Show preset templates\n" +
                    "  swarm create <preset>   — Create from a preset\n" +
                    "  swarm show <id>         — Show swarm topology\n" +
                    "  swarm run <id> --input  — Execute a task\n" +
                    "  swarm validate <id>     — Check topology\n" +
                    "  plan \"description\"      — AI-assisted swarm design\n\n" +
                    "What would you like to do?"),

            _ when lower.Contains("sos") || lower.Contains("emergency") || lower.Contains("safety") =>
                new SwarmAgentResponse(
                    "For safety/emergency scenarios, the `safety-report-pipeline` preset is your best bet.\n" +
                    "It chains: triage → threat assessment → evidence collection → geospatial → review → aggregation.\n\n" +
                    "▶ swarm create safety-report-pipeline\n\n" +
                    "Then run it with:\n" +
                    "▶ swarm run safety-report-pipeline --input \"your scenario\" --stream",
                    SuggestedCommand: "swarm create safety-report-pipeline"),

            _ when lower.Contains("neighborhood") || lower.Contains("watch") || lower.Contains("community") =>
                new SwarmAgentResponse(
                    "The `neighborhood-watch` preset coordinates community safety monitoring.\n\n" +
                    "▶ swarm create neighborhood-watch\n\n" +
                    "This sets up volunteer coordination, patrol routing, and incident reporting agents.",
                    SuggestedCommand: "swarm create neighborhood-watch"),

            _ when lower.Contains("compliance") || lower.Contains("audit") || lower.Contains("iso") =>
                new SwarmAgentResponse(
                    "Use the `compliance-audit` preset for ISO/IEC checking pipelines.\n\n" +
                    "▶ swarm create compliance-audit\n\n" +
                    "It includes document scanning, gap analysis, and remediation planning agents.",
                    SuggestedCommand: "swarm create compliance-audit"),

            _ when lower.Contains("evidence") || lower.Contains("chain of custody") =>
                new SwarmAgentResponse(
                    "The `evidence-chain` preset handles evidence collection and chain-of-custody.\n\n" +
                    "▶ swarm create evidence-chain",
                    SuggestedCommand: "swarm create evidence-chain"),

            _ when lower.Contains("list") || lower.Contains("show me") =>
                new SwarmAgentResponse(
                    "To see your registered swarms:\n\n▶ swarm list\n\n" +
                    "Or to see all available templates:\n\n▶ swarm presets",
                    SuggestedCommand: "swarm list"),

            _ when lower.Contains("plan") || lower.Contains("design") || lower.Contains("custom") =>
                new SwarmAgentResponse(
                    "To design a custom swarm with AI assistance, use the plan command:\n\n" +
                    "▶ plan \"your task description\" --backend azure --create\n\n" +
                    "This analyzes your goal, recommends the optimal agent topology, and can auto-create the swarm.\n" +
                    "Use `--backend claude` to use Claude Code instead of Azure OpenAI for planning."),

            _ when lower.Contains("bye") || lower.Contains("exit") || lower.Contains("quit") || lower.Contains("done") =>
                new SwarmAgentResponse("Got it. Run any swarm command when you're ready.", EndConversation: true),

            _ =>
                new SwarmAgentResponse(
                    "(Mock mode — limited responses. Connect Azure OpenAI for full agent capabilities.)\n\n" +
                    "I can help with:\n" +
                    "  • Creating swarms from presets (safety, neighborhood, compliance, evidence, dispatch)\n" +
                    "  • Running tasks through existing swarms\n" +
                    "  • Planning custom swarm topologies with AI\n\n" +
                    "Try describing your goal, or type 'help' for command reference.")
        };

        return Task.FromResult(response);
    }
}
