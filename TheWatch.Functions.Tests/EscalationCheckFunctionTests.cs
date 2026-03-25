// EscalationCheckFunctionTests - xUnit tests for EscalationCheckFunction.
// Tests the RabbitMQ-triggered RunCheck(string message) method.
// The TimerTrigger RunSweep(TimerInfo) is tested via reflection to construct TimerInfo.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TheWatch.Adapters.Mock;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Enums;
using TheWatch.Functions.Functions;
using System.Text.Json;
using Xunit;

namespace TheWatch.Functions.Tests;

public class EscalationCheckFunctionTests
{
    private readonly EscalationCheckFunction _sut;
    private readonly ILogger<EscalationCheckFunction> _logger;

    public EscalationCheckFunctionTests()
    {
        _logger = NullLoggerFactory.Instance.CreateLogger<EscalationCheckFunction>();

        var lf = NullLoggerFactory.Instance;
        _sut = new EscalationCheckFunction(
            new MockResponseRequestAdapter(lf.CreateLogger<MockResponseRequestAdapter>()),
            new MockResponseTrackingAdapter(lf.CreateLogger<MockResponseTrackingAdapter>()),
            new MockResponseDispatchAdapter(lf.CreateLogger<MockResponseDispatchAdapter>()),
            new MockEscalationAdapter(lf.CreateLogger<MockEscalationAdapter>()),
            new MockSpatialIndex(),
            new MockAuditTrail(),
            new MockEmergencyServicesPort(),
            Options.Create(new EscalationConfiguration()),
            _logger);
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
        var timerInfo = new Microsoft.Azure.Functions.Worker.TimerInfo();
        await _sut.RunSweep(timerInfo);
    }

    [Fact]
    public async Task RunCheck_ValidMessage_ProcessesWithoutError()
    {
        var json = Serialize(MakeMessage(EscalationPolicy.TimedEscalation));
        await _sut.RunCheck(json);
    }

    [Fact]
    public async Task RunCheck_Conditional911_ProcessesWithoutError()
    {
        var json = Serialize(MakeMessage(EscalationPolicy.Conditional911));
        await _sut.RunCheck(json);
    }

    [Fact]
    public async Task RunCheck_Immediate911_ProcessesWithoutError()
    {
        var json = Serialize(MakeMessage(EscalationPolicy.Immediate911));
        await _sut.RunCheck(json);
    }

    [Fact]
    public async Task RunCheck_FullCascade_ProcessesWithoutError()
    {
        var json = Serialize(MakeMessage(EscalationPolicy.FullCascade));
        await _sut.RunCheck(json);
    }

    [Fact]
    public async Task RunCheck_ManualPolicy_ProcessesWithoutError()
    {
        var json = Serialize(MakeMessage(EscalationPolicy.Manual));
        await _sut.RunCheck(json);
    }

    [Fact]
    public async Task RunCheck_InvalidJson_ThrowsException()
    {
        var json = "THIS IS NOT JSON";
        await Assert.ThrowsAsync<JsonException>(() => _sut.RunCheck(json));
    }

    [Fact]
    public async Task RunCheck_NullDeserialization_ReturnsEarly()
    {
        var json = "null";
        await _sut.RunCheck(json);
    }

    [Fact]
    public async Task RunCheck_AllPolicies_ProcessWithoutError()
    {
        foreach (EscalationPolicy policy in Enum.GetValues<EscalationPolicy>())
        {
            var json = Serialize(MakeMessage(policy));
            await _sut.RunCheck(json);
        }
    }
}
