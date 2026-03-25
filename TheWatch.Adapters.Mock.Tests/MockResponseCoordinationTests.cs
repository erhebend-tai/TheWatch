// MockResponseCoordinationTests — verifies all response coordination mock adapters
// fulfill their port contracts: request lifecycle, dispatch, tracking, escalation,
// participation filtering, and navigation/distance-exclusion logic.
//
// Example — running these tests:
//   dotnet test --filter "FullyQualifiedName~MockResponseCoordinationTests"
//
// WAL: Every port interface method has at least one test. If a new method is added
//      to any IResponseXxxPort, a corresponding test MUST be added here.

using Microsoft.Extensions.Logging.Abstractions;
using TheWatch.Adapters.Mock;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Adapters.Mock.Tests;

#region Helpers

/// <summary>
/// Shared helper for building test <see cref="ResponseRequest"/> instances.
/// Uses <see cref="ResponseScopePresets"/> so tests stay in sync with production defaults.
/// </summary>
internal static class TestRequestFactory
{
    public static ResponseRequest Create(
        string id = "test-req-001",
        string userId = "test-user",
        ResponseScope scope = ResponseScope.CheckIn)
    {
        var (radius, desired, escalation, strategy, timeout) = ResponseScopePresets.GetDefaults(scope);
        return new ResponseRequest(
            RequestId: id, UserId: userId, DeviceId: null,
            Scope: scope, Escalation: escalation, Strategy: strategy,
            Latitude: 30.2672, Longitude: -97.7431, AccuracyMeters: 10,
            RadiusMeters: radius, DesiredResponderCount: desired,
            EscalationTimeout: timeout,
            Description: "Test request", TriggerSource: "TEST",
            TriggerConfidence: 0.95f, CreatedAt: DateTime.UtcNow);
    }
}

#endregion

// ═══════════════════════════════════════════════════════════════
// MockResponseRequestAdapter Tests
// ═══════════════════════════════════════════════════════════════

public class MockResponseRequestAdapterTests
{
    private readonly MockResponseRequestAdapter _sut = new(NullLogger<MockResponseRequestAdapter>.Instance);

    [Fact]
    public async Task CreateRequestAsync_SetsStatusToDispatching()
    {
        var request = TestRequestFactory.Create();

        var result = await _sut.CreateRequestAsync(request);

        Assert.Equal(ResponseStatus.Dispatching, result.Status);
        Assert.Equal(request.RequestId, result.RequestId);
    }

    [Fact]
    public async Task GetRequestAsync_ReturnsNull_ForNonExistentId()
    {
        var result = await _sut.GetRequestAsync("does-not-exist");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetRequestAsync_ReturnsCreatedRequest()
    {
        var request = TestRequestFactory.Create(id: "get-test-001");
        await _sut.CreateRequestAsync(request);

        var result = await _sut.GetRequestAsync("get-test-001");

        Assert.NotNull(result);
        Assert.Equal("get-test-001", result!.RequestId);
        Assert.Equal(ResponseStatus.Dispatching, result.Status);
    }

    [Fact]
    public async Task GetActiveRequestsAsync_FiltersByUserIdAndActiveStatuses()
    {
        // Arrange — create requests for two users, one cancelled
        await _sut.CreateRequestAsync(TestRequestFactory.Create(id: "active-1", userId: "user-A"));
        await _sut.CreateRequestAsync(TestRequestFactory.Create(id: "active-2", userId: "user-A"));
        await _sut.CreateRequestAsync(TestRequestFactory.Create(id: "active-3", userId: "user-B"));
        await _sut.CancelRequestAsync("active-2", "test cancel");

        var results = await _sut.GetActiveRequestsAsync("user-A");

        // Only active-1 should remain (active-2 cancelled, active-3 belongs to user-B)
        Assert.Single(results);
        Assert.Equal("active-1", results[0].RequestId);
    }

    [Fact]
    public async Task CancelRequestAsync_SetsCancelledStatus()
    {
        await _sut.CreateRequestAsync(TestRequestFactory.Create(id: "cancel-001"));

        var result = await _sut.CancelRequestAsync("cancel-001", "User confirmed safe");

        Assert.Equal(ResponseStatus.Cancelled, result.Status);
    }

    [Fact]
    public async Task CancelRequestAsync_ThrowsKeyNotFoundException_ForInvalidId()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.CancelRequestAsync("nonexistent", "reason"));
    }

    [Fact]
    public async Task ResolveRequestAsync_SetsResolvedStatus()
    {
        await _sut.CreateRequestAsync(TestRequestFactory.Create(id: "resolve-001"));

        var result = await _sut.ResolveRequestAsync("resolve-001", "responder-42");

        Assert.Equal(ResponseStatus.Resolved, result.Status);
    }

    [Fact]
    public async Task UpdateStatusAsync_UpdatesStatusCorrectly()
    {
        await _sut.CreateRequestAsync(TestRequestFactory.Create(id: "status-001"));

        var result = await _sut.UpdateStatusAsync("status-001", ResponseStatus.Active);

        Assert.Equal(ResponseStatus.Active, result.Status);
    }
}

