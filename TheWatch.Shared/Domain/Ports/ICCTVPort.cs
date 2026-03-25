// =============================================================================
// ICCTVPort — port interfaces for personal CCTV / security camera integration.
// =============================================================================
// Allows users to wire their personal CCTV cameras into TheWatch so the system
// can alert them (and optionally dispatch responders) when something happens.
//
// Two primary integration paths:
//
//   1. LIVE FEED REGISTRATION — User registers an RTSP/ONVIF/HLS camera stream.
//      TheWatch connects to the feed, runs server-side or edge analysis (motion,
//      person detection, anomaly detection), and raises alerts automatically.
//
//      Supported protocols:
//        - RTSP (Real Time Streaming Protocol) — most IP cameras (Hikvision, Dahua, Reolink, Amcrest)
//        - ONVIF (Open Network Video Interface Forum) — standardized discovery + PTZ control
//        - HLS (HTTP Live Streaming) — cloud cameras that expose an HLS endpoint
//        - RTMP (Real-Time Messaging Protocol) — for cameras that push to an RTMP server
//        - WebRTC — browser-based low-latency feeds
//
//      Example: User adds their Reolink doorbell camera via RTSP.
//        Camera URL: rtsp://192.168.1.50:554/h264Preview_01_main
//        TheWatch runs person detection → detects unknown person at 2 AM
//        → alerts user via push notification → user taps "Call The Watch"
//        → SOS triggered with camera snapshot as evidence
//
//   2. VIDEO UPLOAD / CLIP SUBMISSION — User uploads a recorded clip (MP4, MOV, AVI)
//      for analysis. TheWatch runs the same detection pipeline on the upload and
//      returns timestamped events found in the footage.
//
//      Example: User uploads a dashcam clip from an incident.
//        TheWatch analyzes → detects "person running", "vehicle collision" at timestamps
//        → attaches to the incident as evidence with frame-by-frame annotations
//
// Detection capabilities (adapter-dependent, mock simulates all):
//   - Motion detection (pixel-diff or optical flow)
//   - Person detection (YOLO, MobileNet SSD, or cloud Vision API)
//   - Vehicle detection (make/model/color when possible)
//   - Face detection (presence only — NO facial recognition stored, privacy-first)
//   - Package / object detection (left objects, removed objects)
//   - Anomaly / unusual activity (loitering, fence climbing, running)
//   - Audio anomaly (glass break, scream, gunshot — from camera mic)
//   - Smoke / fire detection (visual)
//   - Pet / animal detection (to suppress false positives)
//   - License plate detection (OCR — stored ephemerally, auto-deleted after 24h)
//
// Privacy guardrails:
//   - Facial recognition data is NEVER stored. Face detection is presence-only.
//   - License plates auto-delete after 24 hours unless attached to an active incident.
//   - Camera feeds are NOT recorded by TheWatch unless the user explicitly enables
//     cloud recording. Analysis runs on the live stream and only events are stored.
//   - Users can set "privacy zones" (masked regions) that are blacked out before analysis.
//   - All camera credentials are encrypted at rest (AES-256) and never logged.
//
// Alert-to-SOS bridge:
//   When a camera detection event meets the user's alert threshold, the system:
//   1. Sends a push notification to the user with the detection frame/clip
//   2. User reviews and can either dismiss or escalate
//   3. If user taps "Call The Watch" (or doesn't respond within timeout):
//      → Creates an SOS ResponseRequest via IResponseCoordinationService
//      → Camera snapshot/clip auto-attached as EvidenceSubmission
//      → Nearby responders dispatched with camera location as incident coordinates
//   4. If auto-escalation is enabled for this camera:
//      → System auto-triggers SOS after configurable delay (default: 60s)
//      → User can cancel within the delay window
//
// Camera brands with known RTSP URL patterns (auto-discovery):
//   Hikvision:    rtsp://{ip}:554/Streaming/Channels/101
//   Dahua:        rtsp://{ip}:554/cam/realmonitor?channel=1&subtype=0
//   Reolink:      rtsp://{ip}:554/h264Preview_01_main
//   Amcrest:      rtsp://{ip}:554/cam/realmonitor?channel=1&subtype=0
//   Axis:         rtsp://{ip}/axis-media/media.amp
//   Wyze (RTSP):  rtsp://{ip}/live
//   UniFi:        rtsp://{ip}:7447/camera-id
//   Eufy:         rtsp://{ip}:554/live0 (requires firmware mod)
//   Ring:         Not RTSP — requires Ring API bridge (see IoTSource.Ring)
//   Nest/Google:  Not RTSP — requires Google SDM API bridge
//   Arlo:         Not RTSP — requires Arlo API bridge
//
// ONVIF discovery:
//   TheWatch can discover ONVIF-compatible cameras on the local network using
//   WS-Discovery (multicast to 239.255.255.250:3702). The user's companion app
//   (Android/iOS) runs discovery on their WiFi and presents found cameras for
//   one-tap registration.
//
// WAL: Camera credentials NEVER logged. Feed URLs contain credentials and are
//      stored encrypted (AES-256-GCM). Analysis events stored, raw video NOT
//      stored unless user opts into cloud recording. All detection frames are
//      auto-deleted after 7 days unless attached to an incident.
// =============================================================================

