// =============================================================================
// Mock CCTV Adapter — permanent first-class in-memory implementation.
// =============================================================================
// Simulates the full CCTV lifecycle: feed registration, detection events,
// video upload analysis, alert-to-SOS escalation.
//
// Seeded data:
//   - 3 camera feeds for mock-user-001 (front door RTSP, driveway ONVIF, baby room upload-only)
//   - 5 recent detection events across those feeds
//   - 1 video upload (analyzed, 2 events found)
//
// WAL: All operations are in-memory only. No external network calls.
//      Feed URLs are stored in plain text (mock only — production encrypts AES-256-GCM).
// =============================================================================

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Adapters.Mock;

public class MockCCTVAdapter : ICCTVPort
{
    private readonly ConcurrentDictionary<string, CameraFeedRegistration> _feeds = new();
    private readonly ConcurrentDictionary<string, DetectionEvent> _events = new();
    private readonly ConcurrentDictionary<string, VideoUpload> _uploads = new();
    private readonly ILogger<MockCCTVAdapter> _logger;

    public MockCCTVAdapter(ILogger<MockCCTVAdapter> logger)
    {
        _logger = logger;
        SeedMockData();
    }

    private void SeedMockData()
    {
        var defaultDetectionConfig = new CameraDetectionConfig(
            MotionDetection: true,
            PersonDetection: true,
            VehicleDetection: true,
            FacePresenceDetection: false,
            PackageDetection: true,
            AnomalyDetection: true,
            AudioAnomalyDetection: true,
            SmokeFireDetection: true,
            PetAnimalDetection: true,
            LicensePlateDetection: false,
            AlertConfidenceThreshold: 0.7,
            AlertCooldown: TimeSpan.FromSeconds(30),
            SuppressWindows: null);

        var feeds = new[]
        {
            new CameraFeedRegistration(
                FeedId: "cam-001",
                UserId: "mock-user-001",
                FeedName: "Front Door Camera",
                Protocol: CameraProtocol.RTSP,
                FeedUrl: "rtsp://admin:****@192.168.1.50:554/h264Preview_01_main",
                CameraBrand: "Reolink",
                CameraModel: "Doorbell WiFi",
                Latitude: 30.2672,
                Longitude: -97.7431,
                InstallationZone: "Front Door",
                Status: CameraFeedStatus.Online,
                RegisteredAt: DateTime.UtcNow.AddDays(-45),
                LastFrameAt: DateTime.UtcNow.AddSeconds(-2),
                LastEventAt: DateTime.UtcNow.AddMinutes(-15),
                DetectionConfig: defaultDetectionConfig,
                PrivacyZones: new[]
                {
                    new PrivacyZone("Neighbor's Window", 0.7, 0.1, 0.25, 0.3)
                },
                AutoEscalationEnabled: true,
                AutoEscalationDelay: TimeSpan.FromSeconds(60),
                CloudRecordingEnabled: false),

            new CameraFeedRegistration(
                FeedId: "cam-002",
                UserId: "mock-user-001",
                FeedName: "Driveway Camera",
                Protocol: CameraProtocol.ONVIF,
                FeedUrl: "http://192.168.1.51:80/onvif/device_service",
                CameraBrand: "Hikvision",
                CameraModel: "DS-2CD2043G2-I",
                Latitude: 30.2673,
                Longitude: -97.7433,
                InstallationZone: "Driveway",
                Status: CameraFeedStatus.Online,
                RegisteredAt: DateTime.UtcNow.AddDays(-30),
                LastFrameAt: DateTime.UtcNow.AddSeconds(-1),
                LastEventAt: DateTime.UtcNow.AddHours(-2),
                DetectionConfig: defaultDetectionConfig with
                {
                    VehicleDetection = true,
                    LicensePlateDetection = true,
                    PersonDetection = true
                },
                PrivacyZones: null,
                AutoEscalationEnabled: false,
                AutoEscalationDelay: TimeSpan.FromSeconds(90),
                CloudRecordingEnabled: false),

            new CameraFeedRegistration(
                FeedId: "cam-003",
                UserId: "mock-user-001",
                FeedName: "Baby Room Monitor",
                Protocol: CameraProtocol.Upload,
                FeedUrl: null,
                CameraBrand: "Wyze",
                CameraModel: "Cam v3",
                Latitude: 30.2672,
                Longitude: -97.7431,
                InstallationZone: "Baby Room",
                Status: CameraFeedStatus.Online,
                RegisteredAt: DateTime.UtcNow.AddDays(-10),
                LastFrameAt: DateTime.UtcNow.AddMinutes(-1),
                LastEventAt: null,
                DetectionConfig: defaultDetectionConfig with
                {
                    PersonDetection = true,
                    AudioAnomalyDetection = true,
                    VehicleDetection = false,
                    PackageDetection = false,
                    LicensePlateDetection = false,
                    AnomalyDetection = false
                },
                PrivacyZones: null,
                AutoEscalationEnabled: false,
                AutoEscalationDelay: TimeSpan.FromMinutes(2),
                CloudRecordingEnabled: false),
        };

        foreach (var f in feeds) _feeds[f.FeedId] = f;

        // Seed detection events
        var events = new[]
        {
            new DetectionEvent(
                EventId: "det-001", FeedId: "cam-001", UserId: "mock-user-001",
                DetectionType: DetectionType.Person, Confidence: 0.92,
                Label: "Person — unknown, approaching front door",
                BoundingBox: new BoundingBox(0.3, 0.2, 0.15, 0.45),
                SnapshotBlobReference: "cctv/cam-001/det-001.jpg",
                ClipBlobReference: "cctv/cam-001/det-001-clip.mp4",
                VideoTimestamp: null,
                DetectedAt: DateTime.UtcNow.AddMinutes(-15),
                UserNotified: true, UserResponse: "dismissed",
                EscalatedRequestId: null, Metadata: null),

            new DetectionEvent(
                EventId: "det-002", FeedId: "cam-001", UserId: "mock-user-001",
                DetectionType: DetectionType.Package, Confidence: 0.87,
                Label: "Package delivered at front door",
                BoundingBox: new BoundingBox(0.4, 0.7, 0.12, 0.1),
                SnapshotBlobReference: "cctv/cam-001/det-002.jpg",
                ClipBlobReference: null,
                VideoTimestamp: null,
                DetectedAt: DateTime.UtcNow.AddHours(-3),
                UserNotified: true, UserResponse: "dismissed",
                EscalatedRequestId: null, Metadata: null),

            new DetectionEvent(
                EventId: "det-003", FeedId: "cam-002", UserId: "mock-user-001",
                DetectionType: DetectionType.Vehicle, Confidence: 0.85,
                Label: "Vehicle — dark sedan, unknown plate",
                BoundingBox: new BoundingBox(0.1, 0.4, 0.35, 0.25),
                SnapshotBlobReference: "cctv/cam-002/det-003.jpg",
                ClipBlobReference: "cctv/cam-002/det-003-clip.mp4",
                VideoTimestamp: null,
                DetectedAt: DateTime.UtcNow.AddHours(-2),
                UserNotified: true, UserResponse: "dismissed",
                EscalatedRequestId: null, Metadata: null),

            new DetectionEvent(
                EventId: "det-004", FeedId: "cam-002", UserId: "mock-user-001",
                DetectionType: DetectionType.Anomaly, Confidence: 0.78,
                Label: "Unusual activity — person loitering near driveway for 3+ minutes",
                BoundingBox: new BoundingBox(0.5, 0.3, 0.2, 0.5),
                SnapshotBlobReference: "cctv/cam-002/det-004.jpg",
                ClipBlobReference: "cctv/cam-002/det-004-clip.mp4",
                VideoTimestamp: null,
                DetectedAt: DateTime.UtcNow.AddHours(-1),
                UserNotified: true, UserResponse: null,  // Pending — user hasn't responded yet
                EscalatedRequestId: null, Metadata: null),

            new DetectionEvent(
                EventId: "det-005", FeedId: "cam-001", UserId: "mock-user-001",
                DetectionType: DetectionType.AudioAnomaly, Confidence: 0.73,
                Label: "Audio anomaly — glass break detected",
                BoundingBox: null,
                SnapshotBlobReference: "cctv/cam-001/det-005.jpg",
                ClipBlobReference: "cctv/cam-001/det-005-clip.mp4",
                VideoTimestamp: null,
                DetectedAt: DateTime.UtcNow.AddMinutes(-5),
                UserNotified: true, UserResponse: "escalated",
                EscalatedRequestId: "resp-glass-break-001",
                Metadata: new Dictionary<string, string>
                {
                    ["audio_class"] = "glass_break",
                    ["audio_confidence"] = "0.81",
                    ["duration_ms"] = "1200"
                }),
        };

        foreach (var e in events) _events[e.EventId] = e;

        // Seed a video upload
        _uploads["upload-001"] = new VideoUpload(
            UploadId: "upload-001",
            UserId: "mock-user-001",
            RequestId: null,
            FileName: "ring_doorbell_2026-03-23.mp4",
            MimeType: "video/mp4",
            FileSizeBytes: 15_200_000,
            Duration: TimeSpan.FromSeconds(32),
            ContentHash: "a1b2c3d4e5f6789012345678abcdef01fedcba9876543210",
            BlobReference: "uploads/mock-user-001/upload-001.mp4",
            ThumbnailBlobReference: "uploads/mock-user-001/upload-001-thumb.jpg",
            Status: VideoAnalysisStatus.Complete,
            UploadedAt: DateTime.UtcNow.AddHours(-6),
            AnalysisStartedAt: DateTime.UtcNow.AddHours(-6).AddSeconds(5),
            AnalysisCompletedAt: DateTime.UtcNow.AddHours(-6).AddSeconds(45),
            EventsFound: 2,
            ErrorMessage: null);
    }

