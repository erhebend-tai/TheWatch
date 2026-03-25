// MockVolunteeringAdapter — in-memory mock adapter for IVolunteeringPort.
// Fully functional with seeded volunteer profiles for development and testing.
//
// This is a PERMANENT first-class adapter. Every volunteering screen works
// against this mock before live adapters exist.
//
// Seeded data:
//   - 5 volunteers in various states (Active, PendingTraining, Suspended, Withdrawn)
//   - Schedules for designated responders
//   - Stats with realistic mock data
//
// WAL: MockVolunteeringAdapter created with in-memory ConcurrentDictionary storage.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Adapters.Mock;

public class MockVolunteeringAdapter : IVolunteeringPort
{
    private readonly ConcurrentDictionary<string, VolunteerProfile> _profiles = new();
    private readonly ConcurrentDictionary<string, List<AvailabilityScheduleEntry>> _schedules = new();
    private readonly ConcurrentDictionary<string, List<VerificationSubmission>> _verifications = new();
    private readonly ILogger<MockVolunteeringAdapter> _logger;

    public MockVolunteeringAdapter(ILogger<MockVolunteeringAdapter> logger)
    {
        _logger = logger;
        SeedMockData();
    }

    private void SeedMockData()
    {
        // Seed volunteers matching the mock participation adapter users
        var volunteers = new[]
        {
            new VolunteerProfile
            {
                ProfileId = "vol-001", UserId = "resp-001", DisplayName = "Marcus C.",
                Status = VolunteerStatus.Active, HasVehicle = true,
                IsCprCertified = true, IsFirstAidCertified = true, IsEmtCertified = true,
                Languages = new List<string> { "en", "zh" },
                AdditionalCertifications = new List<string> { "CERT", "Wilderness First Responder" },
                DateOfBirth = new DateTime(1988, 3, 15), MockCallsCompleted = 3,
                BackgroundCheckSubmitted = true, BackgroundCheckApproved = true,
                BackgroundCheckSubmittedAt = DateTime.UtcNow.AddDays(-30),
                AcceptedTermsAt = DateTime.UtcNow.AddDays(-45),
                EnrolledAt = DateTime.UtcNow.AddDays(-45),
                MaxResponseRadiusMeters = 8000
            },
            new VolunteerProfile
            {
                ProfileId = "vol-002", UserId = "resp-002", DisplayName = "Sarah W.",
                Status = VolunteerStatus.Active, HasVehicle = true,
                IsCprCertified = true, IsFirstAidCertified = true, IsEmtCertified = false,
                Languages = new List<string> { "en", "fr" },
                AdditionalCertifications = new List<string> { "Crisis Intervention Training" },
                DateOfBirth = new DateTime(1992, 7, 22), MockCallsCompleted = 2,
                BackgroundCheckSubmitted = true, BackgroundCheckApproved = true,
                BackgroundCheckSubmittedAt = DateTime.UtcNow.AddDays(-20),
                AcceptedTermsAt = DateTime.UtcNow.AddDays(-35),
                EnrolledAt = DateTime.UtcNow.AddDays(-35),
                MaxResponseRadiusMeters = 5000
            },
            new VolunteerProfile
            {
                ProfileId = "vol-003", UserId = "resp-003", DisplayName = "David K.",
                Status = VolunteerStatus.PendingTraining, HasVehicle = false,
                IsCprCertified = false, IsFirstAidCertified = false,
                Languages = new List<string> { "en", "ko" },
                DateOfBirth = new DateTime(1995, 11, 8), MockCallsCompleted = 0,
                AcceptedTermsAt = DateTime.UtcNow.AddDays(-5),
                EnrolledAt = DateTime.UtcNow.AddDays(-5),
                MaxResponseRadiusMeters = 3000
            },
            new VolunteerProfile
            {
                ProfileId = "vol-004", UserId = "resp-004", DisplayName = "Elena R.",
                Status = VolunteerStatus.Active, HasVehicle = true,
                IsCprCertified = true, IsFirstAidCertified = false,
                Languages = new List<string> { "en", "es" },
                DateOfBirth = new DateTime(1985, 1, 30), MockCallsCompleted = 1,
                BackgroundCheckSubmitted = false,
                AcceptedTermsAt = DateTime.UtcNow.AddDays(-60),
                EnrolledAt = DateTime.UtcNow.AddDays(-60),
                MaxResponseRadiusMeters = 6000
            },
            new VolunteerProfile
            {
                ProfileId = "vol-005", UserId = "resp-006", DisplayName = "Aisha P.",
                Status = VolunteerStatus.Active, HasVehicle = false,
                IsCprCertified = false, IsFirstAidCertified = true,
                Languages = new List<string> { "en", "hi", "ur" },
                DateOfBirth = new DateTime(1998, 9, 12), MockCallsCompleted = 2,
                AcceptedTermsAt = DateTime.UtcNow.AddDays(-25),
                EnrolledAt = DateTime.UtcNow.AddDays(-25),
                MaxResponseRadiusMeters = 4000
            },
        };

        foreach (var v in volunteers)
            _profiles[v.UserId] = v;

        // Seed schedules for designated responders
        _schedules["resp-001"] = new List<AvailabilityScheduleEntry>
        {
            new() { DayOfWeek = DayOfWeek.Monday, StartTime = new TimeOnly(18, 0), EndTime = new TimeOnly(22, 0) },
            new() { DayOfWeek = DayOfWeek.Wednesday, StartTime = new TimeOnly(18, 0), EndTime = new TimeOnly(22, 0) },
            new() { DayOfWeek = DayOfWeek.Saturday, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(20, 0) },
            new() { DayOfWeek = DayOfWeek.Sunday, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(20, 0) },
        };

        _schedules["resp-002"] = new List<AvailabilityScheduleEntry>
        {
            new() { DayOfWeek = DayOfWeek.Saturday, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(18, 0) },
            new() { DayOfWeek = DayOfWeek.Sunday, StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(18, 0) },
        };
    }