using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Domain.Ports;

// ═══════════════════════════════════════════════════════════════
// Camera Feed Registration
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Protocol used to connect to a camera feed.
/// </summary>
public enum CameraProtocol
{
    /// <summary>RTSP — Real Time Streaming Protocol. Most IP cameras.</summary>
    RTSP,

    /// <summary>ONVIF — standardized IP camera protocol with discovery + PTZ.</summary>
    ONVIF,

    /// <summary>HLS — HTTP Live Streaming. Cloud cameras with web endpoints.</summary>
    HLS,

    /// <summary>RTMP — cameras that push to an RTMP ingest server.</summary>
    RTMP,

    /// <summary>WebRTC — browser-based peer-to-peer streaming.</summary>
    WebRTC,

    /// <summary>CloudAPI — cloud-only cameras accessed via vendor REST API (Ring, Nest, Arlo).</summary>
    CloudAPI,

    /// <summary>Upload — no live feed; user uploads recorded clips for analysis.</summary>
    Upload
}

/// <summary>
/// Connection health of a registered camera feed.
/// </summary>
public enum CameraFeedStatus
{
    /// <summary>Feed is connected and streaming.</summary>
    Online,

    /// <summary>Feed was connected but connection dropped. Auto-reconnect pending.</summary>
    Reconnecting,

    /// <summary>Feed is not reachable (camera off, network issue, credentials changed).</summary>
    Offline,

    /// <summary>Camera is registered but feed has not been connected yet.</summary>
    Pending,

    /// <summary>Authentication failed — credentials likely expired or changed.</summary>
    AuthFailed,

    /// <summary>Analysis is paused by user (camera still connected but events suppressed).</summary>
    Paused
}

/// <summary>
/// A registered camera feed linked to a user's TheWatch account.
///
/// Example:
///   var cam = new CameraFeedRegistration(
///       FeedId: "cam-001",
///       UserId: "user-456",
///       FeedName: "Front Door Camera",
///       Protocol: CameraProtocol.RTSP,
///       FeedUrl: "rtsp://192.168.1.50:554/h264Preview_01_main",
///       ...
///   );
/// </summary>
public record CameraFeedRegistration(
    string FeedId,
    string UserId,

    /// <summary>User-friendly name ("Front Door", "Driveway", "Baby Room").</summary>
    string FeedName,

    CameraProtocol Protocol,

    /// <summary>
    /// Connection URL. Encrypted at rest. Contains credentials for RTSP/ONVIF.
    /// Examples:
    ///   RTSP:     "rtsp://admin:pass@192.168.1.50:554/h264Preview_01_main"
    ///   ONVIF:    "http://192.168.1.50:80/onvif/device_service"
    ///   HLS:      "https://camera.example.com/live/stream.m3u8"
    ///   CloudAPI: "ring://device-id-123" or "nest://device-id-456"
    ///   Upload:   null (no live feed)
    /// </summary>
    string? FeedUrl,

    /// <summary>Camera brand/model if known (auto-detected or user-entered).</summary>
    string? CameraBrand,
    string? CameraModel,

    // Location
    double? Latitude,
    double? Longitude,
    string? InstallationZone,     // "Front Door", "Driveway", "Backyard", "Living Room"

    // Status
    CameraFeedStatus Status,
    DateTime RegisteredAt,
    DateTime? LastFrameAt,        // When the last frame/event was received
    DateTime? LastEventAt,        // When the last detection event occurred

    // Detection configuration — what to look for
    CameraDetectionConfig DetectionConfig,

    // Privacy zones — regions of the frame to mask before analysis
    // Format: list of normalized rectangles (x, y, width, height in 0.0-1.0)
    IReadOnlyList<PrivacyZone>? PrivacyZones,

    // Auto-escalation: if true, unacknowledged camera alerts auto-trigger SOS
    bool AutoEscalationEnabled,
    TimeSpan AutoEscalationDelay,  // Default: 60 seconds

    // Cloud recording: if true, TheWatch stores the feed (costs $, requires storage plan)
    bool CloudRecordingEnabled
);