    // ═══════════════════════════════════════════════════════════════
    // Camera Feed Management
    // ═══════════════════════════════════════════════════════════════

    public Task<CameraFeedRegistration> RegisterFeedAsync(
        CameraFeedRegistration feed, CancellationToken ct = default)
    {
        var feedId = string.IsNullOrEmpty(feed.FeedId)
            ? $"cam-{Guid.NewGuid():N}"[..12]
            : feed.FeedId;

        var registered = feed with
        {
            FeedId = feedId,
            Status = CameraFeedStatus.Pending,
            RegisteredAt = DateTime.UtcNow
        };

        _feeds[feedId] = registered;

        _logger.LogInformation(
            "[MockCCTV] Registered feed: {FeedId} ({FeedName}) for user {UserId}, protocol={Protocol}",
            feedId, feed.FeedName, feed.UserId, feed.Protocol);

        return Task.FromResult(registered);
    }

    public Task<CameraFeedRegistration> UpdateFeedAsync(
        CameraFeedRegistration feed, CancellationToken ct = default)
    {
        if (!_feeds.ContainsKey(feed.FeedId))
            throw new KeyNotFoundException($"Feed {feed.FeedId} not found");

        _feeds[feed.FeedId] = feed;
        _logger.LogInformation("[MockCCTV] Updated feed: {FeedId} ({FeedName})", feed.FeedId, feed.FeedName);
        return Task.FromResult(feed);
    }

