// =============================================================================
// Mock Watch Call Adapter — permanent first-class in-memory implementation.
// =============================================================================
// Simulates the full Watch Call lifecycle: enrollment, mock training scenarios,
// live call creation, participant management, narration transcript, escalation.
//
// Seeded data:
//   - 4 mock call training scenarios covering common situations
//   - 3 enrolled users (one active, one pending training, one suspended)
//   - 1 completed mock call with narration transcript
//
// WAL: All operations are in-memory only. No external network calls.
// =============================================================================

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Adapters.Mock;

public class MockWatchCallAdapter : IWatchCallPort
{
    private readonly ConcurrentDictionary<string, WatchCallEnrollment> _enrollments = new();
    private readonly ConcurrentDictionary<string, MockCallScenario> _scenarios = new();
    private readonly ConcurrentDictionary<string, WatchCall> _calls = new();
    private readonly ILogger<MockWatchCallAdapter> _logger;
    private int _participantCounter;

    public MockWatchCallAdapter(ILogger<MockWatchCallAdapter> logger)
    {
        _logger = logger;
        SeedMockData();
    }

    private void SeedMockData()
    {
        // ── Mock Call Training Scenarios ────────────────────────────

        var scenarios = new[]
        {
            new MockCallScenario(
                ScenarioId: "scenario-walking-home",
                Title: "Walking Home Late",
                Description: "Experience what it's like when a neighbor calls a Watch because they see someone unfamiliar.",
                SetupNarrative: "It's 11:15 PM. You're walking home from the bus stop on your street. "
                    + "A neighbor who doesn't recognize you in the dark initiates a Watch Call. "
                    + "Listen to how the narrator describes the scene — notice the neutral, factual language.",
                ScriptedNarrations: new List<TimestampedNarration>
                {
                    new(TimeSpan.FromSeconds(3), "I see one person walking along the sidewalk at a steady, relaxed pace. The street has moderate lighting from two streetlamps."),
                    new(TimeSpan.FromSeconds(8), "The person is carrying what appears to be a bag — possibly a backpack or grocery bag. They are heading in the direction of the residential houses."),
                    new(TimeSpan.FromSeconds(15), "The person has stopped near a mailbox and appears to be checking it. They are now walking up a driveway toward a house."),
                    new(TimeSpan.FromSeconds(22), "The person has reached the front door and appears to be using a key. The porch light has turned on. This appears to be a resident arriving home."),
                    new(TimeSpan.FromSeconds(28), "The person has entered the house. The front door is now closed. The porch light remains on. The scene is quiet.")
                },
                DebriefText: "Notice how the narrator described only what was visible — actions, objects, and environment. "
                    + "No assumptions were made about who the person was or what they intended. "
                    + "That's how every Watch Call works: facts only, no judgment. "
                    + "Now imagine being the person walking home. This is why neutral narration matters.",
                Tags: new List<string> { "nighttime", "pedestrian", "residential", "arriving-home" },
                EstimatedDuration: TimeSpan.FromSeconds(35)),

            new MockCallScenario(
                ScenarioId: "scenario-car-repair",
                Title: "Working on Your Car",
                Description: "You're fixing your car in the driveway and a neighbor who doesn't recognize you calls a Watch.",
                SetupNarrative: "It's a Saturday afternoon. You're under the hood of your car in your driveway, "
                    + "replacing the battery. A new neighbor who hasn't met you yet sees an unfamiliar person "
                    + "working on a car and initiates a Watch Call.",
                ScriptedNarrations: new List<TimestampedNarration>
                {
                    new(TimeSpan.FromSeconds(3), "I see one person standing near a parked vehicle in a residential driveway. The vehicle's hood is open."),
                    new(TimeSpan.FromSeconds(8), "The person appears to be leaning over the engine compartment. They are holding a tool — it looks like a wrench or socket set."),
                    new(TimeSpan.FromSeconds(14), "There are additional tools visible on the ground next to the vehicle. The person's movements are methodical and focused on the engine area."),
                    new(TimeSpan.FromSeconds(20), "The person has stepped back from the vehicle and is wiping their hands. They appear to be inspecting their work."),
                    new(TimeSpan.FromSeconds(26), "The person is closing the vehicle's hood. The tools on the ground are being gathered up. This appears to be routine vehicle maintenance.")
                },
                DebriefText: "The narrator described tools, actions, and the scene — never assuming whether the person "
                    + "\"belonged\" there or not. A person working on a car in a driveway is just that: a person working on a car. "
                    + "Watch Calls provide clarity without judgment.",
                Tags: new List<string> { "daytime", "vehicle", "driveway", "maintenance" },
                EstimatedDuration: TimeSpan.FromSeconds(32)),

            new MockCallScenario(
                ScenarioId: "scenario-delivery",
                Title: "Delivering a Package",
                Description: "You're a delivery driver dropping off a package when a camera flags you as an unknown person.",
                SetupNarrative: "You're delivering a package to a house on your route. The homeowner's camera detects "
                    + "an unfamiliar person approaching the front door and the automated system initiates a Watch Call.",
                ScriptedNarrations: new List<TimestampedNarration>
                {
                    new(TimeSpan.FromSeconds(2), "I see a vehicle stopped near the curb in front of a house. A person is exiting the vehicle from the driver's side."),
                    new(TimeSpan.FromSeconds(7), "The person is carrying a brown rectangular box. They are walking up the path toward the front door of the house."),
                    new(TimeSpan.FromSeconds(12), "The person has placed the box on the front porch near the door. They appear to be taking a photo of the package with a handheld device."),
                    new(TimeSpan.FromSeconds(17), "The person is walking back toward their vehicle. The package remains on the porch. This appears to be a delivery."),
                    new(TimeSpan.FromSeconds(22), "The person has returned to their vehicle and is driving away. The package is still on the porch. Scene is clear.")
                },
                DebriefText: "A delivery. That's it. The narrator didn't speculate, didn't describe the person beyond their actions, "
                    + "and didn't raise an alarm. Context comes from observation, not assumption.",
                Tags: new List<string> { "daytime", "delivery", "package", "vehicle" },
                EstimatedDuration: TimeSpan.FromSeconds(28)),

            new MockCallScenario(
                ScenarioId: "scenario-visiting-friend",
                Title: "Visiting a Friend",
                Description: "You're waiting on a friend's porch for them to answer the door.",
                SetupNarrative: "You've come to visit a friend in their neighborhood. You ring the doorbell and wait "
                    + "on the porch. Your friend is in the shower and takes a few minutes. A nearby enrolled watcher "
                    + "sees someone standing on a porch for several minutes and initiates a Watch Call.",
                ScriptedNarrations: new List<TimestampedNarration>
                {
                    new(TimeSpan.FromSeconds(3), "I see one person standing on the front porch of a house. They appear to be facing the front door."),
                    new(TimeSpan.FromSeconds(10), "The person has pressed what appears to be a doorbell or knocked on the door. They are now standing and waiting."),
                    new(TimeSpan.FromSeconds(20), "The person is still on the porch. They have taken out a handheld device and appear to be looking at it — possibly a phone."),
                    new(TimeSpan.FromSeconds(35), "The person is pacing slowly on the porch. They have looked toward the street once. They remain near the front door."),
                    new(TimeSpan.FromSeconds(50), "The front door has opened. Another person is visible in the doorway. The two people appear to greet each other. They are both entering the house."),
                    new(TimeSpan.FromSeconds(55), "The front door is now closed. The porch is empty. This appears to have been a visitor waiting for the resident.")
                },
                DebriefText: "Waiting on a porch. Checking your phone. Completely ordinary. But without neutral narration, "
                    + "someone watching a camera feed might have interpreted this very differently. "
                    + "The narrator kept it factual — and when the friend opened the door, the situation resolved itself.",
                Tags: new List<string> { "daytime", "visitor", "porch", "waiting" },
                EstimatedDuration: TimeSpan.FromMinutes(1))
        };

        foreach (var s in scenarios)
            _scenarios[s.ScenarioId] = s;

        // ── Seeded Enrollments ─────────────────────────────────────

        _enrollments["mock-user-001"] = new WatchCallEnrollment(
            EnrollmentId: "enroll-001",
            UserId: "mock-user-001",
            DisplayAlias: "Watcher-7F3A",
            Status: WatchCallEnrollmentStatus.Active,
            Latitude: 30.2672,
            Longitude: -97.7431,
            WatchRadiusMeters: 2000,
            MockCallsCompleted: 2,
            LastMockCallAt: DateTime.UtcNow.AddDays(-10),
            TrainingCompletedAt: DateTime.UtcNow.AddDays(-14),
            LiveCallsParticipated: 3,
            CallsAsSubject: 2,
            AverageBehaviorScore: 4.7,
            SuspensionReason: null,
            SuspendedUntil: null,
            EnrolledAt: DateTime.UtcNow.AddDays(-30),
            LastUpdated: DateTime.UtcNow.AddDays(-1));

        _enrollments["mock-user-002"] = new WatchCallEnrollment(
            EnrollmentId: "enroll-002",
            UserId: "mock-user-002",
            DisplayAlias: "Watcher-B91C",
            Status: WatchCallEnrollmentStatus.PendingTraining,
            Latitude: 30.2690,
            Longitude: -97.7415,
            WatchRadiusMeters: 1500,
            MockCallsCompleted: 0,
            LastMockCallAt: null,
            TrainingCompletedAt: null,
            LiveCallsParticipated: 0,
            CallsAsSubject: 0,
            AverageBehaviorScore: null,
            SuspensionReason: null,
            SuspendedUntil: null,
            EnrolledAt: DateTime.UtcNow.AddDays(-2),
            LastUpdated: DateTime.UtcNow.AddDays(-2));

        _enrollments["mock-user-003"] = new WatchCallEnrollment(
            EnrollmentId: "enroll-003",
            UserId: "mock-user-003",
            DisplayAlias: "Watcher-D4E2",
            Status: WatchCallEnrollmentStatus.Active,
            Latitude: 30.2655,
            Longitude: -97.7450,
            WatchRadiusMeters: 2500,
            MockCallsCompleted: 1,
            LastMockCallAt: DateTime.UtcNow.AddDays(-20),
            TrainingCompletedAt: DateTime.UtcNow.AddDays(-20),
            LiveCallsParticipated: 1,
            CallsAsSubject: 1,
            AverageBehaviorScore: 4.2,
            SuspensionReason: null,
            SuspendedUntil: null,
            EnrolledAt: DateTime.UtcNow.AddDays(-25),
            LastUpdated: DateTime.UtcNow.AddDays(-5));

        _logger.LogInformation("[WAL-WATCHCALL-MOCK] Seeded {ScenarioCount} scenarios, {EnrollmentCount} enrollments",
            _scenarios.Count, _enrollments.Count);
    }