/// <summary>
/// What the analysis pipeline should look for on this camera feed.
/// Each detection type can be individually enabled/disabled.
///
/// Example:
///   A baby room camera might only want person detection + audio anomaly.
///   A driveway camera wants person, vehicle, package, and license plate.
/// </summary>
public record CameraDetectionConfig(
    bool MotionDetection,
    bool PersonDetection,
    bool VehicleDetection,
    bool FacePresenceDetection,    // Presence only — NO recognition
    bool PackageDetection,
    bool AnomalyDetection,         // Loitering, fence climbing, running
    bool AudioAnomalyDetection,    // Glass break, scream, gunshot
    bool SmokeFireDetection,
    bool PetAnimalDetection,       // To suppress false positives
    bool LicensePlateDetection,    // OCR — auto-deleted after 24h

    /// <summary>
    /// Minimum confidence threshold (0.0-1.0) for triggering an alert.
    /// Lower = more sensitive (more false positives).
    /// Higher = fewer alerts but may miss events.
    /// Default: 0.7
    /// </summary>
    double AlertConfidenceThreshold,

    /// <summary>
    /// Cooldown between alerts for the same detection type on this camera.
    /// Prevents alert fatigue from repeated detections of the same event.
    /// Default: 30 seconds.
    /// </summary>
    TimeSpan AlertCooldown,

    /// <summary>
    /// Time windows when this camera should suppress alerts (e.g., when
    /// the user is normally home and doesn't want person alerts).
    /// Null = always active.
    /// </summary>
    IReadOnlyList<SuppressWindow>? SuppressWindows
);

/// <summary>A rectangular region of the frame to mask before analysis (privacy zone).</summary>
public record PrivacyZone(
    string Label,       // "Neighbor's Window", "Street", etc.
    double X,           // Normalized 0.0-1.0 from left
    double Y,           // Normalized 0.0-1.0 from top
    double Width,       // Normalized 0.0-1.0
    double Height       // Normalized 0.0-1.0
);

/// <summary>Time window during which alerts are suppressed for a camera.</summary>
public record SuppressWindow(
    TimeOnly From,
    TimeOnly To,
    DayOfWeek[]? Days   // Null = every day
);

// ═══════════════════════════════════════════════════════════════
// Detection Events
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Type of object/event detected by the analysis pipeline.
/// </summary>
public enum DetectionType
{
    Motion,
    Person,
    Vehicle,
    FacePresence,
    Package,
    Anomaly,
    AudioAnomaly,
    SmokeFire,
    PetAnimal,
    LicensePlate
}