    public Task<bool> RemoveFeedAsync(string feedId, CancellationToken ct = default)
    {
        var removed = _feeds.TryRemove(feedId, out _);
        if (removed)
        {
            // Also remove associated events
            var eventIds = _events.Values.Where(e => e.FeedId == feedId).Select(e => e.EventId).ToList();
            foreach (var eid in eventIds) _events.TryRemove(eid, out _);
        }
        _logger.LogInformation("[MockCCTV] Remove feed {FeedId}: {Result}", feedId, removed ? "REMOVED" : "NOT_FOUND");
        return Task.FromResult(removed);
    }

    public Task<CameraFeedRegistration?> GetFeedAsync(string feedId, CancellationToken ct = default)
    {
        _feeds.TryGetValue(feedId, out var feed);
        return Task.FromResult(feed);
    }

    public Task<IReadOnlyList<CameraFeedRegistration>> GetUserFeedsAsync(
        string userId, CancellationToken ct = default)
    {
        var feeds = _feeds.Values
            .Where(f => f.UserId == userId)
            .OrderBy(f => f.InstallationZone)
            .ThenBy(f => f.FeedName)
            .ToList();
        return Task.FromResult<IReadOnlyList<CameraFeedRegistration>>(feeds);
    }

    public Task<CameraFeedRegistration> PauseFeedAsync(string feedId, CancellationToken ct = default)
    {
        if (!_feeds.TryGetValue(feedId, out var feed))
            throw new KeyNotFoundException($"Feed {feedId} not found");

        var paused = feed with { Status = CameraFeedStatus.Paused };
        _feeds[feedId] = paused;
        _logger.LogInformation("[MockCCTV] Paused feed: {FeedId} ({FeedName})", feedId, feed.FeedName);
        return Task.FromResult(paused);
    }

