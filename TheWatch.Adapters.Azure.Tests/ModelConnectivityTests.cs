// ModelConnectivityTests — verifies all 4 Azure OpenAI model deployments respond.
//
// Tests:
//   1. GPT-4.1 completions (Supervisor/Orchestrator agents)
//   2. GPT-4o completions (General-purpose agents)
//   3. GPT-4o-mini completions (Specialist file agents)
//   4. text-embedding-3-large vector generation (RAG agents)
//
// Example:
//   dotnet test --filter "ClassName~ModelConnectivityTests"

using Azure;
using Azure.AI.OpenAI;
using FluentAssertions;
using OpenAI.Chat;
using OpenAI.Embeddings;
using Xunit;
using Xunit.Abstractions;

namespace TheWatch.Adapters.Azure.Tests;

[Trait("Category", "Integration")]
public class ModelConnectivityTests
{
    private readonly ITestOutputHelper _output;
    private readonly TestConfiguration _config;
    private readonly AzureOpenAIClient _client;

    public ModelConnectivityTests(ITestOutputHelper output)
    {
        _output = output;
        _config = TestConfiguration.Load();
        _client = new AzureOpenAIClient(new Uri(_config.Endpoint), new AzureKeyCredential(_config.ApiKey));
    }

    [Fact]
    public async Task Gpt41_ShouldRespondToSafetyClassificationPrompt()
    {
        // Arrange — GPT-4.1 is the model for Supervisor and Orchestrator agents
        var chatClient = _client.GetChatClient(_config.DeploymentGpt41);

        var messages = new ChatMessage[]
        {
            ChatMessage.CreateSystemMessage(
                "You are a safety system triage agent for TheWatch emergency response platform. " +
                "Classify incoming reports by severity (1-5) and category."),
            ChatMessage.CreateUserMessage(
                "SOS triggered at coordinates 38.9072, -77.0369. User phrase detected: 'help me now'. " +
                "Device sensors show rapid movement followed by sudden stop.")
        };

        // Act
        var response = await chatClient.CompleteChatAsync(messages, new ChatCompletionOptions
        {
            MaxOutputTokenCount = 500,
            Temperature = 0.3f
        });

        // Assert
        response.Value.Should().NotBeNull();
        response.Value.Content.Should().NotBeEmpty();
        var text = response.Value.Content[0].Text;
        text.Should().NotBeNullOrWhiteSpace();
        response.Value.Usage.TotalTokenCount.Should().BeGreaterThan(0);

        _output.WriteLine($"[GPT-4.1] Tokens: {response.Value.Usage.TotalTokenCount}");
        _output.WriteLine($"[GPT-4.1] Response: {text[..Math.Min(text.Length, 500)]}");
    }

    [Fact]
    public async Task Gpt4o_ShouldRespondToThreatAssessmentPrompt()
    {
        // Arrange — GPT-4o is for general-purpose agents
        var chatClient = _client.GetChatClient(_config.DeploymentGpt4o);

        var messages = new ChatMessage[]
        {
            ChatMessage.CreateSystemMessage(
                "You are a threat assessment agent. Evaluate emergency reports and determine " +
                "threat level (1-5), whether first responders are needed, and recommended response."),
            ChatMessage.CreateUserMessage(
                "Report: Glass break sensor triggered at 2:30 AM. Motion detected in living room. " +
                "Homeowner's panic phrase detected via always-on microphone. " +
                "Location: residential neighborhood, Memphis TN.")
        };

        // Act
        var response = await chatClient.CompleteChatAsync(messages, new ChatCompletionOptions
        {
            MaxOutputTokenCount = 500,
            Temperature = 0.2f
        });

        // Assert
        response.Value.Should().NotBeNull();
        var text = response.Value.Content[0].Text;
        text.Should().NotBeNullOrWhiteSpace();
        response.Value.Usage.TotalTokenCount.Should().BeGreaterThan(0);

        _output.WriteLine($"[GPT-4o] Tokens: {response.Value.Usage.TotalTokenCount}");
        _output.WriteLine($"[GPT-4o] Response: {text[..Math.Min(text.Length, 500)]}");
    }

