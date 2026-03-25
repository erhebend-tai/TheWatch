// SimulationServiceTests — tests for the in-memory simulation event pipeline.
//
// SimulationService uses System.Threading.Channels for pub/sub and maintains
// an in-memory event log with a 500-event cap. Tests verify:
//   - Event publishing writes to the log
//   - Event log respects the 500-event cap
//   - GetEventLogAsync respects the limit parameter
//   - SubscribeToEventsAsync receives published events
//   - Initial mock data is seeded on construction
//
// Example — running simulation tests:
//   dotnet test --filter "FullyQualifiedName~SimulationServiceTests"

namespace TheWatch.Dashboard.Api.Tests;

public class SimulationServiceTests
{
    private static SimulationService CreateService()
        => new(NullLogger<SimulationService>.Instance);

    // ────────────────────────────────────────────────────────────
    // Event Log
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// On construction, SimulationService seeds 4 mock events.
    /// GetEventLogAsync should return them.
    /// </summary>
    [Fact]
    public async Task GetEventLogAsync_ReturnsSeedData()
    {
        var svc = CreateService();

        var events = await svc.GetEventLogAsync();

        Assert.NotEmpty(events);
        Assert.Equal(4, events.Count);
    }

    /// <summary>
    /// GetEventLogAsync with a limit should cap the result count.
    /// </summary>
    [Fact]
    public async Task GetEventLogAsync_RespectsLimit()
    {
        var svc = CreateService();

        var events = await svc.GetEventLogAsync(limit: 2);

        Assert.Equal(2, events.Count);
    }

    /// <summary>
    /// PublishEventAsync should add the event to the front of the log.
    /// </summary>
    [Fact]
    public async Task PublishEventAsync_AddsToEventLog()
    {
        var svc = CreateService();

        var newEvent = new SimulationEvent
        {
            EventType = SimulationEventType.SOSTrigger,
            Payload = "{\"severity\": \"high\"}",
            Source = "Test-Device",
            Timestamp = DateTime.UtcNow,
            Latitude = 30.27,
            Longitude = -97.74
        };

        await svc.PublishEventAsync(newEvent);

        var events = await svc.GetEventLogAsync();
        Assert.Equal(5, events.Count); // 4 seed + 1 new
        Assert.Equal("Test-Device", events[0].Source); // New event is first (inserted at 0)
    }

    /// <summary>
    /// Event log should never exceed 500 entries. When the 501st event is added,
    /// the oldest event should be removed.
    /// </summary>
    [Fact]
    public async Task PublishEventAsync_CapsAt500Events()
    {
        var svc = CreateService();

        // Publish 500 events (4 seed + 500 = 504 inserts, capped at 500)
        for (int i = 0; i < 500; i++)
        {
            await svc.PublishEventAsync(new SimulationEvent
            {
                EventType = SimulationEventType.SensorReading,
                Payload = $"{{\"index\": {i}}}",
                Source = $"Test-{i}",
                Timestamp = DateTime.UtcNow
            });
        }

        var events = await svc.GetEventLogAsync(limit: 600);
        Assert.Equal(500, events.Count);
    }

    // ────────────────────────────────────────────────────────────
    // Pub/Sub via Channel
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// SubscribeToEventsAsync should yield events that are published after subscription.
    /// </summary>
    [Fact]
    public async Task SubscribeToEventsAsync_ReceivesPublishedEvents()
    {
        var svc = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var receivedEvents = new List<SimulationEvent>();
        var subscriberStarted = new TaskCompletionSource();

        // Start subscriber in background
        var subscriberTask = Task.Run(async () =>
        {
            subscriberStarted.SetResult();
            await foreach (var evt in svc.SubscribeToEventsAsync(cts.Token))
            {
                receivedEvents.Add(evt);
                if (receivedEvents.Count >= 2) break;
            }
        }, cts.Token);

        // Wait for subscriber to start before publishing
        await subscriberStarted.Task;
        await Task.Delay(50); // Small delay to ensure ReadAllAsync is listening

        // Publish two events
        await svc.PublishEventAsync(new SimulationEvent
        {
            EventType = SimulationEventType.PhraseDetection,
            Payload = "{\"phrase\": \"help me\"}",
            Source = "Sub-Test-1",
            Timestamp = DateTime.UtcNow
        });

        await svc.PublishEventAsync(new SimulationEvent
        {
            EventType = SimulationEventType.AlertEscalation,
            Payload = "{\"level\": 2}",
            Source = "Sub-Test-2",
            Timestamp = DateTime.UtcNow
        });

        // Wait for subscriber to receive both events (or timeout)
        await Task.WhenAny(subscriberTask, Task.Delay(3000));

        Assert.Equal(2, receivedEvents.Count);
        Assert.Equal("Sub-Test-1", receivedEvents[0].Source);
        Assert.Equal("Sub-Test-2", receivedEvents[1].Source);
    }

    // ────────────────────────────────────────────────────────────
    // Seed data validation
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// Seed data should contain all expected event types.
    /// </summary>
    [Fact]
    public async Task SeedData_ContainsExpectedEventTypes()
    {
        var svc = CreateService();

        var events = await svc.GetEventLogAsync();

        Assert.Contains(events, e => e.EventType == SimulationEventType.SensorReading);
        Assert.Contains(events, e => e.EventType == SimulationEventType.DeviceStateChange);
        Assert.Contains(events, e => e.EventType == SimulationEventType.PhraseDetection);
        Assert.Contains(events, e => e.EventType == SimulationEventType.SOSTrigger);
    }

    /// <summary>
    /// Seed data should include geographic coordinates.
    /// </summary>
    [Fact]
    public async Task SeedData_HasGeographicCoordinates()
    {
        var svc = CreateService();

        var events = await svc.GetEventLogAsync();

        Assert.All(events, e =>
        {
            Assert.NotNull(e.Latitude);
            Assert.NotNull(e.Longitude);
            Assert.InRange(e.Latitude!.Value, -90, 90);
            Assert.InRange(e.Longitude!.Value, -180, 180);
        });
    }
}