// ═══════════════════════════════════════════════════════════════
// MockResponseDispatchAdapter Tests
// ═══════════════════════════════════════════════════════════════

public class MockResponseDispatchAdapterTests
{
    private readonly MockResponseDispatchAdapter _sut = new(NullLogger<MockResponseDispatchAdapter>.Instance);

    [Fact]
    public async Task DispatchAsync_ReturnsMinOfDesiredCountAnd20()
    {
        // CheckIn default desired = 8 (< 20), so should return 8
        var request = TestRequestFactory.Create(scope: ResponseScope.CheckIn);
        var result = await _sut.DispatchAsync(request);
        Assert.Equal(8, result);

        // Community default desired = 50 (> 20), should cap at 20
        var communityRequest = TestRequestFactory.Create(id: "dispatch-community", scope: ResponseScope.Community);
        var communityResult = await _sut.DispatchAsync(communityRequest);
        Assert.Equal(20, communityResult);
    }

    [Fact]
    public async Task RedispatchAsync_ReturnsMinOfNewDesiredCountAnd50()
    {
        var request = TestRequestFactory.Create();

        var result = await _sut.RedispatchAsync(request, 5000, 30);
        Assert.Equal(30, result);

        var capped = await _sut.RedispatchAsync(request, 10000, 100);
        Assert.Equal(50, capped);
    }
}

// ═══════════════════════════════════════════════════════════════
// MockResponseTrackingAdapter Tests
// ═══════════════════════════════════════════════════════════════

public class MockResponseTrackingAdapterTests
{
    private readonly MockResponseTrackingAdapter _sut = new(NullLogger<MockResponseTrackingAdapter>.Instance);

    private static ResponderAcknowledgment CreateTestAck(
        string ackId = "ack-001",
        string requestId = "req-001",
        AckStatus status = AckStatus.Acknowledged) =>
        new(
            AckId: ackId, RequestId: requestId,
            ResponderId: "resp-001", ResponderName: "Test Responder",
            ResponderRole: "VOLUNTEER",
            ResponderLatitude: 30.27, ResponderLongitude: -97.74,
            DistanceMeters: 500, EstimatedArrival: TimeSpan.FromMinutes(5),
            Status: status, AcknowledgedAt: DateTime.UtcNow);

    [Fact]
    public async Task AcknowledgeAsync_StoresAndReturnsAck()
    {
        var ack = CreateTestAck();

        var result = await _sut.AcknowledgeAsync(ack);

        Assert.Equal("ack-001", result.AckId);
        Assert.Equal(AckStatus.Acknowledged, result.Status);
    }

    [Fact]
    public async Task UpdateAckStatusAsync_UpdatesStatus()
    {
        await _sut.AcknowledgeAsync(CreateTestAck());

        var result = await _sut.UpdateAckStatusAsync("ack-001", AckStatus.EnRoute);

        Assert.Equal(AckStatus.EnRoute, result.Status);
    }