    [Fact]
    public async Task Gpt4oMini_ShouldRespondToFileAgentPrompt()
    {
        // Arrange — GPT-4o-mini is for high-volume Specialist file agents
        var chatClient = _client.GetChatClient(_config.DeploymentGpt4oMini);

        var messages = new ChatMessage[]
        {
            ChatMessage.CreateSystemMessage(
                "You are a file specialist agent responsible for SwarmCoordinationService.cs. " +
                "Your goals: maintain documentation, track code quality, report issues."),
            ChatMessage.CreateUserMessage(
                "Analyze this file's purpose in 2 words, then describe it in one sentence: " +
                "SwarmCoordinationService.cs orchestrates RabbitMQ message dispatch, " +
                "Hangfire recurring jobs, and SignalR broadcast for the AI agent swarm.")
        };

        // Act
        var response = await chatClient.CompleteChatAsync(messages, new ChatCompletionOptions
        {
            MaxOutputTokenCount = 200,
            Temperature = 0.5f
        });

        // Assert
        response.Value.Should().NotBeNull();
        var text = response.Value.Content[0].Text;
        text.Should().NotBeNullOrWhiteSpace();
        response.Value.Usage.TotalTokenCount.Should().BeGreaterThan(0);

        _output.WriteLine($"[GPT-4o-mini] Tokens: {response.Value.Usage.TotalTokenCount}");
        _output.WriteLine($"[GPT-4o-mini] Response: {text}");
    }

    [Fact]
    public async Task Embedding_ShouldGenerateVectorsForSafetyContext()
    {
        // Arrange — text-embedding-3-large is for RAG/context retrieval agents
        var embeddingClient = _client.GetEmbeddingClient(_config.DeploymentEmbedding);

        var inputs = new[]
        {
            "Emergency SOS triggered at downtown Memphis location with phrase detection",
            "Volunteer responder check-in from user at 38.1, -89.9 status OK",
            "Glass break sensor IoT alert residential zone 3 nighttime",
            "Standards audit ISO 27001 control A.12.4 logging and monitoring gap identified"
        };

        // Act
        var response = await embeddingClient.GenerateEmbeddingsAsync(inputs);

        // Assert
        response.Value.Should().NotBeNull();
        response.Value.Should().HaveCount(4);

        foreach (var embedding in response.Value)
        {
            var vec = embedding.ToFloats();
            vec.Length.Should().Be(3072, "text-embedding-3-large produces 3072-dimensional vectors");
            vec.ToArray().Sum(x => x * x).Should().BeGreaterThan(0, "vector should not be zero");
        }

        _output.WriteLine($"[Embedding] Generated {response.Value.Count} vectors, each {response.Value[0].ToFloats().Length} dimensions");
        _output.WriteLine($"[Embedding] Usage: {response.Value.Usage.TotalTokenCount} tokens");

        // Verify vectors are distinct (different inputs → different vectors)
        var v0 = response.Value[0].ToFloats().ToArray();
        var v1 = response.Value[1].ToFloats().ToArray();
        var cosine = v0.Zip(v1, (a, b) => a * b).Sum();
        cosine.Should().BeLessThan(0.95f, "different safety contexts should produce distinct embeddings");
        _output.WriteLine($"[Embedding] Cosine similarity (SOS vs check-in): {cosine:F4}");
    }

    [Fact]
    public async Task Gpt4oMini_ShouldHandleToolCallsCorrectly()
    {
        // Arrange — verify tool calling works (critical for swarm handoffs)
        var chatClient = _client.GetChatClient(_config.DeploymentGpt4oMini);

        var messages = new ChatMessage[]
        {
            ChatMessage.CreateSystemMessage(
                "You are a triage agent. When you receive an emergency, " +
                "ALWAYS call the classify_report tool with the category and severity."),
            ChatMessage.CreateUserMessage("SOS emergency: person reports being followed on foot, downtown area.")
        };

        var options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = 300,
            Temperature = 0.1f,
        };
        options.Tools.Add(ChatTool.CreateFunctionTool(
            "classify_report",
            "Classify an incoming safety report",
            BinaryData.FromString("""
            {
                "type": "object",
                "properties": {
                    "category": { "type": "string", "enum": ["SOS_EMERGENCY","CHECK_IN_REQUEST","EVIDENCE_SUBMISSION","LOCATION_ALERT","DEVICE_ALERT"] },
                    "severity": { "type": "integer", "minimum": 1, "maximum": 5 },
                    "summary": { "type": "string" }
                },
                "required": ["category", "severity", "summary"]
            }
            """)));

        // Act
        var response = await chatClient.CompleteChatAsync(messages, options);

        // Assert
        response.Value.Should().NotBeNull();
        response.Value.ToolCalls.Should().NotBeEmpty("triage agent should call classify_report tool");

        var toolCall = response.Value.ToolCalls[0];
        toolCall.FunctionName.Should().Be("classify_report");
        toolCall.FunctionArguments.Should().NotBeNull();

        var argsText = toolCall.FunctionArguments.ToString();
        argsText.Should().Contain("SOS_EMERGENCY");

        _output.WriteLine($"[Tool Call] Function: {toolCall.FunctionName}");
        _output.WriteLine($"[Tool Call] Args: {argsText}");
        _output.WriteLine($"[Tool Call] Tokens: {response.Value.Usage.TotalTokenCount}");
    }
}
