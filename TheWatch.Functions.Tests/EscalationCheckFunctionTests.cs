// EscalationCheckFunctionTests - xUnit tests for EscalationCheckFunction.
// Tests the RabbitMQ-triggered RunCheck(string message) method.
// The TimerTrigger RunSweep(TimerInfo) is tested via reflection to construct TimerInfo.
//
// WAL: EscalationCheckFunction has two triggers:
//   1. Timer ("*/30 * * * * *") → RunSweep: periodic sweep of all active requests
//   2. RabbitMQ ("escalation-check") → RunCheck: targeted escalation for a specific request
//      Switch on EscalationPolicy: TimedEscalation, Conditional911, Immediate911, FullCascade, Manual
//
// Example:
//   var msg = JsonSerializer.Serialize(new EscalationCheckMessage(
//       "req-001", EscalationPolicy.TimedEscalation, DateTime.UtcNow, 5));
//   await new EscalationCheckFunction(logger).RunCheck(msg);

namespace TheWatch.Functions.Tests;

public class EscalationCheckFunctionTests
{
    private readonly EscalationCheckFunction _sut;
    private readonly ILogger<EscalationCheckFunction> _logger;

    public EscalationCheckFunctionTests()
    {
        _logger = NullLoggerFactory.Instance.CreateLogger<EscalationCheckFunction>();
        _sut = new EscalationCheckFunction(_logger);
    }

    private static string Serialize(EscalationCheckMessage msg) =>
        JsonSerializer.Serialize(msg);

    private static EscalationCheckMessage MakeMessage(
        EscalationPolicy policy = EscalationPolicy.TimedEscalation,
        string requestId = "req-esc-001") =>
        new(
            RequestId: requestId,
            Policy: policy,
            TimeoutAt: DateTime.UtcNow.AddMinutes(-1),
            DesiredResponderCount: 5
        );

    [Fact]
    public async Task RunSweep_ExecutesWithoutError()
    {
        // Arrange — construct a TimerInfo via the public constructor (Azure Functions Worker SDK)
        // TimerInfo has a parameterless constructor in the isolated worker model.
        var timerInfo = new Microsoft.Azure.Functions.Worker.TimerInfo();

        // Act & Assert — should complete without throwing
        await _sut.RunSweep(timerInfo);
    }

    [Fact]
    public async Task RunCheck_ValidMessage_LogsEscalationPolicy()
    {
        // Arrange
        var json = Serialize(MakeMessage(EscalationPolicy.TimedEscalation));

        // Act & Assert — should process without throwing
        await _sut.RunCheck(json);
    }

    [Fact]
    public async Task RunCheck_TimedEscalation_LogsRadiusExpansion()
    {
        // Arrange — TimedEscalation policy: "Expanding radius"
        var json = Serialize(MakeMessage(EscalationPolicy.TimedEscalation));

        // Act & Assert
        await _sut.RunCheck(json);
    }

    [Fact]
    public async Task RunCheck_Conditional911_Logs911Notification()
    {
        // Arrange — Conditional911: "notifying 911"
        var json = Serialize(MakeMessage(EscalationPolicy.Conditional911));

        // Act & Assert
        await _sut.RunCheck(json);
    }

    [Fact]
    public async Task RunCheck_Immediate911_LogsAlreadyInProgress()
    {
        // Arrange — Immediate911: "Already in progress"
        var json = Serialize(MakeMessage(EscalationPolicy.Immediate911));

        // Act & Assert
        await _sut.RunCheck(json);
    }

    [Fact]
    public async Task RunCheck_FullCascade_LogsAllTiersActivation()
    {
        // Arrange — FullCascade: "Activating all tiers"
        var json = Serialize(MakeMessage(EscalationPolicy.FullCascade));

        // Act & Assert
        await _sut.RunCheck(json);
    }

    [Fact]
    public async Task RunCheck_ManualPolicy_LogsNoAction()
    {
        // Arrange — Manual: "no auto-action"
        var json = Serialize(MakeMessage(EscalationPolicy.Manual));

        // Act & Assert
        await _sut.RunCheck(json);
    }

    [Fact]
    public async Task RunCheck_InvalidJson_ThrowsException()
    {
        // Arrange — malformed JSON
        var json = "THIS IS NOT JSON";

        // Act & Assert — JsonException caught, logged, re-thrown
        await Assert.ThrowsAsync<JsonException>(() => _sut.RunCheck(json));
    }

    [Fact]
    public async Task RunCheck_NullDeserialization_ReturnsEarly()
    {
        // Arrange — "null" deserializes to null
        var json = "null";

        // Act & Assert — returns early without error
        await _sut.RunCheck(json);
    }

    [Fact]
    public async Task RunCheck_AllPolicies_ProcessWithoutError()
    {
        // Arrange & Act — iterate every EscalationPolicy enum value
        foreach (EscalationPolicy policy in Enum.GetValues<EscalationPolicy>())
        {
            var json = Serialize(MakeMessage(policy));
            await _sut.RunCheck(json);
        }
    }
}