    [Fact]
    public async Task UpdateAckStatusAsync_ThrowsForInvalidAckId()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => _sut.UpdateAckStatusAsync("nonexistent", AckStatus.EnRoute));
    }

    [Fact]
    public async Task GetAcknowledgmentsAsync_FiltersByRequestId()
    {
        await _sut.AcknowledgeAsync(CreateTestAck(ackId: "a1", requestId: "req-A"));
        await _sut.AcknowledgeAsync(CreateTestAck(ackId: "a2", requestId: "req-A"));
        await _sut.AcknowledgeAsync(CreateTestAck(ackId: "a3", requestId: "req-B"));

        var results = await _sut.GetAcknowledgmentsAsync("req-A");

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Equal("req-A", r.RequestId));
    }

    [Fact]
    public async Task GetAcknowledgmentCountAsync_ExcludesDeclinedAcks()
    {
        await _sut.AcknowledgeAsync(CreateTestAck(ackId: "c1", requestId: "req-count"));
        await _sut.AcknowledgeAsync(CreateTestAck(ackId: "c2", requestId: "req-count", status: AckStatus.Declined));
        await _sut.AcknowledgeAsync(CreateTestAck(ackId: "c3", requestId: "req-count", status: AckStatus.EnRoute));

        var count = await _sut.GetAcknowledgmentCountAsync("req-count");

        // c1 (Acknowledged) + c3 (EnRoute) = 2; c2 (Declined) excluded
        Assert.Equal(2, count);
    }
}

// ═══════════════════════════════════════════════════════════════
// MockEscalationAdapter Tests
// ═══════════════════════════════════════════════════════════════

public class MockEscalationAdapterTests
{
    private readonly MockEscalationAdapter _sut = new(NullLogger<MockEscalationAdapter>.Instance);

    [Fact]
    public async Task ScheduleEscalationAsync_CompletesWithoutError()
    {
        var request = TestRequestFactory.Create();
        await _sut.ScheduleEscalationAsync(request); // Should not throw
    }

    [Fact]
    public async Task CheckAndEscalateAsync_ReturnsNull()
    {
        var result = await _sut.CheckAndEscalateAsync("any-request-id");

        Assert.Null(result);
    }

    [Fact]
    public async Task CancelEscalationAsync_CompletesWithoutError()
    {
        await _sut.CancelEscalationAsync("any-request-id"); // Should not throw
    }

    [Fact]
    public async Task GetEscalationHistoryAsync_ReturnsEmpty_ForUnknownRequestId()
    {
        var result = await _sut.GetEscalationHistoryAsync("unknown-request");

        Assert.Empty(result);
    }
}

// ═══════════════════════════════════════════════════════════════
// MockParticipationAdapter Tests
// ═══════════════════════════════════════════════════════════════

public class MockParticipationAdapterTests
{
    private readonly MockParticipationAdapter _sut = new(NullLogger<MockParticipationAdapter>.Instance);

    [Theory]
    [InlineData("resp-001")]
    [InlineData("resp-004")]
    [InlineData("resp-008")]
    public async Task GetPreferencesAsync_ReturnsSeededData_ForKnownUsers(string userId)
    {
        var prefs = await _sut.GetPreferencesAsync(userId);

        Assert.NotNull(prefs);
        Assert.Equal(userId, prefs!.UserId);
    }

    [Fact]
    public async Task GetPreferencesAsync_ReturnsNull_ForUnknownUsers()
    {
        var prefs = await _sut.GetPreferencesAsync("nonexistent-user");

        Assert.Null(prefs);
    }

    [Fact]
    public async Task UpdatePreferencesAsync_UpdatesLastUpdated()
    {
        var original = (await _sut.GetPreferencesAsync("resp-001"))!;
        var before = original.LastUpdated;

        // Small delay to ensure timestamp difference
        await Task.Delay(10);
        var updated = await _sut.UpdatePreferencesAsync(original with { OptedInCommunity = false });

        Assert.True(updated.LastUpdated >= before);
        Assert.False(updated.OptedInCommunity);
    }

    [Fact]
    public async Task FindEligibleRespondersAsync_FiltersByScopeOptIn()
    {
        // CheckIn: resp-001,002,003,004,006 opted in (005 disabled, 007 not opted in checkIn? let's verify)
        var checkInResponders = await _sut.FindEligibleRespondersAsync(
            30.2672, -97.7431, 5000, ResponseScope.CheckIn);

        // Neighborhood: fewer users opted in (resp-001,002,004,006 have OptedInNeighborhood)
        var neighborhoodResponders = await _sut.FindEligibleRespondersAsync(
            30.2672, -97.7431, 5000, ResponseScope.Neighborhood);

        // Neighborhood should have equal or fewer responders than CheckIn
        Assert.True(neighborhoodResponders.Count <= checkInResponders.Count);
    }