/// <summary>
/// A single detection event from a camera feed or uploaded video.
///
/// Example:
///   DetectionEvent for a person detected at the front door at 2:15 AM:
///   {
///       EventId: "det-001",
///       FeedId: "cam-front-door",
///       DetectionType: Person,
///       Confidence: 0.92,
///       Label: "Person — unknown",
///       BoundingBox: { X: 0.3, Y: 0.2, Width: 0.15, Height: 0.4 },
///       SnapshotBlobReference: "cctv/cam-001/det-001.jpg",
///       ...
///   }
/// </summary>
public record DetectionEvent(
    string EventId,
    string FeedId,
    string UserId,

    DetectionType DetectionType,
    double Confidence,           // 0.0-1.0
    string Label,                // "Person — unknown", "Vehicle — sedan, dark", "Package delivered"

    /// <summary>Bounding box in normalized coordinates (0.0-1.0).</summary>
    BoundingBox? BoundingBox,

    /// <summary>Reference to the snapshot frame in blob storage.</summary>
    string? SnapshotBlobReference,

    /// <summary>Reference to a short clip around the event (e.g., 10s before + 10s after).</summary>
    string? ClipBlobReference,

    /// <summary>
    /// For uploaded videos: timestamp within the video where the event occurs.
    /// For live feeds: null (DetectedAt is the real-time timestamp).
    /// </summary>
    TimeSpan? VideoTimestamp,

    /// <summary>When the event was detected.</summary>
    DateTime DetectedAt,

    /// <summary>
    /// Whether the user has been notified about this event.
    /// Events below the confidence threshold are stored but not notified.
    /// </summary>
    bool UserNotified,

    /// <summary>
    /// User's response to the alert: null (pending), "dismissed", "escalated", "auto_escalated".
    /// </summary>
    string? UserResponse,

    /// <summary>
    /// If the user escalated this event to an SOS, the resulting ResponseRequest ID.
    /// </summary>
    string? EscalatedRequestId,

    /// <summary>Additional metadata from the detection model.</summary>
    IDictionary<string, string>? Metadata
);

/// <summary>Bounding box in normalized image coordinates.</summary>
public record BoundingBox(
    double X,       // Left edge, 0.0-1.0
    double Y,       // Top edge, 0.0-1.0
    double Width,   // 0.0-1.0
    double Height   // 0.0-1.0
);

// ═══════════════════════════════════════════════════════════════
// Video Upload for Analysis
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Status of a video upload submitted for analysis.
/// </summary>
public enum VideoAnalysisStatus
{
    /// <summary>Upload received, queued for analysis.</summary>
    Queued,

    /// <summary>Analysis pipeline is running on this video.</summary>
    Analyzing,

    /// <summary>Analysis complete — events available.</summary>
    Complete,

    /// <summary>Analysis failed (corrupt file, unsupported format, etc.).</summary>
    Failed
}

/// <summary>
/// A video uploaded by the user for analysis. Not from a live feed —
/// this is a recorded file (dashcam, exported CCTV clip, phone video).
///
/// Example:
///   User uploads a 30-second Ring doorbell clip:
///   POST /api/cctv/upload
///   → VideoUpload created (status: Queued)
///   → Analysis pipeline runs
///   → DetectionEvents created for each finding
///   → status → Complete
///   → User can review events and escalate if needed
/// </summary>
public record VideoUpload(
    string UploadId,
    string UserId,
    string? RequestId,            // If attached to an existing incident

    string FileName,
    string MimeType,              // "video/mp4", "video/quicktime", "video/x-msvideo"
    long FileSizeBytes,
    TimeSpan? Duration,
    string? ContentHash,          // SHA-256 for integrity

    string BlobReference,         // Path in blob storage
    string? ThumbnailBlobReference,

    VideoAnalysisStatus Status,
    DateTime UploadedAt,
    DateTime? AnalysisStartedAt,
    DateTime? AnalysisCompletedAt,

    /// <summary>Number of detection events found in this video.</summary>
    int EventsFound,

    /// <summary>Error message if analysis failed.</summary>
    string? ErrorMessage
);

// ═══════════════════════════════════════════════════════════════
// Port Interface
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Port for personal CCTV / security camera integration.
///
/// Adapters:
///   - MockCCTVAdapter: in-memory simulation with seeded cameras and events (dev)
///   - Production: RTSP connector (GStreamer/FFmpeg), ONVIF client (onvif-rs),
///     cloud API bridges (Ring API, Nest SDM, Arlo API), analysis pipeline
///     (YOLO/MobileNet on GPU worker, or Azure/Google/AWS Vision API)
/// </summary>
public interface ICCTVPort
{
    // ── Camera Feed Management ───────────────────────────────────

