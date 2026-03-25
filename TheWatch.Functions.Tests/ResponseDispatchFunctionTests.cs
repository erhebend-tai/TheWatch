// ResponseDispatchFunctionTests — xUnit tests for ResponseDispatchFunction.
// Tests the RabbitMQ-triggered Run(string message) method with various JSON payloads.
//
// WAL: ResponseDispatchFunction deserializes a ResponseDispatchMessage, queries
//      ISpatialIndex for nearby responders, and sends batch push notifications
//      via INotificationSendPort.SendPushBatchAsync().
//
// The function now takes ISpatialIndex + INotificationSendPort + ILogger<T>.
// Tests use in-memory mock implementations from TheWatch.Adapters.Mock.
//
// Example:
//   var msg = JsonSerializer.Serialize(new ResponseDispatchMessage(
//       "req-001", "user-1", ResponseScope.CheckIn, 33.0, -97.0, 1000, 8,
//       DispatchStrategy.NearestN, "test", "MANUAL_BUTTON", DateTime.UtcNow));
//   await sut.Run(msg); // queries spatial index, sends notifications, logs results

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Functions.Functions;
using System.Collections.Concurrent;

namespace TheWatch.Functions.Tests;

public class ResponseDispatchFunctionTests
{
    private readonly ResponseDispatchFunction _sut;
    private readonly ILogger<ResponseDispatchFunction> _logger;
    private readonly TestSpatialIndex _spatialIndex;
    private readonly TestNotificationSendPort _notificationPort;

    public ResponseDispatchFunctionTests()
    {
        _logger = NullLoggerFactory.Instance.CreateLogger<ResponseDispatchFunction>();
        _spatialIndex = new TestSpatialIndex();
        _notificationPort = new TestNotificationSendPort();
        _sut = new ResponseDispatchFunction(_spatialIndex, _notificationPort, _logger);
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
            TriggerSource: "MANUAL_BUTTON",
            CreatedAt: DateTime.UtcNow
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
    public async Task Run_WithNearbyResponders_SendsBatchNotifications()
    {
        // Arrange — seed spatial index with some responders
        _spatialIndex.SeedResponders(5, 33.0198, -96.6989);
        var msg = MakeMessage(desiredResponders: 5);
        var json = Serialize(msg);

        // Act
        await _sut.Run(json);

        // Assert — batch send should have been called with 5 payloads
        Assert.Equal(5, _notificationPort.LastBatchSize);
    }

    [Fact]
    public async Task Run_NoNearbyResponders_SkipsNotifications()
    {
        // Arrange — empty spatial index (default)
        var msg = MakeMessage(desiredResponders: 8);
        var json = Serialize(msg);

        // Act
        await _sut.Run(json);

        // Assert — no batch send should have been called
        Assert.Equal(0, _notificationPort.LastBatchSize);
    }

    [Fact]
    public async Task Run_AllScopes_ProcessWithoutError()
    {
        // Arrange — seed some responders
        _spatialIndex.SeedResponders(3, 33.0198, -96.6989);

        // Act — run through every ResponseScope enum value
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
        // Arrange — 0 desired responders means empty payload list
        _spatialIndex.SeedResponders(10, 33.0198, -96.6989);
        var msg = MakeMessage(desiredResponders: 0);
        var json = Serialize(msg);

        // Act & Assert — should complete without error
        await _sut.Run(json);

        // No notifications sent since Take(0) yields empty
        Assert.Equal(0, _notificationPort.LastBatchSize);
    }

    // ═══════════════════════════════════════════════════════════════
    // Test Doubles
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Minimal ISpatialIndex test double that returns configurable nearby results.
    /// </summary>
    private class TestSpatialIndex : ISpatialIndex
    {
        private readonly List<SpatialResult> _seeded = new();

        public void SeedResponders(int count, double centerLat, double centerLng)
        {
            _seeded.Clear();
            for (var i = 0; i < count; i++)
            {
                _seeded.Add(new SpatialResult
                {
                    EntityId = $"resp-{i:D3}",
                    EntityType = "Volunteer",
                    Latitude = centerLat + (i * 0.001),
                    Longitude = centerLng + (i * 0.001),
                    DistanceMeters = 200 + (i * 150),
                    RingLevel = 1
                });
            }
        }

        public Task<List<SpatialResult>> FindNearbyAsync(SpatialQuery query, CancellationToken ct = default)
            => Task.FromResult(_seeded.Take(query.MaxResults).ToList());

        public Task IndexAsync(string entityId, string entityType, double latitude, double longitude, Dictionary<string, string>? metadata = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RemoveAsync(string entityId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<List<SpatialResult>> GetRingAsync(int ringLevel, double centerLat, double centerLng, CancellationToken ct = default)
            => Task.FromResult(new List<SpatialResult>());

        public Task UpdatePositionAsync(string entityId, double latitude, double longitude, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    /// <summary>
    /// Minimal INotificationSendPort test double that tracks batch calls.
    /// </summary>
    private class TestNotificationSendPort : INotificationSendPort
    {
        public int LastBatchSize { get; private set; }

        public Task<NotificationResult> SendPushAsync(NotificationPayload payload, CancellationToken ct = default)
        {
            return Task.FromResult(new NotificationResult(
                payload.NotificationId, payload.RecipientUserId,
                NotificationChannel.Push, NotificationDeliveryStatus.Sent,
                $"mock-{payload.NotificationId}", null, DateTime.UtcNow));
        }

        public Task<IReadOnlyList<NotificationResult>> SendPushBatchAsync(IReadOnlyList<NotificationPayload> payloads, CancellationToken ct = default)
        {
            LastBatchSize = payloads.Count;
            var results = payloads.Select(p => new NotificationResult(
                p.NotificationId, p.RecipientUserId,
                NotificationChannel.Push, NotificationDeliveryStatus.Sent,
                $"mock-{p.NotificationId}", null, DateTime.UtcNow)).ToList();
            return Task.FromResult<IReadOnlyList<NotificationResult>>(results);
        }

        public Task<bool> CancelNotificationAsync(string notificationId, string recipientUserId, CancellationToken ct = default)
            => Task.FromResult(true);
    }
}
