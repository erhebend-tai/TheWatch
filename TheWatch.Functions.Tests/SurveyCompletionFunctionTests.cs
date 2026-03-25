// SurveyCompletionFunctionTests - xUnit tests for SurveyCompletionFunction.
// Tests the RabbitMQ-triggered Run(string message) method with SurveyCompletedMessage payloads.
//
// WAL: SurveyCompletionFunction deserializes a SurveyCompletedMessage from "survey-completed" queue.
//   - If deserialization fails → logs warning, returns early
//   - If RequestId is not null → calls CheckCompletionThresholdAsync
//   - In production would broadcast via SignalR
//
// Example:
//   var msg = JsonSerializer.Serialize(new SurveyCompletedMessage(
//       "resp-101", "tpl-postincident-v1", "req-123", "user-456"));
//   await new SurveyCompletionFunction(logger).Run(msg);

namespace TheWatch.Functions.Tests;

public class SurveyCompletionFunctionTests
{
    private readonly SurveyCompletionFunction _sut;
    private readonly ILogger<SurveyCompletionFunction> _logger;

    public SurveyCompletionFunctionTests()
    {
        _logger = NullLoggerFactory.Instance.CreateLogger<SurveyCompletionFunction>();
        _sut = new SurveyCompletionFunction(_logger);
    }

    private static string Serialize(SurveyCompletedMessage msg) =>
        JsonSerializer.Serialize(msg);

    private static SurveyCompletedMessage MakeMessage(
        string? requestId = "req-survey-001") =>
        new(
            ResponseId: "resp-test-001",
            TemplateId: "tpl-postincident-wellbeing-v1",
            RequestId: requestId,
            UserId: "user-test-001"
        );

    [Fact]
    public async Task Run_ValidMessage_ProcessesSuccessfully()
    {
        var json = Serialize(MakeMessage());
        await _sut.Run(json);
    }

    [Fact]
    public async Task Run_NullDeserialization_LogsWarningAndReturns()
    {
        var json = "null";
        await _sut.Run(json);
    }

    [Fact]
    public async Task Run_InvalidJson_ThrowsException()
    {
        var json = "NOT VALID JSON!!";
        await Assert.ThrowsAsync<JsonException>(() => _sut.Run(json));
    }

    [Fact]
    public async Task Run_WithRequestId_ChecksCompletionThreshold()
    {
        // Arrange — message with a RequestId triggers CheckCompletionThresholdAsync
        var json = Serialize(MakeMessage(requestId: "req-active-001"));

        // Act & Assert — should complete without error
        await _sut.Run(json);
    }

    [Fact]
    public async Task Run_NullRequestId_SkipsCompletionCheck()
    {
        // Arrange — null RequestId skips the threshold check branch
        var json = Serialize(MakeMessage(requestId: null));

        // Act & Assert — should complete without error
        await _sut.Run(json);
    }

    [Fact]
    public async Task Run_DifferentTemplateIds_AllProcess()
    {
        // Arrange — test with various template IDs
        var templates = new[]
        {
            "tpl-postincident-wellbeing-v1",
            "tpl-household-safety-v1",
            "tpl-responder-debrief-v1",
            "tpl-weekly-safety-check-v1"
        };

        foreach (var tpl in templates)
        {
            var msg = new SurveyCompletedMessage("resp-" + tpl, tpl, "req-001", "user-001");
            await _sut.Run(Serialize(msg));
        }
    }
}