    /// <summary>Register a new camera feed.</summary>
    Task<CameraFeedRegistration> RegisterFeedAsync(
        CameraFeedRegistration feed, CancellationToken ct = default);

    /// <summary>Update an existing camera feed (name, detection config, privacy zones, etc.).</summary>
    Task<CameraFeedRegistration> UpdateFeedAsync(
        CameraFeedRegistration feed, CancellationToken ct = default);

    /// <summary>Remove a camera feed and all associated events.</summary>
    Task<bool> RemoveFeedAsync(string feedId, CancellationToken ct = default);

    /// <summary>Get a camera feed by ID.</summary>
    Task<CameraFeedRegistration?> GetFeedAsync(string feedId, CancellationToken ct = default);

    /// <summary>Get all camera feeds for a user.</summary>
    Task<IReadOnlyList<CameraFeedRegistration>> GetUserFeedsAsync(
        string userId, CancellationToken ct = default);

    /// <summary>Pause analysis on a feed (camera stays connected, events suppressed).</summary>
    Task<CameraFeedRegistration> PauseFeedAsync(string feedId, CancellationToken ct = default);

    /// <summary>Resume analysis on a paused feed.</summary>
    Task<CameraFeedRegistration> ResumeFeedAsync(string feedId, CancellationToken ct = default);

    /// <summary>Test connectivity to a camera (validates URL + credentials).</summary>
    Task<CameraConnectivityResult> TestConnectivityAsync(
        string feedUrl, CameraProtocol protocol, CancellationToken ct = default);

    // ── Detection Events ─────────────────────────────────────────

    /// <summary>
    /// Ingest a detection event from the analysis pipeline (or external camera system).
    /// If the event meets the user's alert threshold, sends a push notification.
    /// If auto-escalation is enabled and user doesn't respond, triggers SOS.
    /// </summary>
    Task<DetectionEvent> IngestDetectionEventAsync(
        DetectionEvent detectionEvent, CancellationToken ct = default);

    /// <summary>Get detection events for a camera feed.</summary>
    Task<IReadOnlyList<DetectionEvent>> GetEventsForFeedAsync(
        string feedId, int limit = 50, DateTime? since = null,
        DetectionType? typeFilter = null, CancellationToken ct = default);

    /// <summary>Get detection events for a user across all their cameras.</summary>
    Task<IReadOnlyList<DetectionEvent>> GetEventsForUserAsync(
        string userId, int limit = 50, DateTime? since = null,
        CancellationToken ct = default);

    /// <summary>
    /// User responds to a detection alert: dismiss or escalate to SOS.
    /// If escalated, creates a ResponseRequest and attaches the detection snapshot as evidence.
    /// Returns the updated event (with EscalatedRequestId if escalated).
    /// </summary>
    Task<DetectionEvent> RespondToEventAsync(
        string eventId, string userResponse, CancellationToken ct = default);

    // ── Video Upload ─────────────────────────────────────────────

    /// <summary>Submit a video for analysis.</summary>
    Task<VideoUpload> SubmitVideoForAnalysisAsync(
        VideoUpload upload, CancellationToken ct = default);

    /// <summary>Get the status of a video upload/analysis.</summary>
    Task<VideoUpload?> GetVideoUploadAsync(
        string uploadId, CancellationToken ct = default);

    /// <summary>Get all video uploads for a user.</summary>
    Task<IReadOnlyList<VideoUpload>> GetUserUploadsAsync(
        string userId, CancellationToken ct = default);

    /// <summary>Get detection events found in an analyzed video.</summary>
    Task<IReadOnlyList<DetectionEvent>> GetEventsForUploadAsync(
        string uploadId, CancellationToken ct = default);
}

/// <summary>Result of testing camera connectivity.</summary>
public record CameraConnectivityResult(
    bool Success,
    string? ErrorMessage,
    string? CameraBrand,         // Auto-detected from ONVIF or RTSP headers
    string? CameraModel,
    string? Resolution,          // "1920x1080", "2560x1440", etc.
    double? LatencyMs            // Round-trip time to first frame
);
