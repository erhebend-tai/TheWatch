// MockResponseCoordinationAdapter — mock implementations of all response coordination ports.
// Fully functional with in-memory state. Simulates dispatch delays, responder acknowledgments,
// and escalation behavior without any external dependencies.
//
// This is a PERMANENT first-class adapter, not a throwaway test double.
// Every dashboard screen works against these mocks before live adapters exist.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Adapters.Mock;

// ═══════════════════════════════════════════════════════════════
// Mock Response Request Port
// ═══════════════════════════════════════════════════════════════

public class MockResponseRequestAdapter : IResponseRequestPort
{
    private readonly ConcurrentDictionary<string, ResponseRequest> _requests = new();
    private readonly ILogger<MockResponseRequestAdapter> _logger;

    public MockResponseRequestAdapter(ILogger<MockResponseRequestAdapter> logger)
    {
        _logger = logger;
    }

    public Task<ResponseRequest> CreateRequestAsync(ResponseRequest request, CancellationToken ct = default)
    {
        _requests[request.RequestId] = request with { Status = ResponseStatus.Dispatching };
        _logger.LogInformation(
            "[MockResponse] Created request {Id}: Scope={Scope}, Radius={Radius}m, Responders={Count}",
            request.RequestId, request.Scope, request.RadiusMeters, request.DesiredResponderCount);
        return Task.FromResult(_requests[request.RequestId]);
    }

    public Task<ResponseRequest?> GetRequestAsync(string requestId, CancellationToken ct = default)
        => Task.FromResult(_requests.TryGetValue(requestId, out var r) ? r : null);

    public Task<IReadOnlyList<ResponseRequest>> GetActiveRequestsAsync(string userId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ResponseRequest>>(
            _requests.Values
                .Where(r => r.UserId == userId && r.Status is ResponseStatus.Active or ResponseStatus.Dispatching)
                .OrderByDescending(r => r.CreatedAt)
                .ToList());

    public Task<ResponseRequest> CancelRequestAsync(string requestId, string reason, CancellationToken ct = default)
    {
        if (_requests.TryGetValue(requestId, out var request))
        {
            var updated = request with { Status = ResponseStatus.Cancelled };
            _requests[requestId] = updated;
            _logger.LogInformation("[MockResponse] Cancelled request {Id}: {Reason}", requestId, reason);
            return Task.FromResult(updated);
        }
        throw new KeyNotFoundException($"Request {requestId} not found");
    }

    public Task<ResponseRequest> ResolveRequestAsync(string requestId, string resolvedBy, CancellationToken ct = default)
    {
        if (_requests.TryGetValue(requestId, out var request))
        {
            var updated = request with { Status = ResponseStatus.Resolved };
            _requests[requestId] = updated;
            _logger.LogInformation("[MockResponse] Resolved request {Id} by {ResolvedBy}", requestId, resolvedBy);
            return Task.FromResult(updated);
        }
        throw new KeyNotFoundException($"Request {requestId} not found");
    }

    public Task<ResponseRequest> UpdateStatusAsync(string requestId, ResponseStatus newStatus, CancellationToken ct = default)
    {
        if (_requests.TryGetValue(requestId, out var request))
        {
            var updated = request with { Status = newStatus };
            _requests[requestId] = updated;
            return Task.FromResult(updated);
        }
        throw new KeyNotFoundException($"Request {requestId} not found");
    }
}

// ═══════════════════════════════════════════════════════════════
// Mock Response Dispatch Port
// ═══════════════════════════════════════════════════════════════

public class MockResponseDispatchAdapter : IResponseDispatchPort
{
    private readonly ILogger<MockResponseDispatchAdapter> _logger;

    public MockResponseDispatchAdapter(ILogger<MockResponseDispatchAdapter> logger)
    {
        _logger = logger;
    }

    public Task<int> DispatchAsync(ResponseRequest request, CancellationToken ct = default)
    {
        var defaults = ResponseScopePresets.GetDefaults(request.Scope);
        var notified = Math.Min(request.DesiredResponderCount, 20); // Mock caps at 20

        _logger.LogInformation(
            "[MockDispatch] Dispatched {Count} responders for {Scope} request {Id} " +
            "(radius: {Radius}m, strategy: {Strategy})",
            notified, request.Scope, request.RequestId, request.RadiusMeters, request.Strategy);

        return Task.FromResult(notified);
    }

