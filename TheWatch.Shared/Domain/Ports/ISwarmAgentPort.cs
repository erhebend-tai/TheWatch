// =============================================================================
// ISwarmAgentPort — domain port for interactive conversational swarm agent.
// =============================================================================
// NO AI SDK imports allowed in this file.
//
// Provides a chat-style interface for an AI agent that helps users formulate
// swarm requests interactively. The CLI owns the I/O loop (reading stdin,
// writing stdout); this port handles only the AI conversation.
//
// Architecture:
//   CLI default swarm handler → ISwarmAgentPort.SendMessageAsync(...)
//     → Adapter (AzureOpenAISwarmAgentAdapter) → Azure OpenAI Chat Completions
//       → Conversational response guiding the user to a swarm action
//
// The agent knows about TheWatch's swarm capabilities, preset templates,
// and can suggest specific CLI commands to accomplish the user's goal.
//
// Example:
//   var greeting = await agentPort.GetGreetingAsync(ct);
//   Console.WriteLine(greeting);
//   while (true)
//   {
//       var input = Console.ReadLine();
//       var response = await agentPort.SendMessageAsync(input, history, ct);
//       Console.WriteLine(response.Content);
//       if (response.SuggestedCommand is not null)
//           Console.WriteLine($"Suggested: {response.SuggestedCommand}");
//   }
// =============================================================================

namespace TheWatch.Shared.Domain.Ports;

/// <summary>
/// A single message in the agent conversation history.
/// </summary>
public record SwarmAgentMessage(string Role, string Content);

/// <summary>
/// The agent's response to a user message, optionally including a suggested
/// CLI command and a flag indicating the conversation has concluded.
/// </summary>
public record SwarmAgentResponse(
    string Content,
    string? SuggestedCommand = null,
    bool EndConversation = false);

/// <summary>
/// Port for an interactive conversational agent that guides users through
/// swarm creation, configuration, and execution via natural language.
/// </summary>
public interface ISwarmAgentPort
{
    /// <summary>Adapter identifier (e.g., "AzureOpenAI", "Mock").</summary>
    string ProviderName { get; }

    /// <summary>
    /// Get the agent's opening greeting and instructions.
    /// Called once at the start of an interactive session.
    /// </summary>
    Task<string> GetGreetingAsync(CancellationToken ct = default);

    /// <summary>
    /// Send a user message and receive the agent's response.
    /// The conversation history includes all prior messages (user + assistant)
    /// so the adapter can maintain context across turns.
    /// </summary>
    Task<SwarmAgentResponse> SendMessageAsync(
        string userMessage,
        IReadOnlyList<SwarmAgentMessage> conversationHistory,
        CancellationToken ct = default);
}