    [Fact]
    public async Task FindEligibleRespondersAsync_ExcludesDisabledUsers()
    {
        // resp-005 has IsResponderEnabled=false
        var responders = await _sut.FindEligibleRespondersAsync(
            30.2672, -97.7431, 50000, ResponseScope.CheckIn);

        Assert.DoesNotContain(responders, r => r.UserId == "resp-005");
    }

    [Fact]
    public async Task FindEligibleRespondersAsync_ExcludesOnFootRespondersBeyondWalkingDistance()
    {
        // The mock assigns increasing distances (200 + i*150).
        // Responders on foot (no vehicle) beyond 1600m should be excluded.
        // resp-003 is on foot (HasVehicle=false), resp-008 is on foot.
        // Their mock distance depends on their index in the filtered+eligible list.
        var responders = await _sut.FindEligibleRespondersAsync(
            30.2672, -97.7431, 50000, ResponseScope.CheckIn);

        foreach (var r in responders)
        {
            if (!r.HasVehicle)
            {
                // On-foot responders that made the cut must be within walking distance
                Assert.True(r.DistanceMeters <= DispatchDistancePolicy.DefaultMaxWalkingDistanceMeters,
                    $"On-foot responder {r.UserId} at {r.DistanceMeters}m should have been excluded (max walking: {DispatchDistancePolicy.DefaultMaxWalkingDistanceMeters}m)");
            }
        }
    }

    [Fact]
    public async Task SetAvailabilityAsync_TogglesAvailability()
    {
        await _sut.SetAvailabilityAsync("resp-001", false);
        var prefs = await _sut.GetPreferencesAsync("resp-001");
        Assert.False(prefs!.IsCurrentlyAvailable);

        await _sut.SetAvailabilityAsync("resp-001", true);
        prefs = await _sut.GetPreferencesAsync("resp-001");
        Assert.True(prefs!.IsCurrentlyAvailable);
    }
}

// ═══════════════════════════════════════════════════════════════
// MockNavigationAdapter Tests
// ═══════════════════════════════════════════════════════════════

public class MockNavigationAdapterTests
{
    private readonly MockNavigationAdapter _sut = new(NullLogger<MockNavigationAdapter>.Instance);

    [Fact]
    public async Task GetDirectionsAsync_ReturnsValidUrls_ForDriving()
    {
        var result = await _sut.GetDirectionsAsync(
            "req-001", "resp-001",
            30.2672, -97.7431,  // responder
            30.2700, -97.7400,  // incident
            hasVehicle: true);

        Assert.Equal("driving", result.TravelMode);
        Assert.Contains("travelmode=driving", result.GoogleMapsUrl);
        Assert.Contains("dirflg=d", result.AppleMapsUrl);
        Assert.Contains("waze.com", result.WazeUrl);
        Assert.NotNull(result.EstimatedTravelTime);
    }

    [Fact]
    public async Task GetDirectionsAsync_ReturnsWalkingMode_ForNoVehicle()
    {
        var result = await _sut.GetDirectionsAsync(
            "req-001", "resp-002",
            30.2672, -97.7431,
            30.2680, -97.7425,
            hasVehicle: false);

        Assert.Equal("walking", result.TravelMode);
        Assert.Contains("travelmode=walking", result.GoogleMapsUrl);
        Assert.Contains("dirflg=w", result.AppleMapsUrl);
    }

    [Fact]
    public void ShouldExcludeFromDispatch_ReturnsTrue_ForFarOnFootResponders()
    {
        // 3000m away, no vehicle — should be excluded
        Assert.True(_sut.ShouldExcludeFromDispatch(3000, hasVehicle: false));
    }

    [Fact]
    public void ShouldExcludeFromDispatch_ReturnsFalse_ForDrivers()
    {
        // 3000m away but has vehicle — should NOT be excluded
        Assert.False(_sut.ShouldExcludeFromDispatch(3000, hasVehicle: true));

        // Even very far drivers should not be excluded
        Assert.False(_sut.ShouldExcludeFromDispatch(50000, hasVehicle: true));
    }

    [Fact]
    public void MaxWalkingDistanceMeters_Equals1600()
    {
        Assert.Equal(DispatchDistancePolicy.DefaultMaxWalkingDistanceMeters, _sut.MaxWalkingDistanceMeters);
        Assert.Equal(1600, _sut.MaxWalkingDistanceMeters);
    }
}