    // ── Enrollment ────────────────────────────────────────────────

    public Task<WatchCallEnrollment> EnrollAsync(WatchCallEnrollment enrollment, CancellationToken ct = default)
    {
        var e = enrollment with
        {
            EnrollmentId = string.IsNullOrEmpty(enrollment.EnrollmentId)
                ? $"enroll-{Guid.NewGuid():N}"[..16] : enrollment.EnrollmentId,
            Status = WatchCallEnrollmentStatus.PendingTraining,
            DisplayAlias = $"Watcher-{Guid.NewGuid():N}"[..12].ToUpperInvariant(),
            EnrolledAt = DateTime.UtcNow,
            LastUpdated = DateTime.UtcNow
        };

        _enrollments[e.UserId] = e;
        _logger.LogInformation("[WAL-WATCHCALL-MOCK] Enrolled user {UserId} as {Alias}", e.UserId, e.DisplayAlias);
        return Task.FromResult(e);
    }

    public Task<WatchCallEnrollment?> GetEnrollmentAsync(string userId, CancellationToken ct = default)
    {
        _enrollments.TryGetValue(userId, out var enrollment);
        return Task.FromResult(enrollment);
    }

    public Task<WatchCallEnrollment> UpdateEnrollmentAsync(WatchCallEnrollment enrollment, CancellationToken ct = default)
    {
        var updated = enrollment with { LastUpdated = DateTime.UtcNow };
        _enrollments[updated.UserId] = updated;
        return Task.FromResult(updated);
    }