    // ── Enrollment ────────────────────────────────────────────────

    public Task<VolunteerProfile> EnrollAsync(VolunteerProfile profile, CancellationToken ct = default)
    {
        profile.Status = VolunteerStatus.PendingTraining;
        profile.EnrolledAt = DateTime.UtcNow;
        profile.LastUpdated = DateTime.UtcNow;

        if (string.IsNullOrEmpty(profile.ProfileId))
            profile.ProfileId = Guid.NewGuid().ToString();

        _profiles[profile.UserId] = profile;

        _logger.LogInformation(
            "[MockVolunteering] Enrolled {UserId} ({DisplayName}) — status: {Status}",
            profile.UserId, profile.DisplayName, profile.Status);

        return Task.FromResult(profile);
    }

    public Task<VolunteerProfile?> GetProfileAsync(string userId, CancellationToken ct = default)
        => Task.FromResult(_profiles.TryGetValue(userId, out var p) ? p : null);

    public Task<VolunteerProfile> UpdateProfileAsync(VolunteerProfile profile, CancellationToken ct = default)
    {
        profile.LastUpdated = DateTime.UtcNow;
        _profiles[profile.UserId] = profile;

        _logger.LogInformation(
            "[MockVolunteering] Updated profile for {UserId}: vehicle={HasVehicle}, CPR={CPR}, " +
            "FirstAid={FirstAid}, EMT={EMT}, languages={Languages}",
            profile.UserId, profile.HasVehicle, profile.IsCprCertified,
            profile.IsFirstAidCertified, profile.IsEmtCertified,
            string.Join(",", profile.Languages));

        return Task.FromResult(profile);
    }

    public Task<VolunteerProfile> ActivateAfterTrainingAsync(string userId, CancellationToken ct = default)
    {
        if (!_profiles.TryGetValue(userId, out var profile))
            throw new KeyNotFoundException($"Volunteer profile not found for user {userId}");

        if (profile.Status != VolunteerStatus.PendingTraining)
        {
            _logger.LogWarning("[MockVolunteering] Cannot activate {UserId} — current status: {Status}",
                userId, profile.Status);
            return Task.FromResult(profile);
        }

        // In mock mode, we trust the caller's assertion that training is complete.
        // The controller verifies this via IWatchCallPort before calling.
        profile.MockCallsCompleted = Math.Max(profile.MockCallsCompleted, 1);
        profile.Status = VolunteerStatus.Active;
        profile.LastUpdated = DateTime.UtcNow;
        _profiles[userId] = profile;

        _logger.LogInformation("[MockVolunteering] Activated {UserId} after training completion", userId);
        return Task.FromResult(profile);
    }

    public Task<bool> WithdrawAsync(string userId, CancellationToken ct = default)
    {
        if (!_profiles.TryGetValue(userId, out var profile))
            return Task.FromResult(false);

        profile.Status = VolunteerStatus.Withdrawn;
        profile.LastUpdated = DateTime.UtcNow;
        _profiles[userId] = profile;

        _logger.LogInformation("[MockVolunteering] {UserId} withdrew from volunteering program", userId);
        return Task.FromResult(true);
    }

    // ── Verification ──────────────────────────────────────────────

