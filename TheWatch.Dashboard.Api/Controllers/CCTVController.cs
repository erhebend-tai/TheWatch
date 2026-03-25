// =============================================================================
// CCTVController — REST endpoints for personal CCTV / security camera management.
// =============================================================================
// Allows users to register, manage, and monitor their personal security cameras
// through TheWatch. Camera feeds are modeled but not actually connected in this
// controller (RTSP/ONVIF are future adapter work). The controller manages the
// camera lifecycle; the mock adapter simulates realistic responses.
//
// Endpoints:
//   POST   /api/cctv/register                — Register a new camera (RTSP URL, location, owner)
//   DELETE /api/cctv/{cameraId}              — Unregister a camera and remove associated events
//   GET    /api/cctv/user/{userId}           — List a user's registered cameras
//   GET    /api/cctv/{cameraId}/status       — Get camera health / connection status
//   POST   /api/cctv/{cameraId}/stream/start — Start monitoring a camera stream (analysis pipeline)
//   POST   /api/cctv/{cameraId}/stream/stop  — Stop monitoring / pause analysis
//   GET    /api/cctv/nearby                  — Get cameras near a lat/lng (for incident response)
//   PUT    /api/cctv/{cameraId}              — Update camera settings (detection config, privacy zones)
//   POST   /api/cctv/{cameraId}/test         — Test camera connectivity
//   GET    /api/cctv/{cameraId}/events       — Get detection events for a camera
//   GET    /api/cctv/events/user/{userId}    — Get detection events across all user's cameras
//   POST   /api/cctv/{cameraId}/events/{eventId}/respond — User responds to a detection alert
//   POST   /api/cctv/upload                  — Submit a video for analysis
//   GET    /api/cctv/upload/{uploadId}       — Get video upload status
//   GET    /api/cctv/upload/{uploadId}/events — Get events found in an analyzed video
//
// WAL: Thin controller — validation + delegation to ICCTVPort.
//      Camera feed URLs contain credentials and are NEVER logged.
//      All operations delegate to the adapter pattern; mock adapter returns simulated data.
// =============================================================================

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using TheWatch.Dashboard.Api.Hubs;
using TheWatch.Dashboard.Api.Services;
using TheWatch.Shared.Domain.Ports;

namespace TheWatch.Dashboard.Api.Controllers;

[ApiController]
[Route("api/cctv")]
public class CCTVController : ControllerBase
{
    private readonly ICCTVPort _cctvPort;
    private readonly IResponseCoordinationService _coordinationService;
    private readonly IHubContext<DashboardHub> _hubContext;
    private readonly ILogger<CCTVController> _logger;