    public Task<bool> UnenrollAsync(string userId, CancellationToken ct = default)
    {
        if (!_enrollments.TryGetValue(userId, out var enrollment))
            return Task.FromResult(false);

        _enrollments[userId] = enrollment with
        {
            Status = WatchCallEnrollmentStatus.Unenrolled,
            LastUpdated = DateTime.UtcNow
        };
        _logger.LogInformation("[WAL-WATCHCALL-MOCK] Unenrolled user {UserId}", userId);
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<WatchCallEnrollment>> FindNearbyWatchersAsync(
        double latitude, double longitude, double radiusMeters,
        int maxResults = 10, CancellationToken ct = default)
    {
        // Simple distance filter using Haversine approximation
        var nearby = _enrollments.Values
            .Where(e => e.Status == WatchCallEnrollmentStatus.Active)
            .Where(e => HaversineMeters(latitude, longitude, e.Latitude, e.Longitude) <= radiusMeters)
            .OrderBy(e => HaversineMeters(latitude, longitude, e.Latitude, e.Longitude))
            .Take(maxResults)
            .ToList();

        return Task.FromResult<IReadOnlyList<WatchCallEnrollment>>(nearby);
    }

    // ── Mock Call Training ────────────────────────────────────────

    public Task<IReadOnlyList<MockCallScenario>> GetMockScenariosAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<MockCallScenario>>(_scenarios.Values.ToList());
    }