    public Task<VerificationSubmission> SubmitVerificationAsync(
        VerificationSubmission submission, CancellationToken ct = default)
    {
        submission.SubmittedAt = DateTime.UtcNow;
        // Mock: auto-approve
        submission.Approved = true;
        submission.ReviewedAt = DateTime.UtcNow;

        var list = _verifications.GetOrAdd(submission.UserId, _ => new List<VerificationSubmission>());
        lock (list) { list.Add(submission); }

        // Update profile
        if (_profiles.TryGetValue(submission.UserId, out var profile))
        {
            profile.BackgroundCheckSubmitted = true;
            profile.BackgroundCheckApproved = true;
            profile.BackgroundCheckSubmittedAt = submission.SubmittedAt;
            profile.LastUpdated = DateTime.UtcNow;
        }

        _logger.LogInformation(
            "[MockVolunteering] Verification submitted for {UserId}: type={Type}, auto-approved",
            submission.UserId, submission.DocumentType);

        return Task.FromResult(submission);
    }

    // ── Schedule ──────────────────────────────────────────────────

    public Task SetScheduleAsync(string userId, IReadOnlyList<AvailabilityScheduleEntry> schedule,
        CancellationToken ct = default)
    {
        _schedules[userId] = new List<AvailabilityScheduleEntry>(schedule);

        _logger.LogInformation(
            "[MockVolunteering] Set schedule for {UserId}: {Count} time windows",
            userId, schedule.Count);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AvailabilityScheduleEntry>> GetScheduleAsync(
        string userId, CancellationToken ct = default)
    {
        if (_schedules.TryGetValue(userId, out var schedule))
            return Task.FromResult<IReadOnlyList<AvailabilityScheduleEntry>>(schedule);

        return Task.FromResult<IReadOnlyList<AvailabilityScheduleEntry>>(
            Array.Empty<AvailabilityScheduleEntry>());
    }

    // ── Stats & Leaderboard ───────────────────────────────────────

    public Task<VolunteerStats> GetStatsAsync(string userId, CancellationToken ct = default)
    {
        // Generate realistic mock stats based on whether the user is enrolled
        if (!_profiles.TryGetValue(userId, out var profile))
        {
            return Task.FromResult(new VolunteerStats
            {
                UserId = userId,
                MemberSince = DateTime.UtcNow
            });
        }

        // Seed deterministic mock stats based on profile
        var rng = new Random(userId.GetHashCode());
        var totalResponses = profile.Status == VolunteerStatus.Active
            ? rng.Next(5, 50) : 0;
        var totalDispatched = totalResponses + rng.Next(3, 15);

        var stats = new VolunteerStats
        {
            UserId = userId,
            TotalResponses = totalResponses,
            TotalDispatched = totalDispatched,
            AcceptanceRate = totalDispatched > 0
                ? Math.Round((double)totalResponses / totalDispatched, 2) : 0,
            AverageResponseTimeMinutes = Math.Round(2.0 + rng.NextDouble() * 6.0, 1),
            CheckInsResponded = (int)(totalResponses * 0.5),
            NeighborhoodResponded = (int)(totalResponses * 0.3),
            CommunityResponded = (int)(totalResponses * 0.15),
            EvacuationResponded = (int)(totalResponses * 0.05),
            LastResponseAt = totalResponses > 0
                ? DateTime.UtcNow.AddHours(-rng.Next(1, 72)) : null,
            MemberSince = profile.EnrolledAt
        };

        return Task.FromResult(stats);
    }

    public Task<IReadOnlyList<LeaderboardEntry>> GetLeaderboardAsync(
        int top = 20, string? currentUserId = null, CancellationToken ct = default)
    {
        var activeProfiles = _profiles.Values
            .Where(p => p.Status == VolunteerStatus.Active)
            .OrderBy(p => p.UserId)
            .ToList();

        var entries = new List<LeaderboardEntry>();
        var rng = new Random(42); // Deterministic seed for consistent leaderboard

        for (int i = 0; i < activeProfiles.Count && i < top; i++)
        {
            var p = activeProfiles[i];
            var totalResponses = 50 - (i * 8) + rng.Next(0, 5);
            if (totalResponses < 1) totalResponses = 1;

            entries.Add(new LeaderboardEntry
            {
                Rank = i + 1,
                AnonymizedName = $"Volunteer #{(i + 1) * 7}",
                TotalResponses = totalResponses,
                AverageResponseTimeMinutes = Math.Round(2.5 + rng.NextDouble() * 4.0, 1),
                AcceptanceRate = Math.Round(0.7 + rng.NextDouble() * 0.3, 2),
                IsCurrentUser = p.UserId == currentUserId
            });
        }

        return Task.FromResult<IReadOnlyList<LeaderboardEntry>>(entries);
    }
}