    public Task<CameraFeedRegistration> ResumeFeedAsync(string feedId, CancellationToken ct = default)
    {
        if (!_feeds.TryGetValue(feedId, out var feed))
            throw new KeyNotFoundException($"Feed {feedId} not found");

        var resumed = feed with { Status = CameraFeedStatus.Online };
        _feeds[feedId] = resumed;
        _logger.LogInformation("[MockCCTV] Resumed feed: {FeedId} ({FeedName})", feedId, feed.FeedName);
        return Task.FromResult(resumed);
    }

    public Task<CameraConnectivityResult> TestConnectivityAsync(
        string feedUrl, CameraProtocol protocol, CancellationToken ct = default)
    {
        _logger.LogInformation("[MockCCTV] Testing connectivity: {Protocol} → {Url}", protocol, "[REDACTED]");

        // Mock: always succeed with simulated values
        return Task.FromResult(new CameraConnectivityResult(
            Success: true,
            ErrorMessage: null,
            CameraBrand: "Reolink",
            CameraModel: "RLC-810A",
            Resolution: "3840x2160",
            LatencyMs: 45.2));
    }

    // ═══════════════════════════════════════════════════════════════
    // Detection Events
    // ═══════════════════════════════════════════════════════════════

    public Task<DetectionEvent> IngestDetectionEventAsync(
        DetectionEvent detectionEvent, CancellationToken ct = default)
    {
        var eventId = string.IsNullOrEmpty(detectionEvent.EventId)
            ? $"det-{Guid.NewGuid():N}"[..12]
            : detectionEvent.EventId;

        var stored = detectionEvent with
        {
            EventId = eventId,
            DetectedAt = DateTime.UtcNow,
            UserNotified = detectionEvent.Confidence >= 0.7  // Notify if above threshold
        };

        _events[eventId] = stored;

        // Update feed's LastEventAt
        if (_feeds.TryGetValue(detectionEvent.FeedId, out var feed))
        {
            _feeds[detectionEvent.FeedId] = feed with { LastEventAt = DateTime.UtcNow };
        }

        _logger.LogInformation(
            "[MockCCTV] Detection event: {EventId} on {FeedId} — {Type} ({Confidence:P0}): {Label}",
            eventId, detectionEvent.FeedId, detectionEvent.DetectionType,
            detectionEvent.Confidence, detectionEvent.Label);

        return Task.FromResult(stored);
    }

    public Task<IReadOnlyList<DetectionEvent>> GetEventsForFeedAsync(
        string feedId, int limit = 50, DateTime? since = null,
        DetectionType? typeFilter = null, CancellationToken ct = default)
    {
        var query = _events.Values.Where(e => e.FeedId == feedId);
        if (since.HasValue) query = query.Where(e => e.DetectedAt > since.Value);
        if (typeFilter.HasValue) query = query.Where(e => e.DetectionType == typeFilter.Value);

        var events = query.OrderByDescending(e => e.DetectedAt).Take(limit).ToList();
        return Task.FromResult<IReadOnlyList<DetectionEvent>>(events);
    }

    public Task<IReadOnlyList<DetectionEvent>> GetEventsForUserAsync(
        string userId, int limit = 50, DateTime? since = null,
        CancellationToken ct = default)
    {
        var query = _events.Values.Where(e => e.UserId == userId);
        if (since.HasValue) query = query.Where(e => e.DetectedAt > since.Value);

        var events = query.OrderByDescending(e => e.DetectedAt).Take(limit).ToList();
        return Task.FromResult<IReadOnlyList<DetectionEvent>>(events);
    }