    public Task<MockCallScenario?> GetMockScenarioAsync(string scenarioId, CancellationToken ct = default)
    {
        _scenarios.TryGetValue(scenarioId, out var scenario);
        return Task.FromResult(scenario);
    }

    public Task<WatchCall> StartMockCallAsync(string userId, string scenarioId, CancellationToken ct = default)
    {
        if (!_scenarios.TryGetValue(scenarioId, out var scenario))
            throw new ArgumentException($"Scenario '{scenarioId}' not found.");

        var enrollment = _enrollments.GetValueOrDefault(userId);

        var call = new WatchCall(
            CallId: $"call-mock-{Guid.NewGuid():N}"[..18],
            Status: WatchCallStatus.MockTraining,
            IsMockCall: true,
            MockScenarioId: scenarioId,
            InitiatorUserId: userId,
            LinkedRequestId: null,
            LinkedGuardReportId: null,
            Latitude: enrollment?.Latitude ?? 30.2672,
            Longitude: enrollment?.Longitude ?? -97.7431,
            RadiusMeters: enrollment?.WatchRadiusMeters ?? 2000,
            Participants: new List<WatchCallParticipant>(),
            MaxParticipants: 1,
            NarrationTranscript: scenario.ScriptedNarrations.ToList(),
            RecordingEnabled: false,
            RecordingBlobReference: null,
            RequestedAt: DateTime.UtcNow,
            ConnectedAt: DateTime.UtcNow,
            ResolvedAt: null,
            EscalatedAt: null,
            Resolution: null,
            EscalatedRequestId: null);

        _calls[call.CallId] = call;
        _logger.LogInformation("[WAL-WATCHCALL-MOCK] Started mock call {CallId} for user {UserId}, scenario {ScenarioId}",
            call.CallId, userId, scenarioId);

        return Task.FromResult(call);
    }