    public CCTVController(
        ICCTVPort cctvPort,
        IResponseCoordinationService coordinationService,
        IHubContext<DashboardHub> hubContext,
        ILogger<CCTVController> logger)
    {
        _cctvPort = cctvPort;
        _coordinationService = coordinationService;
        _hubContext = hubContext;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────
    // Camera Registration & Management
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Register a new camera feed. Creates the feed in Pending status.
    /// The adapter validates the connection and transitions to Online/Offline
    /// when stream/start is called.
    ///
    /// Example request body:
    ///   {
    ///     "userId": "user-456",
    ///     "feedName": "Front Door Camera",
    ///     "protocol": "RTSP",
    ///     "feedUrl": "rtsp://admin:pass@192.168.1.50:554/h264Preview_01_main",
    ///     "latitude": 30.2672,
    ///     "longitude": -97.7431,
    ///     "installationZone": "Front Door",
    ///     "cameraBrand": "Reolink",
    ///     "cameraModel": "Doorbell WiFi"
    ///   }
    /// </summary>
    [HttpPost("register")]
    public async Task<IActionResult> RegisterCamera(
        [FromBody] RegisterCameraRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
            return BadRequest(new { error = "UserId is required" });
        if (string.IsNullOrWhiteSpace(request.FeedName))
            return BadRequest(new { error = "FeedName is required" });

        // Parse protocol — default to RTSP if not specified
        if (!Enum.TryParse<CameraProtocol>(request.Protocol ?? "RTSP", ignoreCase: true, out var protocol))
            return BadRequest(new { error = $"Invalid protocol: {request.Protocol}. Valid values: RTSP, ONVIF, HLS, RTMP, WebRTC, CloudAPI, Upload" });

        var defaultDetectionConfig = new CameraDetectionConfig(
            MotionDetection: request.MotionDetection ?? true,
            PersonDetection: request.PersonDetection ?? true,
            VehicleDetection: request.VehicleDetection ?? false,
            FacePresenceDetection: false,   // Always off by default — privacy-first
            PackageDetection: request.PackageDetection ?? false,
            AnomalyDetection: request.AnomalyDetection ?? true,
            AudioAnomalyDetection: request.AudioAnomalyDetection ?? true,
            SmokeFireDetection: request.SmokeFireDetection ?? true,
            PetAnimalDetection: request.PetAnimalDetection ?? true,
            LicensePlateDetection: false,   // Opt-in only — privacy implications
            AlertConfidenceThreshold: request.AlertConfidenceThreshold ?? 0.7,
            AlertCooldown: TimeSpan.FromSeconds(request.AlertCooldownSeconds ?? 30),
            SuppressWindows: null);

        var feed = new CameraFeedRegistration(
            FeedId: "",
            UserId: request.UserId,
            FeedName: request.FeedName,
            Protocol: protocol,
            FeedUrl: request.FeedUrl,
            CameraBrand: request.CameraBrand,
            CameraModel: request.CameraModel,
            Latitude: request.Latitude,
            Longitude: request.Longitude,
            InstallationZone: request.InstallationZone,
            Status: CameraFeedStatus.Pending,
            RegisteredAt: DateTime.UtcNow,
            LastFrameAt: null,
            LastEventAt: null,
            DetectionConfig: defaultDetectionConfig,
            PrivacyZones: null,
            AutoEscalationEnabled: request.AutoEscalationEnabled ?? false,
            AutoEscalationDelay: TimeSpan.FromSeconds(request.AutoEscalationDelaySeconds ?? 60),
            CloudRecordingEnabled: false);

        var registered = await _cctvPort.RegisterFeedAsync(feed, ct);

        // NOTE: Feed URL intentionally NOT logged — contains credentials
        _logger.LogInformation(
            "Camera registered: {FeedId} ({FeedName}) for user {UserId}, protocol={Protocol}, zone={Zone}",
            registered.FeedId, registered.FeedName, registered.UserId, registered.Protocol, registered.InstallationZone);

        // Broadcast to dashboard
        await _hubContext.Clients.All.SendAsync("CameraRegistered", new
        {
            registered.FeedId,
            registered.UserId,
            registered.FeedName,
            Protocol = registered.Protocol.ToString(),
            registered.InstallationZone,
            registered.Latitude,
            registered.Longitude,
            Status = registered.Status.ToString(),
            registered.RegisteredAt
        }, ct);

        return Created($"/api/cctv/{registered.FeedId}/status", new
        {
            registered.FeedId,
            registered.FeedName,
            Protocol = registered.Protocol.ToString(),
            Status = registered.Status.ToString(),
            registered.InstallationZone,
            registered.Latitude,
            registered.Longitude,
            registered.RegisteredAt,
            Message = "Camera registered. Call POST /api/cctv/{feedId}/stream/start to begin monitoring."
        });
    }

    /// <summary>
    /// Unregister a camera and remove all associated detection events.
    /// </summary>
    [HttpDelete("{cameraId}")]
    public async Task<IActionResult> UnregisterCamera(string cameraId, CancellationToken ct)
    {
        var removed = await _cctvPort.RemoveFeedAsync(cameraId, ct);
        if (!removed)
            return NotFound(new { error = $"Camera {cameraId} not found" });

        _logger.LogInformation("Camera unregistered: {CameraId}", cameraId);

        await _hubContext.Clients.All.SendAsync("CameraUnregistered", new
        {
            CameraId = cameraId,
            RemovedAt = DateTime.UtcNow
        }, ct);

        return Ok(new { cameraId, removed = true, message = "Camera and all associated events removed." });
    }

    /// <summary>
    /// List all cameras registered by a user.
    /// Returns feed metadata, status, and detection configuration for each camera.
    /// </summary>
    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetUserCameras(string userId, CancellationToken ct)
    {
        var feeds = await _cctvPort.GetUserFeedsAsync(userId, ct);
        return Ok(feeds.Select(f => new
        {
            f.FeedId,
            f.FeedName,
            Protocol = f.Protocol.ToString(),
            f.CameraBrand,
            f.CameraModel,
            f.Latitude,
            f.Longitude,
            f.InstallationZone,
            Status = f.Status.ToString(),
            f.RegisteredAt,
            f.LastFrameAt,
            f.LastEventAt,
            f.AutoEscalationEnabled,
            AutoEscalationDelaySeconds = f.AutoEscalationDelay.TotalSeconds,
            DetectionConfig = new
            {
                f.DetectionConfig.MotionDetection,
                f.DetectionConfig.PersonDetection,
                f.DetectionConfig.VehicleDetection,
                f.DetectionConfig.AnomalyDetection,
                f.DetectionConfig.AudioAnomalyDetection,
                f.DetectionConfig.SmokeFireDetection,
                f.DetectionConfig.PetAnimalDetection,
                f.DetectionConfig.AlertConfidenceThreshold,
                AlertCooldownSeconds = f.DetectionConfig.AlertCooldown.TotalSeconds
            }
        }));
    }

    /// <summary>
    /// Get the health/connection status of a specific camera.
    /// Returns current status, last frame time, last event time, and detection config.
    /// </summary>
    [HttpGet("{cameraId}/status")]
    public async Task<IActionResult> GetCameraStatus(string cameraId, CancellationToken ct)
    {
        var feed = await _cctvPort.GetFeedAsync(cameraId, ct);
        if (feed is null)
            return NotFound(new { error = $"Camera {cameraId} not found" });

        return Ok(new
        {
            feed.FeedId,
            feed.FeedName,
            Protocol = feed.Protocol.ToString(),
            feed.CameraBrand,
            feed.CameraModel,
            Status = feed.Status.ToString(),
            feed.Latitude,
            feed.Longitude,
            feed.InstallationZone,
            feed.LastFrameAt,
            feed.LastEventAt,
            feed.AutoEscalationEnabled,
            AutoEscalationDelaySeconds = feed.AutoEscalationDelay.TotalSeconds,
            feed.CloudRecordingEnabled,
            PrivacyZoneCount = feed.PrivacyZones?.Count ?? 0
        });
    }

    /// <summary>
    /// Start monitoring a camera stream. Transitions the feed from Pending/Paused/Offline
    /// to Online (or to Offline/AuthFailed if the connection fails).
    ///
    /// In mock mode: always succeeds and sets status to Online.
    /// In production: validates RTSP/ONVIF connectivity, starts analysis pipeline worker.
    /// </summary>
    [HttpPost("{cameraId}/stream/start")]
    public async Task<IActionResult> StartStream(string cameraId, CancellationToken ct)
    {
        var feed = await _cctvPort.GetFeedAsync(cameraId, ct);
        if (feed is null)
            return NotFound(new { error = $"Camera {cameraId} not found" });

        if (feed.Status == CameraFeedStatus.Online)
            return Ok(new { feed.FeedId, Status = "Online", message = "Stream is already active." });

        var resumed = await _cctvPort.ResumeFeedAsync(cameraId, ct);

        _logger.LogInformation("Stream started: {CameraId} ({FeedName}) — status={Status}",
            cameraId, resumed.FeedName, resumed.Status);

        await _hubContext.Clients.All.SendAsync("CameraStreamStarted", new
        {
            resumed.FeedId,
            resumed.FeedName,
            Status = resumed.Status.ToString(),
            StartedAt = DateTime.UtcNow
        }, ct);

        return Ok(new
        {
            resumed.FeedId,
            Status = resumed.Status.ToString(),
            message = "Monitoring stream started. Detection events will be generated as activity is detected."
        });
    }

    /// <summary>
    /// Stop monitoring a camera stream. Pauses analysis — camera stays registered
    /// but detection events are suppressed until resumed.
    /// </summary>
    [HttpPost("{cameraId}/stream/stop")]
    public async Task<IActionResult> StopStream(string cameraId, CancellationToken ct)
    {
        var feed = await _cctvPort.GetFeedAsync(cameraId, ct);
        if (feed is null)
            return NotFound(new { error = $"Camera {cameraId} not found" });

        if (feed.Status == CameraFeedStatus.Paused)
            return Ok(new { feed.FeedId, Status = "Paused", message = "Stream is already paused." });

        var paused = await _cctvPort.PauseFeedAsync(cameraId, ct);

        _logger.LogInformation("Stream stopped: {CameraId} ({FeedName}) — status={Status}",
            cameraId, paused.FeedName, paused.Status);

        await _hubContext.Clients.All.SendAsync("CameraStreamStopped", new
        {
            paused.FeedId,
            paused.FeedName,
            Status = paused.Status.ToString(),
            StoppedAt = DateTime.UtcNow
        }, ct);

        return Ok(new
        {
            paused.FeedId,
            Status = paused.Status.ToString(),
            message = "Monitoring stream paused. No detection events will be generated until resumed."
        });
    }

    /// <summary>
    /// Get cameras near a geographic location. Used during incident response to find
    /// nearby cameras that might provide visual context for an active SOS.
    ///
    /// Query parameters:
    ///   latitude, longitude — center point
    ///   radiusMeters — search radius (default: 500m)
    ///   limit — max results (default: 20)
    ///
    /// Note: Only returns cameras whose owners have opted into community sharing
    /// (future feature — currently returns all cameras within radius).
    /// </summary>
    [HttpGet("nearby")]
    public async Task<IActionResult> GetNearbyCameras(
        [FromQuery] double latitude,
        [FromQuery] double longitude,
        [FromQuery] double radiusMeters = 500,
        [FromQuery] int limit = 20,
        CancellationToken ct = default)
    {
        if (latitude < -90 || latitude > 90)
            return BadRequest(new { error = "Latitude must be between -90 and 90" });
        if (longitude < -180 || longitude > 180)
            return BadRequest(new { error = "Longitude must be between -180 and 180" });

        // Get all feeds and filter by distance
        // In production, this would use PostGIS/H3 spatial queries for efficiency.
        // Mock implementation: iterate all feeds and compute Haversine distance.
        var allFeeds = new List<CameraFeedRegistration>();

        // Collect all feeds from all users — in production this is a spatial index query
        // For now, we use a known set of mock user IDs and iterate
        // The mock adapter seeds data for "mock-user-001"
        var feeds001 = await _cctvPort.GetUserFeedsAsync("mock-user-001", ct);
        allFeeds.AddRange(feeds001);

        var nearbyCameras = allFeeds
            .Where(f => f.Latitude.HasValue && f.Longitude.HasValue && f.Status == CameraFeedStatus.Online)
            .Select(f => new
            {
                Feed = f,
                DistanceMeters = HaversineDistance(latitude, longitude, f.Latitude!.Value, f.Longitude!.Value)
            })
            .Where(x => x.DistanceMeters <= radiusMeters)
            .OrderBy(x => x.DistanceMeters)
            .Take(limit)
            .Select(x => new
            {
                x.Feed.FeedId,
                x.Feed.FeedName,
                x.Feed.UserId,
                Protocol = x.Feed.Protocol.ToString(),
                x.Feed.Latitude,
                x.Feed.Longitude,
                x.Feed.InstallationZone,
                Status = x.Feed.Status.ToString(),
                DistanceMeters = Math.Round(x.DistanceMeters, 1)
            })
            .ToList();

        return Ok(new
        {
            Center = new { latitude, longitude },
            radiusMeters,
            Count = nearbyCameras.Count,
            Cameras = nearbyCameras
        });
    }

    /// <summary>
    /// Update a camera's settings — detection configuration, privacy zones, escalation settings.
    /// </summary>
    [HttpPut("{cameraId}")]
    public async Task<IActionResult> UpdateCamera(
        string cameraId,
        [FromBody] UpdateCameraRequest request,
        CancellationToken ct)
    {
        var existing = await _cctvPort.GetFeedAsync(cameraId, ct);
        if (existing is null)
            return NotFound(new { error = $"Camera {cameraId} not found" });

        var updated = existing with
        {
            FeedName = request.FeedName ?? existing.FeedName,
            InstallationZone = request.InstallationZone ?? existing.InstallationZone,
            AutoEscalationEnabled = request.AutoEscalationEnabled ?? existing.AutoEscalationEnabled,
            AutoEscalationDelay = request.AutoEscalationDelaySeconds.HasValue
                ? TimeSpan.FromSeconds(request.AutoEscalationDelaySeconds.Value)
                : existing.AutoEscalationDelay,
            DetectionConfig = request.HasDetectionConfigChanges()
                ? existing.DetectionConfig with
                {
                    MotionDetection = request.MotionDetection ?? existing.DetectionConfig.MotionDetection,
                    PersonDetection = request.PersonDetection ?? existing.DetectionConfig.PersonDetection,
                    VehicleDetection = request.VehicleDetection ?? existing.DetectionConfig.VehicleDetection,
                    AnomalyDetection = request.AnomalyDetection ?? existing.DetectionConfig.AnomalyDetection,
                    AudioAnomalyDetection = request.AudioAnomalyDetection ?? existing.DetectionConfig.AudioAnomalyDetection,
                    SmokeFireDetection = request.SmokeFireDetection ?? existing.DetectionConfig.SmokeFireDetection,
                    PetAnimalDetection = request.PetAnimalDetection ?? existing.DetectionConfig.PetAnimalDetection,
                    AlertConfidenceThreshold = request.AlertConfidenceThreshold ?? existing.DetectionConfig.AlertConfidenceThreshold,
                    AlertCooldown = request.AlertCooldownSeconds.HasValue
                        ? TimeSpan.FromSeconds(request.AlertCooldownSeconds.Value)
                        : existing.DetectionConfig.AlertCooldown
                }
                : existing.DetectionConfig
        };

        var result = await _cctvPort.UpdateFeedAsync(updated, ct);

        _logger.LogInformation("Camera updated: {CameraId} ({FeedName})", cameraId, result.FeedName);

        return Ok(new
        {
            result.FeedId,
            result.FeedName,
            Status = result.Status.ToString(),
            result.InstallationZone,
            result.AutoEscalationEnabled,
            AutoEscalationDelaySeconds = result.AutoEscalationDelay.TotalSeconds,
            message = "Camera settings updated."
        });
    }

    /// <summary>
    /// Test camera connectivity. Validates the URL and credentials without
    /// fully starting the analysis pipeline.
    /// </summary>
    [HttpPost("{cameraId}/test")]
    public async Task<IActionResult> TestConnectivity(string cameraId, CancellationToken ct)
    {
        var feed = await _cctvPort.GetFeedAsync(cameraId, ct);
        if (feed is null)
            return NotFound(new { error = $"Camera {cameraId} not found" });

        if (string.IsNullOrEmpty(feed.FeedUrl))
            return BadRequest(new { error = "Camera has no feed URL (upload-only cameras cannot be tested)" });

        var result = await _cctvPort.TestConnectivityAsync(feed.FeedUrl, feed.Protocol, ct);

        return Ok(new
        {
            feed.FeedId,
            result.Success,
            result.ErrorMessage,
            DetectedBrand = result.CameraBrand,
            DetectedModel = result.CameraModel,
            result.Resolution,
            result.LatencyMs
        });
    }

    // ─────────────────────────────────────────────────────────────
    // Detection Events
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Get detection events for a specific camera feed.
    /// Optionally filter by detection type and time range.
    /// </summary>
    [HttpGet("{cameraId}/events")]
    public async Task<IActionResult> GetCameraEvents(
        string cameraId,
        [FromQuery] int limit = 50,
        [FromQuery] DateTime? since = null,
        [FromQuery] string? type = null,
        CancellationToken ct = default)
    {
        var feed = await _cctvPort.GetFeedAsync(cameraId, ct);
        if (feed is null)
            return NotFound(new { error = $"Camera {cameraId} not found" });

        DetectionType? typeFilter = null;
        if (!string.IsNullOrEmpty(type))
        {
            if (!Enum.TryParse<DetectionType>(type, ignoreCase: true, out var parsed))
                return BadRequest(new { error = $"Invalid detection type: {type}" });
            typeFilter = parsed;
        }

        var events = await _cctvPort.GetEventsForFeedAsync(cameraId, limit, since, typeFilter, ct);
        return Ok(events.Select(FormatDetectionEvent));
    }

    /// <summary>
    /// Get detection events across all of a user's cameras.
    /// </summary>
    [HttpGet("events/user/{userId}")]
    public async Task<IActionResult> GetUserEvents(
        string userId,
        [FromQuery] int limit = 50,
        [FromQuery] DateTime? since = null,
        CancellationToken ct = default)
    {
        var events = await _cctvPort.GetEventsForUserAsync(userId, limit, since, ct);
        return Ok(events.Select(FormatDetectionEvent));
    }

    /// <summary>
    /// User responds to a detection alert — either dismiss or escalate to SOS.
    /// If escalated:
    ///   1. Creates a ResponseRequest via IResponseCoordinationService
    ///   2. Camera snapshot auto-attached as evidence
    ///   3. Nearby responders dispatched with camera location
    ///
    /// Valid responses: "dismissed", "escalated"
    /// </summary>
    [HttpPost("{cameraId}/events/{eventId}/respond")]
    public async Task<IActionResult> RespondToEvent(
        string cameraId,
        string eventId,
        [FromBody] RespondToDetectionRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.UserResponse))
            return BadRequest(new { error = "UserResponse is required. Valid values: 'dismissed', 'escalated'" });

