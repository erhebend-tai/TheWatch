// SurveyDispatchFunctionTests - xUnit tests for SurveyDispatchFunction.
// Tests the Timer-triggered Run(TimerInfo) method.
//
// WAL: SurveyDispatchFunction fires every 60 seconds and:
//   1. Dispatches post-incident surveys for resolved incidents
//   2. Dispatches registration surveys for new users
//   3. Dispatches scheduled surveys that are due
//   All three sub-methods are currently stubs that log and return Task.CompletedTask.
//   Errors are caught and logged (not re-thrown) to prevent timer function failure.
//
// Example:
//   var fn = new SurveyDispatchFunction(logger);
//   await fn.Run(new TimerInfo()); // should log debug messages and complete

namespace TheWatch.Functions.Tests;

public class SurveyDispatchFunctionTests
{
    private readonly SurveyDispatchFunction _sut;
    private readonly ILogger<SurveyDispatchFunction> _logger;

    public SurveyDispatchFunctionTests()
    {
        _logger = NullLoggerFactory.Instance.CreateLogger<SurveyDispatchFunction>();
        _sut = new SurveyDispatchFunction(_logger);
    }

    [Fact]
    public async Task Run_TimerFires_ExecutesWithoutError()
    {
        // Arrange — construct TimerInfo (isolated worker model has parameterless ctor)
        var timerInfo = new Microsoft.Azure.Functions.Worker.TimerInfo();

        // Act & Assert — should complete without throwing
        await _sut.Run(timerInfo);
    }

    [Fact]
    public async Task Run_NullScheduleStatus_DoesNotThrow()
    {
        // Arrange — TimerInfo with null ScheduleStatus (default)
        var timerInfo = new Microsoft.Azure.Functions.Worker.TimerInfo();
        // ScheduleStatus is null by default, which the function handles gracefully

        // Act & Assert
        await _sut.Run(timerInfo);
    }

    [Fact]
    public async Task Run_MultipleInvocations_AllSucceed()
    {
        // Arrange — simulate multiple timer firings (idempotent)
        var timerInfo = new Microsoft.Azure.Functions.Worker.TimerInfo();

        // Act & Assert — three consecutive invocations should all succeed
        await _sut.Run(timerInfo);
        await _sut.Run(timerInfo);
        await _sut.Run(timerInfo);
    }
}
