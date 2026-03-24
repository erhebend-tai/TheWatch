using System.Threading.Channels;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Enums;

namespace TheWatch.Dashboard.Api.Services;

/// <summary>
/// Simulation service managing MAUI to Dashboard relay using in-memory channels and SignalR.
/// </summary>
public class SimulationService : ISimulationService
{
    private readonly ILogger<SimulationService> _logger;
    private readonly Channel<SimulationEvent> _eventChannel;
    private readonly List<SimulationEvent> _eventLog = new();

    public SimulationService(ILogger<SimulationService> logger)
    {
        _logger = logger;
        _eventChannel = Channel.CreateUnbounded<SimulationEvent>();
        InitializeMockEventLog();
    }

    public async Task PublishEventAsync(SimulationEvent simulationEvent)
    {
        _eventLog.Insert(0, simulationEvent);
        if (_eventLog.Count > 500) _eventLog.RemoveAt(_eventLog.Count - 1);
        await _eventChannel.Writer.WriteAsync(simulationEvent);
        _logger.LogInformation("Published simulation event: {EventType} from {Source}", simulationEvent.EventType, simulationEvent.Source);
    }

    public Task<List<SimulationEvent>> GetEventLogAsync(int limit = 100) =>
        Task.FromResult(_eventLog.Take(limit).ToList());

    public async IAsyncEnumerable<SimulationEvent> SubscribeToEventsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var simulationEvent in _eventChannel.Reader.ReadAllAsync(cancellationToken))
            yield return simulationEvent;
    }

    private void InitializeMockEventLog()
    {
        var now = DateTime.Now;
        _eventLog.AddRange(new[]
        {
            new SimulationEvent { EventType = SimulationEventType.SensorReading, Payload = "{\"heartRate\": 78, \"spO2\": 98}", Source = "MAUI-Device-001", Timestamp = now.AddMinutes(-5), Latitude = 37.7749, Longitude = -122.4194 },
            new SimulationEvent { EventType = SimulationEventType.DeviceStateChange, Payload = "{\"status\": \"online\", \"battery\": 87}", Source = "MAUI-Device-001", Timestamp = now.AddMinutes(-10), Latitude = 37.7749, Longitude = -122.4194 },
            new SimulationEvent { EventType = SimulationEventType.PhraseDetection, Payload = "{\"phrase\": \"help\", \"confidence\": 0.94}", Source = "MAUI-Device-002", Timestamp = now.AddMinutes(-15), Latitude = 34.0522, Longitude = -118.2437 },
            new SimulationEvent { EventType = SimulationEventType.SOSTrigger, Payload = "{\"severity\": \"critical\", \"reason\": \"fall_detection\"}", Source = "MAUI-Device-003", Timestamp = now.AddMinutes(-30), Latitude = 41.8781, Longitude = -87.6298 },
        });
    }
}