        var validResponses = new[] { "dismissed", "escalated" };
        if (!validResponses.Contains(request.UserResponse.ToLowerInvariant()))
            return BadRequest(new { error = $"Invalid response: {request.UserResponse}. Valid values: {string.Join(", ", validResponses)}" });

        var updated = await _cctvPort.RespondToEventAsync(eventId, request.UserResponse.ToLowerInvariant(), ct);

        // If escalated, create an SOS response via coordination service
        if (request.UserResponse.Equals("escalated", StringComparison.OrdinalIgnoreCase))
        {
            var feed = await _cctvPort.GetFeedAsync(cameraId, ct);
            if (feed is not null && feed.Latitude.HasValue && feed.Longitude.HasValue)
            {
                var description = $"[CCTV Alert Escalation] {updated.Label}\n" +
                    $"Camera: {feed.FeedName} ({feed.InstallationZone})\n" +
                    $"Detection: {updated.DetectionType}, Confidence: {updated.Confidence:P0}\n" +
                    $"Snapshot: {updated.SnapshotBlobReference}";

                var response = await _coordinationService.CreateResponseAsync(
                    feed.UserId,
                    ResponseScope.Neighborhood,
                    feed.Latitude.Value,
                    feed.Longitude.Value,
                    description,
                    $"CCTV_ALERT:{eventId}",
                    ct);

                _logger.LogWarning(
                    "CCTV ESCALATION: Event {EventId} on camera {CameraId} ({FeedName}) -> SOS {RequestId}. " +
                    "Detection: {DetectionType}, Confidence={Confidence:P0}",
                    eventId, cameraId, feed.FeedName, response.RequestId,
                    updated.DetectionType, updated.Confidence);

                await _hubContext.Clients.All.SendAsync("CCTVAlertEscalated", new
                {
                    updated.EventId,
                    CameraId = cameraId,
                    FeedName = feed.FeedName,
                    DetectionType = updated.DetectionType.ToString(),
                    updated.Confidence,
                    updated.Label,
                    ResponseRequestId = response.RequestId,
                    EscalatedAt = DateTime.UtcNow
                }, ct);

                return Ok(new
                {
                    updated.EventId,
                    updated.UserResponse,
                    ResponseRequestId = response.RequestId,
                    message = "Alert escalated to SOS. Responders are being dispatched."
                });
            }
        }

