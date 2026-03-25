// SwarmPresetTests — validates all 4 preset swarm topologies run end-to-end
// against live Azure OpenAI endpoints.
//
// Each test:
//   1. Creates a swarm from a preset
//   2. Feeds it a realistic TheWatch scenario
//   3. Verifies the task completes with handoffs and a final output
//   4. Checks the run summary metrics
//
// Example:
//   dotnet test --filter "ClassName~SwarmPresetTests"

using Azure;
using Azure.AI.OpenAI;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Enums;
using Xunit;
using Xunit.Abstractions;

namespace TheWatch.Adapters.Azure.Tests;

[Trait("Category", "Integration")]
public class SwarmPresetTests
{
    private readonly ITestOutputHelper _output;
    private readonly TestConfiguration _config;
    private readonly AzureOpenAISwarmAdapter _adapter;

    public SwarmPresetTests(ITestOutputHelper output)
    {
        _output = output;
        _config = TestConfiguration.Load();

        var loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));
        var logger = loggerFactory.CreateLogger<AzureOpenAISwarmAdapter>();

        _adapter = new AzureOpenAISwarmAdapter(_config.Endpoint, _config.ApiKey, logger);
    }

    [Fact]
    public async Task SafetyReportPipeline_ShouldTriageAndDispatch()
    {
        // Arrange
        var swarm = SwarmPresets.SafetyReportPipeline();

        // Use gpt-4o-mini for all agents to keep costs low during testing
        foreach (var agent in swarm.Agents)
            agent.Model = _config.DeploymentGpt4oMini;

        var createResult = await _adapter.CreateSwarmAsync(swarm);
        createResult.Success.Should().BeTrue(createResult.ErrorMessage ?? "");

        var task = new SwarmTask
        {
            SwarmId = swarm.SwarmId,
            Input = "URGENT: User triggered SOS at coordinates 35.1495, -90.0490 (Memphis, TN). " +
                    "Phrase detected: 'somebody help me'. Device accelerometer shows sudden stop after rapid movement. " +
                    "User profile: female, age 28, registered volunteer responders in area: 7. " +
                    "Time: 11:45 PM local."
        };

        // Act — track handoffs and tool calls
        var handoffs = new List<SwarmHandoffRecord>();
        var toolCalls = new List<SwarmToolCall>();

        var result = await _adapter.RunTaskStreamingAsync(task,
            onHandoff: h =>
            {
                handoffs.Add(h);
                _output.WriteLine($"  HANDOFF: {h.FromAgentId} → {h.ToAgentId} ({h.Reason})");
            },
            onToolCall: tc =>
            {
                toolCalls.Add(tc);
                _output.WriteLine($"  TOOL: {tc.FunctionName}({tc.ArgumentsJson[..Math.Min(tc.ArgumentsJson.Length, 100)]})");
            });

        // Assert
        result.Success.Should().BeTrue(result.ErrorMessage ?? "Swarm task failed");
        result.Data!.Status.Should().Be(SwarmTaskStatus.Completed);
        result.Data.Output.Should().NotBeNullOrWhiteSpace();
        result.Data.HandoffCount.Should().BeGreaterThan(0, "safety pipeline requires multiple agent handoffs");
        toolCalls.Should().NotBeEmpty("agents should invoke tools like classify_report, assess_threat, etc.");

        _output.WriteLine($"\n  RESULT: {result.Data.Output![..Math.Min(result.Data.Output.Length, 500)]}");
        _output.WriteLine($"  Handoffs: {result.Data.HandoffCount}, Tool calls: {toolCalls.Count}, Tokens: {result.Data.TotalTokensUsed}");

        // Verify run summary
        var summary = await _adapter.GetRunSummaryAsync(task.TaskId);
        summary.Success.Should().BeTrue();
        summary.Data!.AgentsInvolved.Should().BeGreaterThan(1);

        _output.WriteLine($"  Agents involved: {summary.Data.AgentsInvolved}");
        foreach (var m in summary.Data.AgentMetrics)
            _output.WriteLine($"    {m.AgentName} ({m.Role}): {m.TurnsHandled} turns, {m.ToolCallsMade} tool calls");
    }

    [Fact]
    public async Task EmergencyDispatchSwarm_ShouldCompleteRapidResponse()
    {
        // Arrange
        var swarm = SwarmPresets.EmergencyDispatchSwarm();
        foreach (var agent in swarm.Agents)
            agent.Model = _config.DeploymentGpt4oMini;

        var createResult = await _adapter.CreateSwarmAsync(swarm);
        createResult.Success.Should().BeTrue(createResult.ErrorMessage ?? "");

        var task = new SwarmTask
        {
            SwarmId = swarm.SwarmId,
            Input = "IoT glass break sensor triggered at 123 Oak Street, Memphis TN 38104. " +
                    "Motion sensor activated in foyer. Time: 2:15 AM. " +
                    "Homeowner registered as elderly male, 72. Medical flag: mobility impaired. " +
                    "Trusted contacts: 3. Nearby volunteers: 5 within 2km."
        };

        var handoffs = new List<SwarmHandoffRecord>();

        // Act
        var result = await _adapter.RunTaskStreamingAsync(task,
            onHandoff: h =>
            {
                handoffs.Add(h);
                _output.WriteLine($"  HANDOFF: {h.FromAgentId} → {h.ToAgentId}");
            });

        // Assert
        result.Success.Should().BeTrue(result.ErrorMessage ?? "");
        result.Data!.Status.Should().Be(SwarmTaskStatus.Completed);
        result.Data.Output.Should().NotBeNullOrWhiteSpace();
        handoffs.Should().NotBeEmpty("dispatch swarm requires agent handoffs");

        _output.WriteLine($"\n  RESULT: {result.Data.Output![..Math.Min(result.Data.Output.Length, 500)]}");
        _output.WriteLine($"  Handoffs: {handoffs.Count}, Tokens: {result.Data.TotalTokensUsed}");
    }

    [Fact]
    public async Task CodeReviewSwarm_ShouldProduceConsolidatedReview()
    {
        // Arrange
        var swarm = SwarmPresets.CodeReviewSwarm();
        foreach (var agent in swarm.Agents)
            agent.Model = _config.DeploymentGpt4oMini;

        var createResult = await _adapter.CreateSwarmAsync(swarm);
        createResult.Success.Should().BeTrue(createResult.ErrorMessage ?? "");

        var task = new SwarmTask
        {
            SwarmId = swarm.SwarmId,
            Input = """
                Review this code diff for TheWatch.Dashboard.Api:

                ```csharp
                [HttpPost("dispatch")]
                public async Task<ActionResult> DispatchTask([FromBody] SwarmAgentTask task, CancellationToken ct)
                {
                    // No input validation
                    await _swarm.DispatchAgentTaskAsync(task, ct);
                    return Accepted(new { task.TaskId, Status = "Queued" });
                }
                ```

                Check for: security issues, architecture compliance, and code style.
                """
        };

        // Act
        var result = await _adapter.RunTaskStreamingAsync(task,
            onHandoff: h => _output.WriteLine($"  HANDOFF: {h.FromAgentId} → {h.ToAgentId}"));

        // Assert
        result.Success.Should().BeTrue(result.ErrorMessage ?? "");
        result.Data!.Status.Should().Be(SwarmTaskStatus.Completed);
        result.Data.Output.Should().NotBeNullOrWhiteSpace();

        _output.WriteLine($"\n  REVIEW: {result.Data.Output![..Math.Min(result.Data.Output.Length, 800)]}");
        _output.WriteLine($"  Handoffs: {result.Data.HandoffCount}, Tokens: {result.Data.TotalTokensUsed}");
    }

    [Fact]
    public async Task StandardsAuditSwarm_ShouldIdentifyGaps()
    {
        // Arrange
        var swarm = SwarmPresets.StandardsAuditSwarm();
        foreach (var agent in swarm.Agents)
            agent.Model = _config.DeploymentGpt4oMini;

        var createResult = await _adapter.CreateSwarmAsync(swarm);
        createResult.Success.Should().BeTrue(createResult.ErrorMessage ?? "");

        var task = new SwarmTask
        {
            SwarmId = swarm.SwarmId,
            Input = "Audit TheWatch against ISO 27001 Annex A controls. " +
                    "System features: encrypted storage (AES-256), role-based access control, " +
                    "audit logging via IAuditTrail port, evidence chain of custody, " +
                    "real-time monitoring via SignalR, geofenced location tracking. " +
                    "Missing: no formal incident response plan document, no regular penetration testing schedule, " +
                    "no data retention policy enforcement."
        };

        // Act
        var result = await _adapter.RunTaskStreamingAsync(task,
            onHandoff: h => _output.WriteLine($"  HANDOFF: {h.FromAgentId} → {h.ToAgentId}"));

        // Assert
        result.Success.Should().BeTrue(result.ErrorMessage ?? "");
        result.Data!.Status.Should().Be(SwarmTaskStatus.Completed);
        result.Data.Output.Should().NotBeNullOrWhiteSpace();
        result.Data.Output.Should().ContainAny("gap", "remediation", "missing", "recommendation",
            "Gap", "Remediation", "Missing", "Recommendation");

        _output.WriteLine($"\n  AUDIT: {result.Data.Output![..Math.Min(result.Data.Output.Length, 800)]}");
        _output.WriteLine($"  Handoffs: {result.Data.HandoffCount}, Tokens: {result.Data.TotalTokensUsed}");
    }

    [Fact]
    public async Task AllPresets_ShouldValidateSuccessfully()
    {
        // Verify all preset topologies pass validation without hitting Azure
        var presets = SwarmPresets.ListPresets();
        presets.Should().HaveCount(4);

        foreach (var (id, name, description, agentCount) in presets)
        {
            var swarm = SwarmPresets.GetPreset(id);
            swarm.Should().NotBeNull($"preset '{id}' should exist");

            var errors = swarm!.Validate();
            errors.Should().BeEmpty($"preset '{name}' should validate cleanly, but got: {string.Join("; ", errors)}");

            swarm.Agents.Should().HaveCount(agentCount, $"preset '{name}' should have {agentCount} agents");
            swarm.GetEntryPoint().Should().NotBeNull($"preset '{name}' should have an entry point");

            _output.WriteLine($"  {name}: {agentCount} agents, entry={swarm.EntryPointAgentId}");
        }
    }
}