    public Task<int> RedispatchAsync(ResponseRequest request, double newRadiusMeters,
        int newDesiredCount, CancellationToken ct = default)
    {
        var notified = Math.Min(newDesiredCount, 50);
        _logger.LogWarning(
            "[MockDispatch] REDISPATCH for {Id}: expanded radius to {Radius}m, targeting {Count} responders",
            request.RequestId, newRadiusMeters, notified);
        return Task.FromResult(notified);
    }
}

// ═══════════════════════════════════════════════════════════════
// Mock Response Tracking Port
// ═══════════════════════════════════════════════════════════════

public class MockResponseTrackingAdapter : IResponseTrackingPort
{
    private readonly ConcurrentDictionary<string, ResponderAcknowledgment> _acks = new();
    private readonly ILogger<MockResponseTrackingAdapter> _logger;

    public MockResponseTrackingAdapter(ILogger<MockResponseTrackingAdapter> logger)
    {
        _logger = logger;
    }

    public Task<ResponderAcknowledgment> AcknowledgeAsync(ResponderAcknowledgment ack, CancellationToken ct = default)
    {
        _acks[ack.AckId] = ack;
        _logger.LogInformation(
            "[MockTracking] Responder {Name} ({Role}) acknowledged request {RequestId} — ETA: {ETA}",
            ack.ResponderName, ack.ResponderRole, ack.RequestId, ack.EstimatedArrival);
        return Task.FromResult(ack);
    }

    public Task<ResponderAcknowledgment> UpdateAckStatusAsync(string ackId, AckStatus newStatus, CancellationToken ct = default)
    {
        if (_acks.TryGetValue(ackId, out var ack))
        {
            var updated = ack with { Status = newStatus };
            _acks[ackId] = updated;
            _logger.LogInformation("[MockTracking] Ack {Id} status → {Status}", ackId, newStatus);
            return Task.FromResult(updated);
        }
        throw new KeyNotFoundException($"Acknowledgment {ackId} not found");
    }

    public Task<IReadOnlyList<ResponderAcknowledgment>> GetAcknowledgmentsAsync(string requestId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ResponderAcknowledgment>>(
            _acks.Values.Where(a => a.RequestId == requestId).OrderBy(a => a.DistanceMeters).ToList());

    public Task<int> GetAcknowledgmentCountAsync(string requestId, CancellationToken ct = default)
        => Task.FromResult(_acks.Values.Count(a => a.RequestId == requestId && a.Status != AckStatus.Declined));
}

// ═══════════════════════════════════════════════════════════════
// Mock Escalation Port
// ═══════════════════════════════════════════════════════════════

public class MockEscalationAdapter : IEscalationPort
{
    private readonly ConcurrentDictionary<string, List<EscalationEvent>> _history = new();
    private readonly ILogger<MockEscalationAdapter> _logger;

    public MockEscalationAdapter(ILogger<MockEscalationAdapter> logger)
    {
        _logger = logger;
    }

    public Task ScheduleEscalationAsync(ResponseRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "[MockEscalation] Scheduled escalation for {Id}: policy={Policy}, timeout={Timeout}",
            request.RequestId, request.Escalation, request.EscalationTimeout);
        // In production: Hangfire.BackgroundJob.Schedule(() => CheckAndEscalateAsync(...), request.EscalationTimeout);
        return Task.CompletedTask;
    }

    public Task<EscalationEvent?> CheckAndEscalateAsync(string requestId, CancellationToken ct = default)
    {
        _logger.LogInformation("[MockEscalation] Checking escalation for {Id}", requestId);
        // Mock: always return null (no escalation needed in mock mode)
        return Task.FromResult<EscalationEvent?>(null);
    }