    public Task<WatchCallEnrollment> CompleteMockCallAsync(string callId, CancellationToken ct = default)
    {
        if (!_calls.TryGetValue(callId, out var call) || !call.IsMockCall)
            throw new ArgumentException($"Mock call '{callId}' not found.");

        // Mark call as resolved
        _calls[callId] = call with { Status = WatchCallStatus.Resolved, ResolvedAt = DateTime.UtcNow, Resolution = "mock_completed" };

        // Update enrollment
        if (!_enrollments.TryGetValue(call.InitiatorUserId, out var enrollment))
            throw new InvalidOperationException($"No enrollment found for user '{call.InitiatorUserId}'.");

        var newMockCount = enrollment.MockCallsCompleted + 1;
        var updated = enrollment with
        {
            MockCallsCompleted = newMockCount,
            LastMockCallAt = DateTime.UtcNow,
            Status = newMockCount >= 1 ? WatchCallEnrollmentStatus.Active : enrollment.Status,
            TrainingCompletedAt = newMockCount >= 1 && enrollment.TrainingCompletedAt == null
                ? DateTime.UtcNow : enrollment.TrainingCompletedAt,
            LastUpdated = DateTime.UtcNow
        };

        _enrollments[updated.UserId] = updated;
        _logger.LogInformation("[WAL-WATCHCALL-MOCK] Completed mock call {CallId}. User {UserId} now has {Count} mock calls. Status: {Status}",
            callId, updated.UserId, newMockCount, updated.Status);

        return Task.FromResult(updated);
    }

    // ── Live Call Lifecycle ───────────────────────────────────────

    public Task<WatchCall> CreateCallAsync(WatchCall call, CancellationToken ct = default)
    {
        var c = call with
        {
            CallId = string.IsNullOrEmpty(call.CallId)
                ? $"call-{Guid.NewGuid():N}"[..14] : call.CallId,
            Status = WatchCallStatus.Requested,
            RequestedAt = DateTime.UtcNow,
            Participants = new List<WatchCallParticipant>(),
            NarrationTranscript = new List<TimestampedNarration>()
        };

        _calls[c.CallId] = c;
        _logger.LogInformation("[WAL-WATCHCALL-MOCK] Created live call {CallId} at ({Lat}, {Lng}), radius {Radius}m",
            c.CallId, c.Latitude, c.Longitude, c.RadiusMeters);

        return Task.FromResult(c);
    }

    public Task<WatchCall?> GetCallAsync(string callId, CancellationToken ct = default)
    {
        _calls.TryGetValue(callId, out var call);
        return Task.FromResult(call);
    }

    public Task<IReadOnlyList<WatchCall>> GetActiveCallsNearbyAsync(
        double latitude, double longitude, double radiusMeters, CancellationToken ct = default)
    {
        var activeCalls = _calls.Values
            .Where(c => c.Status is WatchCallStatus.Requested or WatchCallStatus.Connecting
                or WatchCallStatus.Active or WatchCallStatus.Narrating)
            .Where(c => HaversineMeters(latitude, longitude, c.Latitude, c.Longitude) <= radiusMeters)
            .OrderBy(c => c.RequestedAt)
            .ToList();

        return Task.FromResult<IReadOnlyList<WatchCall>>(activeCalls);
    }

    public Task<WatchCallParticipant> JoinCallAsync(string callId, string userId, CancellationToken ct = default)
    {
        if (!_calls.TryGetValue(callId, out var call))
            throw new ArgumentException($"Call '{callId}' not found.");

        var counter = Interlocked.Increment(ref _participantCounter);
        var participant = new WatchCallParticipant(
            UserId: userId,
            AnonymizedAlias: $"Watcher {counter}",
            PeerConnectionId: $"peer-{Guid.NewGuid():N}"[..14],
            JoinedAt: DateTime.UtcNow,
            LeftAt: null,
            IsVideoEnabled: true,
            IsAudioEnabled: false);  // Audio off by default for privacy

        var participants = call.Participants.ToList();
        participants.Add(participant);

        var newStatus = participants.Count(p => p.LeftAt == null) >= 2
            ? WatchCallStatus.Active
            : WatchCallStatus.Connecting;

        _calls[callId] = call with { Participants = participants, Status = newStatus, ConnectedAt = call.ConnectedAt ?? DateTime.UtcNow };

        _logger.LogInformation("[WAL-WATCHCALL-MOCK] User {UserId} joined call {CallId} as {Alias}. Participants: {Count}",
            userId, callId, participant.AnonymizedAlias, participants.Count);

        return Task.FromResult(participant);
    }

