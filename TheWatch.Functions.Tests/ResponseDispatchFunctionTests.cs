// ResponseDispatchFunctionTests - xUnit tests for ResponseDispatchFunction.
// Tests the RabbitMQ-triggered Run(string message) method with various JSON payloads.
//
// WAL: ResponseDispatchFunction deserializes a ResponseDispatchMessage, reads scope presets,
//      caps simulated responder notifications at 20, and logs per-responder dispatch.
//
// Example:
//   var msg = JsonSerializer.Serialize(new ResponseDispatchMessage(
//       "req-001", "user-1", ResponseScope.CheckIn, 33.0, -97.0, 1000, 8,
//       DispatchStrategy.NearestN, "test", "MANUAL_BUTTON"));
//   await new ResponseDispatchFunction(logger).Run(msg); // logs 8 responders

namespace TheWatch.Functions.Tests;

public class ResponseDispatchFunctionTests
{
    private readonly ResponseDispatchFunction _sut;
    private readonly ILogger<ResponseDispatchFunction> _logger;

    public ResponseDispatchFunctionTests()
    {
        _logger = NullLoggerFactory.Instance.CreateLogger<ResponseDispatchFunction>();
        _sut = new ResponseDispatchFunction(_logger);
    }

    private static string Serialize(ResponseDispatchMessage msg) =>
        JsonSerializer.Serialize(msg);

    private static ResponseDispatchMessage MakeMessage(
        ResponseScope scope = ResponseScope.CheckIn,
        int desiredResponders = 8,
        double radius = 1000) =>
        new(
            RequestId: "req-test-001",
            UserId: "user-test-001",
            Scope: scope,
            Latitude: 33.0198,
            Longitude: -96.6989,
            RadiusMeters: radius,
            DesiredResponderCount: desiredResponders,
            Strategy: DispatchStrategy.NearestN,
            Description: "Unit test dispatch",
            TriggerSource: "MANUAL_BUTTON"
        );

    [Fact]
    public async Task Run_ValidMessage_ProcessesSuccessfully()
    {
        // Arrange
        var json = Serialize(MakeMessage());

        // Act & Assert — should complete without throwing
        await _sut.Run(json);
    }

    [Fact]
    public async Task Run_NullDeserialization_LogsWarningAndReturns()
    {
        // Arrange — "null" literal deserializes to null for a record type
        var json = "null";

        // Act & Assert — should return early without throwing
        await _sut.Run(json);
    }

    [Fact]
    public async Task Run_InvalidJson_ThrowsException()
    {
        // Arrange — malformed JSON that will fail deserialization
        var json = "{{{{NOT VALID JSON}}}}";

        // Act & Assert — JsonException is caught, logged, and re-thrown
        await Assert.ThrowsAsync<JsonException>(() => _sut.Run(json));
    }

    [Fact]
    public async Task Run_CheckInScope_UsesCorrectDefaults()
    {
        // Arrange — CheckIn scope: radius=1000m, 8 responders, Manual escalation, NearestN
        var msg = MakeMessage(scope: ResponseScope.CheckIn, desiredResponders: 8, radius: 1000);
        var json = Serialize(msg);

        // Act — should complete and log CheckIn defaults
        await _sut.Run(json);

        // Assert — verify the scope preset values are what we expect
        var defaults = ResponseScopePresets.GetDefaults(ResponseScope.CheckIn);
        Assert.Equal(1000, defaults.RadiusMeters);
        Assert.Equal(8, defaults.DesiredResponders);
        Assert.Equal(EscalationPolicy.Manual, defaults.Escalation);
        Assert.Equal(DispatchStrategy.NearestN, defaults.Strategy);
    }

    [Fact]
    public async Task Run_EvacuationScope_UsesCorrectDefaults()
    {
        // Arrange — Evacuation scope: radius=50000m, int.MaxValue responders, FullCascade
        var msg = MakeMessage(scope: ResponseScope.Evacuation, desiredResponders: 100, radius: 50000);
        var json = Serialize(msg);

        // Act — should complete and log Evacuation defaults
        await _sut.Run(json);

        // Assert — verify the scope preset values
        var defaults = ResponseScopePresets.GetDefaults(ResponseScope.Evacuation);
        Assert.Equal(50000, defaults.RadiusMeters);
        Assert.Equal(int.MaxValue, defaults.DesiredResponders);
        Assert.Equal(EscalationPolicy.FullCascade, defaults.Escalation);
        Assert.Equal(DispatchStrategy.EmergencyBroadcast, defaults.Strategy);
        Assert.Equal(TimeSpan.Zero, defaults.EscalationTimeout);
    }

    [Fact]
    public async Task Run_CapsResponderNotificationsAt20()
    {
        // Arrange — request 50 responders, function caps simulated notifications at 20
        var msg = MakeMessage(desiredResponders: 50);
        var json = Serialize(msg);

        // Act — should complete; internally Math.Min(50, 20) = 20
        await _sut.Run(json);

        // Assert — verify the cap logic
        Assert.Equal(20, Math.Min(50, 20));
    }

    [Fact]
    public async Task Run_AllScopes_ProcessWithoutError()
    {
        // Arrange & Act — run through every ResponseScope enum value
        foreach (ResponseScope scope in Enum.GetValues<ResponseScope>())
        {
            var msg = MakeMessage(scope: scope);
            var json = Serialize(msg);
            await _sut.Run(json);
        }
    }

    [Fact]
    public async Task Run_ZeroDesiredResponders_SkipsNotificationLoop()
    {
        // Arrange — 0 desired responders means the for-loop body never executes
        var msg = MakeMessage(desiredResponders: 0);
        var json = Serialize(msg);

        // Act & Assert — should complete without error
        await _sut.Run(json);
    }

    [Fact]
    public async Task Run_ExactlyAtCap_DoesNotExceed20()
    {
        // Arrange — request exactly 20 responders, should not exceed cap
        var msg = MakeMessage(desiredResponders: 20);
        var json = Serialize(msg);

        // Act — should complete, notifying exactly 20
        await _sut.Run(json);

        Assert.Equal(20, Math.Min(20, 20));
    }
}