    public Task<DetectionEvent> RespondToEventAsync(
        string eventId, string userResponse, CancellationToken ct = default)
    {
        if (!_events.TryGetValue(eventId, out var evt))
            throw new KeyNotFoundException($"Event {eventId} not found");

        var updated = evt with { UserResponse = userResponse };

        // If escalated, simulate SOS creation
        if (userResponse == "escalated")
        {
            var requestId = $"resp-cctv-{Guid.NewGuid():N}"[..20];
            updated = updated with { EscalatedRequestId = requestId };

            _logger.LogWarning(
                "[MockCCTV] ESCALATED: Event {EventId} → SOS {RequestId}. " +
                "Detection: {Type} on {FeedId}, confidence={Confidence:P0}",
                eventId, requestId, evt.DetectionType, evt.FeedId, evt.Confidence);
        }
        else
        {
            _logger.LogInformation("[MockCCTV] Event {EventId} responded: {Response}", eventId, userResponse);
        }

        _events[eventId] = updated;
        return Task.FromResult(updated);
    }

    // ═══════════════════════════════════════════════════════════════
    // Video Upload
    // ═══════════════════════════════════════════════════════════════

    public Task<VideoUpload> SubmitVideoForAnalysisAsync(
        VideoUpload upload, CancellationToken ct = default)
    {
        var uploadId = string.IsNullOrEmpty(upload.UploadId)
            ? $"upload-{Guid.NewGuid():N}"[..16]
            : upload.UploadId;

        var stored = upload with
        {
            UploadId = uploadId,
            Status = VideoAnalysisStatus.Queued,
            UploadedAt = DateTime.UtcNow
        };

        _uploads[uploadId] = stored;

        _logger.LogInformation(
            "[MockCCTV] Video submitted: {UploadId} ({FileName}, {Size} bytes) for user {UserId}",
            uploadId, upload.FileName, upload.FileSizeBytes, upload.UserId);

        // Simulate async analysis completing immediately (mock only)
        var analyzed = stored with
        {
            Status = VideoAnalysisStatus.Complete,
            AnalysisStartedAt = DateTime.UtcNow,
            AnalysisCompletedAt = DateTime.UtcNow.AddSeconds(2),
            EventsFound = 1
        };
        _uploads[uploadId] = analyzed;

        // Create a mock detection event from the video
        var mockEvent = new DetectionEvent(
            EventId: $"det-upload-{uploadId}",
            FeedId: uploadId,  // Use uploadId as feed reference for uploaded videos
            UserId: upload.UserId,
            DetectionType: DetectionType.Person,
            Confidence: 0.88,
            Label: "Person detected in uploaded video",
            BoundingBox: new BoundingBox(0.25, 0.15, 0.2, 0.5),
            SnapshotBlobReference: $"uploads/{upload.UserId}/{uploadId}-frame.jpg",
            ClipBlobReference: null,
            VideoTimestamp: TimeSpan.FromSeconds(12),
            DetectedAt: DateTime.UtcNow,
            UserNotified: false,
            UserResponse: null,
            EscalatedRequestId: null,
            Metadata: new Dictionary<string, string>
            {
                ["source"] = "video_upload",
                ["upload_id"] = uploadId
            });
        _events[mockEvent.EventId] = mockEvent;

        return Task.FromResult(analyzed);
    }

    public Task<VideoUpload?> GetVideoUploadAsync(string uploadId, CancellationToken ct = default)
    {
        _uploads.TryGetValue(uploadId, out var upload);
        return Task.FromResult(upload);
    }

    public Task<IReadOnlyList<VideoUpload>> GetUserUploadsAsync(
        string userId, CancellationToken ct = default)
    {
        var uploads = _uploads.Values
            .Where(u => u.UserId == userId)
            .OrderByDescending(u => u.UploadedAt)
            .ToList();
        return Task.FromResult<IReadOnlyList<VideoUpload>>(uploads);
    }

    public Task<IReadOnlyList<DetectionEvent>> GetEventsForUploadAsync(
        string uploadId, CancellationToken ct = default)
    {
        // Events from uploaded videos use uploadId as the FeedId
        var events = _events.Values
            .Where(e => e.FeedId == uploadId || (e.Metadata?.TryGetValue("upload_id", out var uid) == true && uid == uploadId))
            .OrderBy(e => e.VideoTimestamp ?? TimeSpan.Zero)
            .ToList();
        return Task.FromResult<IReadOnlyList<DetectionEvent>>(events);
    }
}