    public Task LeaveCallAsync(string callId, string userId, CancellationToken ct = default)
    {
        if (!_calls.TryGetValue(callId, out var call))
            return Task.CompletedTask;

        var participants = call.Participants.Select(p =>
            p.UserId == userId && p.LeftAt == null
                ? p with { LeftAt = DateTime.UtcNow }
                : p).ToList();

        var activeCount = participants.Count(p => p.LeftAt == null);
        var newStatus = activeCount == 0 ? WatchCallStatus.Resolved : call.Status;

        _calls[callId] = call with
        {
            Participants = participants,
            Status = newStatus,
            ResolvedAt = newStatus == WatchCallStatus.Resolved ? DateTime.UtcNow : call.ResolvedAt,
            Resolution = newStatus == WatchCallStatus.Resolved ? "all_left" : call.Resolution
        };

        _logger.LogInformation("[WAL-WATCHCALL-MOCK] User {UserId} left call {CallId}. Active: {Count}",
            userId, callId, activeCount);

        return Task.CompletedTask;
    }

    public Task AppendNarrationAsync(string callId, TimestampedNarration narration, CancellationToken ct = default)
    {
        if (!_calls.TryGetValue(callId, out var call))
            return Task.CompletedTask;

        var transcript = call.NarrationTranscript.ToList();
        transcript.Add(narration);
        _calls[callId] = call with { NarrationTranscript = transcript, Status = WatchCallStatus.Narrating };

        return Task.CompletedTask;
    }

    public Task<WatchCall> ResolveCallAsync(string callId, string resolution, CancellationToken ct = default)
    {
        if (!_calls.TryGetValue(callId, out var call))
            throw new ArgumentException($"Call '{callId}' not found.");

        var resolved = call with
        {
            Status = WatchCallStatus.Resolved,
            ResolvedAt = DateTime.UtcNow,
            Resolution = resolution
        };

        _calls[callId] = resolved;
        _logger.LogInformation("[WAL-WATCHCALL-MOCK] Resolved call {CallId}: {Resolution}", callId, resolution);
        return Task.FromResult(resolved);
    }

    public Task<WatchCall> EscalateCallAsync(string callId, string escalatedBy, string? reason = null, CancellationToken ct = default)
    {
        if (!_calls.TryGetValue(callId, out var call))
            throw new ArgumentException($"Call '{callId}' not found.");

        var requestId = $"req-{Guid.NewGuid():N}"[..14];
        var escalated = call with
        {
            Status = WatchCallStatus.Escalated,
            EscalatedAt = DateTime.UtcNow,
            EscalatedRequestId = requestId,
            Resolution = $"escalated_by_{escalatedBy}: {reason ?? "no reason given"}"
        };

        _calls[callId] = escalated;
        _logger.LogInformation("[WAL-WATCHCALL-MOCK] Escalated call {CallId} to request {RequestId} by {UserId}",
            callId, requestId, escalatedBy);
        return Task.FromResult(escalated);
    }

    // ── ICE Servers ──────────────────────────────────────────────

    public Task<IReadOnlyList<IceServerConfig>> GetIceServersAsync(CancellationToken ct = default)
    {
        // Dev: Google public STUN only. No TURN needed for localhost.
        var servers = new List<IceServerConfig>
        {
            new IceServerConfig(
                Urls: new List<string> { "stun:stun.l.google.com:19302", "stun:stun1.l.google.com:19302" },
                Username: null,
                Credential: null)
        };

        return Task.FromResult<IReadOnlyList<IceServerConfig>>(servers);
    }

    // ── Haversine helper ─────────────────────────────────────────

    private static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000; // Earth radius in meters
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
              * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}