        return Ok(new
        {
            updated.EventId,
            updated.UserResponse,
            updated.EscalatedRequestId,
            message = request.UserResponse.Equals("dismissed", StringComparison.OrdinalIgnoreCase)
                ? "Alert dismissed."
                : "Alert response recorded."
        });
    }

    // ─────────────────────────────────────────────────────────────
    // Video Upload
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Submit a recorded video for analysis. The analysis pipeline runs detection
    /// on the video and produces timestamped DetectionEvents.
    ///
    /// In mock mode: analysis completes immediately with sample events.
    /// In production: queued for async processing by a GPU worker.
    /// </summary>
    [HttpPost("upload")]
    public async Task<IActionResult> SubmitVideoUpload(
        [FromBody] SubmitVideoRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
            return BadRequest(new { error = "UserId is required" });
        if (string.IsNullOrWhiteSpace(request.FileName))
            return BadRequest(new { error = "FileName is required" });

        var upload = new VideoUpload(
            UploadId: "",
            UserId: request.UserId,
            RequestId: request.RequestId,
            FileName: request.FileName,
            MimeType: request.MimeType ?? "video/mp4",
            FileSizeBytes: request.FileSizeBytes,
            Duration: request.DurationSeconds.HasValue ? TimeSpan.FromSeconds(request.DurationSeconds.Value) : null,
            ContentHash: request.ContentHash,
            BlobReference: request.BlobReference ?? $"uploads/{request.UserId}/{Guid.NewGuid():N}.mp4",
            ThumbnailBlobReference: null,
            Status: VideoAnalysisStatus.Queued,
            UploadedAt: DateTime.UtcNow,
            AnalysisStartedAt: null,
            AnalysisCompletedAt: null,
            EventsFound: 0,
            ErrorMessage: null);

        var result = await _cctvPort.SubmitVideoForAnalysisAsync(upload, ct);

        _logger.LogInformation(
            "Video submitted: {UploadId} ({FileName}, {Size} bytes) for user {UserId}, status={Status}",
            result.UploadId, result.FileName, result.FileSizeBytes, result.UserId, result.Status);

        return Accepted(new
        {
            result.UploadId,
            result.FileName,
            Status = result.Status.ToString(),
            result.EventsFound,
            result.UploadedAt,
            result.AnalysisCompletedAt,
            message = result.Status == VideoAnalysisStatus.Complete
                ? $"Analysis complete. {result.EventsFound} events found."
                : "Video queued for analysis. Check status with GET /api/cctv/upload/{uploadId}."
        });
    }

    /// <summary>
    /// Get the status of a video upload / analysis job.
    /// </summary>
    [HttpGet("upload/{uploadId}")]
    public async Task<IActionResult> GetUploadStatus(string uploadId, CancellationToken ct)
    {
        var upload = await _cctvPort.GetVideoUploadAsync(uploadId, ct);
        if (upload is null)
            return NotFound(new { error = $"Upload {uploadId} not found" });

        return Ok(new
        {
            upload.UploadId,
            upload.FileName,
            upload.MimeType,
            upload.FileSizeBytes,
            DurationSeconds = upload.Duration?.TotalSeconds,
            Status = upload.Status.ToString(),
            upload.UploadedAt,
            upload.AnalysisStartedAt,
            upload.AnalysisCompletedAt,
            upload.EventsFound,
            upload.ErrorMessage
        });
    }

    /// <summary>
    /// Get detection events found in an analyzed video.
    /// </summary>
    [HttpGet("upload/{uploadId}/events")]
    public async Task<IActionResult> GetUploadEvents(string uploadId, CancellationToken ct)
    {
        var upload = await _cctvPort.GetVideoUploadAsync(uploadId, ct);
        if (upload is null)
            return NotFound(new { error = $"Upload {uploadId} not found" });

        if (upload.Status != VideoAnalysisStatus.Complete)
            return Ok(new
            {
                upload.UploadId,
                Status = upload.Status.ToString(),
                Events = Array.Empty<object>(),
                message = "Analysis is not yet complete."
            });

        var events = await _cctvPort.GetEventsForUploadAsync(uploadId, ct);
        return Ok(new
        {
            upload.UploadId,
            Status = upload.Status.ToString(),
            upload.EventsFound,
            Events = events.Select(FormatDetectionEvent)
        });
    }

    // ─────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Format a DetectionEvent for the API response, stripping internal fields.
    /// </summary>
    private static object FormatDetectionEvent(DetectionEvent e) => new
    {
        e.EventId,
        e.FeedId,
        DetectionType = e.DetectionType.ToString(),
        e.Confidence,
        e.Label,
        e.BoundingBox,
        e.SnapshotBlobReference,
        e.ClipBlobReference,
        VideoTimestampSeconds = e.VideoTimestamp?.TotalSeconds,
        e.DetectedAt,
        e.UserNotified,
        e.UserResponse,
        e.EscalatedRequestId
    };

    /// <summary>
    /// Compute Haversine distance between two lat/lng points in meters.
    /// Used for nearby camera queries when PostGIS is not available (mock/dev).
    ///
    /// Formula: a = sin^2(dlat/2) + cos(lat1) * cos(lat2) * sin^2(dlon/2)
    ///          c = 2 * atan2(sqrt(a), sqrt(1-a))
    ///          d = R * c   where R = 6,371,000 meters (Earth mean radius)
    /// </summary>
    private static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6_371_000; // Earth radius in meters
        var dLat = DegreesToRadians(lat2 - lat1);
        var dLon = DegreesToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;
}