    public Task CancelEscalationAsync(string requestId, CancellationToken ct = default)
    {
        _logger.LogInformation("[MockEscalation] Cancelled escalation for {Id}", requestId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<EscalationEvent>> GetEscalationHistoryAsync(string requestId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<EscalationEvent>>(
            _history.TryGetValue(requestId, out var events) ? events : new List<EscalationEvent>());
}

// ═══════════════════════════════════════════════════════════════
// Mock Participation Port
// ═══════════════════════════════════════════════════════════════

public class MockParticipationAdapter : IParticipationPort
{
    private readonly ConcurrentDictionary<string, ParticipationPreferences> _prefs = new();
    private readonly ILogger<MockParticipationAdapter> _logger;

    public MockParticipationAdapter(ILogger<MockParticipationAdapter> logger)
    {
        _logger = logger;
        SeedMockData();
    }

    private void SeedMockData()
    {
        // Seed some mock participants
        // Tuple: (id, name, isEnabled, checkIn, neighborhood, community, certs, hasVehicle)
        var users = new[]
        {
            ("resp-001", "Marcus Chen", true, true, true, true, new[] { "EMT", "CPR" }, true),
            ("resp-002", "Sarah Williams", true, true, true, false, new[] { "NURSE", "FIRST_AID" }, true),
            ("resp-003", "David Kim", true, true, false, false, Array.Empty<string>(), false),       // On foot
            ("resp-004", "Elena Rodriguez", true, true, true, true, new[] { "CPR" }, true),
            ("resp-005", "James Thompson", false, false, false, false, Array.Empty<string>(), false), // Opted out
            ("resp-006", "Aisha Patel", true, true, true, false, new[] { "FIRST_AID" }, false),      // On foot
            ("resp-007", "Robert Jackson", true, true, false, true, new[] { "EMT" }, true),
            ("resp-008", "Maria Gonzalez", true, false, false, false, Array.Empty<string>(), false),  // On foot
        };

        foreach (var (id, _, isEnabled, checkIn, neighborhood, community, certs, hasVehicle) in users)
        {
            _prefs[id] = new ParticipationPreferences(
                UserId: id,
                IsResponderEnabled: isEnabled,
                OptedInCheckIn: checkIn,
                OptedInNeighborhood: neighborhood,
                OptedInCommunity: community,
                OptedInEvacuation: true, // Everyone gets evacuation alerts
                IsCurrentlyAvailable: isEnabled,
                AvailableFrom: null,
                AvailableTo: null,
                AvailableDays: null,
                Certifications: certs,
                MaxResponseRadiusMeters: 5000,
                WillingToBeFirstOnScene: certs.Length > 0,
                HasVehicle: hasVehicle,
                QuietHoursStart: new TimeOnly(23, 0),
                QuietHoursEnd: new TimeOnly(7, 0),
                LastUpdated: DateTime.UtcNow
            );
        }
    }

    public Task<ParticipationPreferences?> GetPreferencesAsync(string userId, CancellationToken ct = default)
        => Task.FromResult(_prefs.TryGetValue(userId, out var p) ? p : null);

    public Task<ParticipationPreferences> UpdatePreferencesAsync(ParticipationPreferences prefs, CancellationToken ct = default)
    {
        _prefs[prefs.UserId] = prefs with { LastUpdated = DateTime.UtcNow };
        _logger.LogInformation(
            "[MockParticipation] Updated prefs for {UserId}: enabled={Enabled}, checkIn={CheckIn}, " +
            "neighborhood={Neighborhood}, community={Community}",
            prefs.UserId, prefs.IsResponderEnabled, prefs.OptedInCheckIn,
            prefs.OptedInNeighborhood, prefs.OptedInCommunity);
        return Task.FromResult(_prefs[prefs.UserId]);
    }

    public Task<IReadOnlyList<EligibleResponder>> FindEligibleRespondersAsync(
        double latitude, double longitude, double radiusMeters, ResponseScope scope,
        int maxResults = 50, CancellationToken ct = default)
    {
        var maxWalkingDistance = DispatchDistancePolicy.DefaultMaxWalkingDistanceMeters;

        var eligible = _prefs.Values
            .Where(p => p.IsResponderEnabled && p.IsCurrentlyAvailable)
            .Where(p => scope switch
            {
                ResponseScope.CheckIn => p.OptedInCheckIn,
                ResponseScope.Neighborhood => p.OptedInNeighborhood,
                ResponseScope.Community => p.OptedInCommunity,
                ResponseScope.Evacuation => p.OptedInEvacuation,
                ResponseScope.SilentDuress => false, // Trusted contacts only, handled separately
                _ => true
            })
            .Select((p, i) => new
            {
                Prefs = p,
                Index = i,
                Distance = 200.0 + (i * 150) // Mock: increasing distances
            })
            // Exclude responders who are beyond walking distance and have no vehicle.
            // A responder on foot 3km away should NOT be asked to respond — they can't
            // get there in time and it's unreasonable to ask someone to walk that far
            // in an emergency. Only responders with a vehicle are dispatched beyond
            // the max walking distance threshold (default: 1600m / ~1 mile).
            .Where(x => x.Prefs.HasVehicle || x.Distance <= maxWalkingDistance)
            .Select(x => new EligibleResponder(
                UserId: x.Prefs.UserId,
                Name: $"Responder {x.Prefs.UserId}",
                Latitude: latitude + (x.Index * 0.002), // Mock: spread responders nearby
                Longitude: longitude + (x.Index * 0.002),
                DistanceMeters: x.Distance,
                Certifications: x.Prefs.Certifications,
                IsFirstOnSceneWilling: x.Prefs.WillingToBeFirstOnScene,
                HasVehicle: x.Prefs.HasVehicle,
                LastActiveAt: DateTime.UtcNow.AddMinutes(-x.Index * 5)))
            .Take(maxResults)
            .ToList();

        _logger.LogInformation(
            "[MockParticipation] Found {Count} eligible responders for {Scope} within {Radius}m " +
            "(excluded on-foot responders beyond {WalkLimit}m)",
            eligible.Count, scope, radiusMeters, maxWalkingDistance);

        return Task.FromResult<IReadOnlyList<EligibleResponder>>(eligible);
    }

    public Task SetAvailabilityAsync(string userId, bool isAvailable, TimeSpan? duration = null, CancellationToken ct = default)
    {
        if (_prefs.TryGetValue(userId, out var prefs))
        {
            _prefs[userId] = prefs with { IsCurrentlyAvailable = isAvailable, LastUpdated = DateTime.UtcNow };
            _logger.LogInformation(
                "[MockParticipation] {UserId} availability → {Available} (duration: {Duration})",
                userId, isAvailable, duration?.ToString() ?? "indefinite");
        }
        return Task.CompletedTask;
    }
}

// ═══════════════════════════════════════════════════════════════
// Mock Navigation Port
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Mock navigation adapter — generates platform deep links for Google Maps,
/// Apple Maps, and Waze without calling any external routing API.
/// Estimates travel time using straight-line distance and average speeds:
///   Walking: ~5 km/h (~83 m/min)
///   Driving: ~40 km/h (~667 m/min) — conservative urban average
/// </summary>
public class MockNavigationAdapter : INavigationPort
{
    private readonly ILogger<MockNavigationAdapter> _logger;

    // Walking speed ≈ 5 km/h = 83.3 m/min
    private const double WalkingSpeedMetersPerMinute = 83.3;
    // Driving speed ≈ 40 km/h = 666.7 m/min (urban average with traffic)
    private const double DrivingSpeedMetersPerMinute = 666.7;

    public MockNavigationAdapter(ILogger<MockNavigationAdapter> logger)
    {
        _logger = logger;
    }

    public double MaxWalkingDistanceMeters => DispatchDistancePolicy.DefaultMaxWalkingDistanceMeters;

    public Task<NavigationDirections> GetDirectionsAsync(
        string requestId,
        string responderId,
        double responderLatitude,
        double responderLongitude,
        double incidentLatitude,
        double incidentLongitude,
        bool hasVehicle,
        CancellationToken ct = default)
    {
        var distanceMeters = HaversineDistance(
            responderLatitude, responderLongitude,
            incidentLatitude, incidentLongitude);

        // Pick travel mode: drive if they have a vehicle, walk if close enough on foot
        var travelMode = hasVehicle ? "driving" : "walking";
        var speedMpm = hasVehicle ? DrivingSpeedMetersPerMinute : WalkingSpeedMetersPerMinute;
        var estimatedMinutes = distanceMeters / speedMpm;

        // Apple Maps dirflg: d=driving, w=walking
        var appleDirFlag = hasVehicle ? "d" : "w";

        var directions = new NavigationDirections(
            RequestId: requestId,
            ResponderId: responderId,
            IncidentLatitude: incidentLatitude,
            IncidentLongitude: incidentLongitude,
            ResponderLatitude: responderLatitude,
            ResponderLongitude: responderLongitude,
            DistanceMeters: distanceMeters,
            TravelMode: travelMode,
            GoogleMapsUrl: $"https://www.google.com/maps/dir/?api=1" +
                $"&origin={responderLatitude},{responderLongitude}" +
                $"&destination={incidentLatitude},{incidentLongitude}" +
                $"&travelmode={travelMode}",
            AppleMapsUrl: $"https://maps.apple.com/?saddr={responderLatitude},{responderLongitude}" +
                $"&daddr={incidentLatitude},{incidentLongitude}" +
                $"&dirflg={appleDirFlag}",
            WazeUrl: $"https://waze.com/ul?ll={incidentLatitude},{incidentLongitude}&navigate=yes",
            EstimatedTravelTime: TimeSpan.FromMinutes(estimatedMinutes)
        );

        _logger.LogInformation(
            "[MockNavigation] Directions for responder {ResponderId} → incident {RequestId}: " +
            "{Distance:F0}m via {Mode}, ETA ~{ETA:F1} min",
            responderId, requestId, distanceMeters, travelMode, estimatedMinutes);

        return Task.FromResult(directions);
    }

    public bool ShouldExcludeFromDispatch(double distanceMeters, bool hasVehicle)
        => !hasVehicle && distanceMeters > MaxWalkingDistanceMeters;

    /// <summary>
    /// Haversine formula for great-circle distance between two lat/lng points.
    /// Returns distance in meters.
    /// </summary>
    private static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6_371_000; // Earth radius in meters
        var dLat = DegreesToRadians(lat2 - lat1);
        var dLon = DegreesToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180;
}