// ─────────────────────────────────────────────────────────────
// Request DTOs
// ─────────────────────────────────────────────────────────────

public record RegisterCameraRequest(
    string UserId,
    string FeedName,
    string? Protocol = "RTSP",
    string? FeedUrl = null,
    string? CameraBrand = null,
    string? CameraModel = null,
    double? Latitude = null,
    double? Longitude = null,
    string? InstallationZone = null,
    bool? AutoEscalationEnabled = false,
    int? AutoEscalationDelaySeconds = 60,
    // Detection config overrides
    bool? MotionDetection = true,
    bool? PersonDetection = true,
    bool? VehicleDetection = false,
    bool? PackageDetection = false,
    bool? AnomalyDetection = true,
    bool? AudioAnomalyDetection = true,
    bool? SmokeFireDetection = true,
    bool? PetAnimalDetection = true,
    double? AlertConfidenceThreshold = 0.7,
    int? AlertCooldownSeconds = 30
);

public record UpdateCameraRequest(
    string? FeedName = null,
    string? InstallationZone = null,
    bool? AutoEscalationEnabled = null,
    int? AutoEscalationDelaySeconds = null,
    // Detection config overrides (null = no change)
    bool? MotionDetection = null,
    bool? PersonDetection = null,
    bool? VehicleDetection = null,
    bool? AnomalyDetection = null,
    bool? AudioAnomalyDetection = null,
    bool? SmokeFireDetection = null,
    bool? PetAnimalDetection = null,
    double? AlertConfidenceThreshold = null,
    int? AlertCooldownSeconds = null
)
{
    /// <summary>Returns true if any detection config field was specified.</summary>
    public bool HasDetectionConfigChanges() =>
        MotionDetection.HasValue || PersonDetection.HasValue || VehicleDetection.HasValue ||
        AnomalyDetection.HasValue || AudioAnomalyDetection.HasValue || SmokeFireDetection.HasValue ||
        PetAnimalDetection.HasValue || AlertConfidenceThreshold.HasValue || AlertCooldownSeconds.HasValue;
}

public record RespondToDetectionRequest(string UserResponse);

public record SubmitVideoRequest(
    string UserId,
    string FileName,
    string? MimeType = "video/mp4",
    long FileSizeBytes = 0,
    double? DurationSeconds = null,
    string? ContentHash = null,
    string? BlobReference = null,
    string? RequestId = null   // Attach to an existing incident
);
