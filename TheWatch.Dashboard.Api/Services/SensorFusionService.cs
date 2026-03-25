// =============================================================================
// SensorFusionService — application service that aggregates multi-source sensor data,
// evaluates anomaly thresholds, auto-creates alerts, and broadcasts via SignalR.
// =============================================================================
//
// Architecture:
//   ┌──────────────┐   ┌──────────────────┐   ┌────────────────────────┐
//   │ Mobile App    │──▶│ SensorFusionSvc  │──▶│ ISensorFusionPort      │
//   │ CCTV Adapter  │   │ (this service)   │   │ (Mock or Production)   │
//   │ IoT Devices   │   └────────┬─────────┘   └────────────────────────┘
//   └──────────────┘            │
//                      ┌────────▼─────────┐
//                      │ DashboardHub      │  ← SignalR broadcast
//                      │ (real-time push)  │
//                      └──────────────────┘
//
// Responsibilities:
//   1. Ingest sensor readings from multiple sources (phone sensors, CCTV, IoT)
//   2. Trigger fusion pipeline to correlate readings into composite events
//   3. Evaluate user-configurable anomaly sensitivity thresholds (0-100)
//   4. When anomaly score exceeds threshold: auto-create alert, broadcast via SignalR
//   5. For critical life-safety events: auto-trigger SOS via IResponseCoordinationService
//
// Sensitivity Mapping (user sets 0-100):
//   0   = off / disabled (no alerts)
//   1-30  = low sensitivity (only high-confidence critical events)
//   31-60 = medium sensitivity (default — balanced)
//   61-90 = high sensitivity (more alerts, some false positives expected)
//   91-100 = maximum sensitivity (alert on everything, useful for testing)
//
//   Internal mapping: threshold = 1.0 - (sensitivity / 100.0)
//   Example: sensitivity=70 → threshold=0.30 → events with confidence >= 0.30 trigger alerts
//   Example: sensitivity=30 → threshold=0.70 → only high-confidence events trigger alerts
//
// Standards:
//   - SensorThresholds (SensorModels.cs) for classification thresholds
//   - ANSI S1.4-2014 for acoustic dBA weighting
//   - MIL-STD-1474E for gunshot impulse detection
//   - NFPA 1584 for stress index thresholds
//
// WAL: All sensor data processing is stateless within this service.
//      State is stored in the ISensorFusionPort adapter (in-memory for mock,
//      Cosmos DB / TimescaleDB for production).
//      CCTV detection events from ICCTVPort can also feed into the fusion pipeline.
// =============================================================================

using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using TheWatch.Dashboard.Api.Hubs;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Enums;

namespace TheWatch.Dashboard.Api.Services;

/// <summary>
/// Application service that bridges sensor data sources, the fusion engine,
/// anomaly detection, and real-time SignalR broadcasting.
/// </summary>
public interface ISensorFusionService
{
    /// <summary>
    /// Ingest a generic sensor reading, run anomaly evaluation, and broadcast if threshold exceeded.
    /// Returns the stored reading and any composite events triggered.
    /// </summary>
    Task<SensorIngestResult> IngestSensorReadingAsync(
        SensorReading reading, int userSensitivity = 50, CancellationToken ct = default);

    /// <summary>
    /// Ingest an accelerometer reading with fall/crash detection.
    /// </summary>
    Task<SensorIngestResult> IngestAccelerometerAsync(
        AccelerometerReading reading, int userSensitivity = 50, CancellationToken ct = default);

    /// <summary>
    /// Ingest an acoustic event (gunshot, scream, glass break, etc.).
    /// </summary>
    Task<SensorIngestResult> IngestAcousticEventAsync(
        AcousticReading reading, int userSensitivity = 50, CancellationToken ct = default);

    /// <summary>
    /// Ingest a wearable health reading (heart rate, SpO2, stress).
    /// </summary>
    Task<SensorIngestResult> IngestWearableHealthAsync(
        WearableHealthReading reading, int userSensitivity = 50, CancellationToken ct = default);

    /// <summary>
    /// Ingest a visual classification from on-device camera ML.
    /// </summary>
    Task<SensorIngestResult> IngestVisualClassificationAsync(
        VisualClassification classification, int userSensitivity = 50, CancellationToken ct = default);

    /// <summary>
    /// Ingest a CCTV detection event into the sensor fusion pipeline.
    /// Converts CCTV DetectionEvents into sensor readings for cross-source correlation.
    /// </summary>
    Task<SensorIngestResult> IngestCCTVDetectionAsync(
        DetectionEvent cctvEvent, int userSensitivity = 50, CancellationToken ct = default);

    /// <summary>
    /// Run the fusion pipeline for a user and return composite events.
    /// </summary>
    Task<IReadOnlyList<CompositeSensorEvent>> FuseAndBroadcastAsync(
        string userId, TimeSpan window, int userSensitivity = 50, CancellationToken ct = default);

    /// <summary>
    /// Get recent sensor readings for a user (for dashboard telemetry display).
    /// </summary>
    Task<IReadOnlyList<SensorReading>> GetRecentReadingsAsync(
        string userId, SensorType? type = null, int limit = 50, CancellationToken ct = default);

    /// <summary>
    /// Get all active (unresolved) composite events for a user.
    /// </summary>
    Task<IReadOnlyList<CompositeSensorEvent>> GetActiveEventsAsync(
        string userId, CancellationToken ct = default);

    /// <summary>
    /// Get sensor health status for a specific device.
    /// </summary>
    Task<IReadOnlyDictionary<SensorType, SensorStatus>> GetSensorStatusAsync(
        string userId, string deviceId, CancellationToken ct = default);

    /// <summary>
    /// Calibrate a sensor on a device.
    /// </summary>
    Task<bool> CalibrateSensorAsync(
        string userId, string deviceId, SensorType sensorType, CancellationToken ct = default);
}

/// <summary>
/// Result of ingesting a sensor reading — includes the stored reading and any
/// composite events that were triggered by the fusion pipeline.
/// </summary>
public record SensorIngestResult(
    bool ReadingStored,
    int CompositeEventsTriggered,
    IReadOnlyList<CompositeSensorEvent> CompositeEvents,
    bool AlertBroadcast,
    bool SosTriggered,
    string? SosRequestId
);

public class SensorFusionService : ISensorFusionService
{
    private readonly ISensorFusionPort _sensorPort;
    private readonly ICCTVPort _cctvPort;
    private readonly IResponseCoordinationService _coordinationService;
    private readonly IHubContext<DashboardHub> _hubContext;
    private readonly ILogger<SensorFusionService> _logger;

    // Track last fusion run per user to avoid redundant processing
    private readonly ConcurrentDictionary<string, DateTime> _lastFusionRun = new();

    // Minimum interval between fusion runs for the same user (prevents storm)
    private static readonly TimeSpan FusionCooldown = TimeSpan.FromSeconds(2);

    // Default fusion window — how far back to look for correlated readings
    private static readonly TimeSpan DefaultFusionWindow = TimeSpan.FromSeconds(10);

    public SensorFusionService(
        ISensorFusionPort sensorPort,
        ICCTVPort cctvPort,
        IResponseCoordinationService coordinationService,
        IHubContext<DashboardHub> hubContext,
        ILogger<SensorFusionService> logger)
    {
        _sensorPort = sensorPort;
        _cctvPort = cctvPort;
        _coordinationService = coordinationService;
        _hubContext = hubContext;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════════
    // Ingest Methods
    // ═══════════════════════════════════════════════════════════════

    public async Task<SensorIngestResult> IngestSensorReadingAsync(
        SensorReading reading, int userSensitivity = 50, CancellationToken ct = default)
    {
        var stored = await _sensorPort.RecordSensorReadingAsync(reading, ct);
        _logger.LogDebug(
            "Sensor reading ingested: {SensorType} from device {DeviceId}, user {UserId}, value={Value}{Unit}",
            reading.SensorType, reading.DeviceId, reading.UserId, reading.RawValue, reading.Unit);

        return await RunPostIngestPipeline(reading.UserId, userSensitivity, ct);
    }

    public async Task<SensorIngestResult> IngestAccelerometerAsync(
        AccelerometerReading reading, int userSensitivity = 50, CancellationToken ct = default)
    {
        // Apply server-side threshold evaluation if not set by client
        if (!reading.IsFallDetected && reading.GForce >= SensorThresholds.FallGForceThreshold)
        {
            reading.IsFallDetected = true;
            _logger.LogWarning(
                "Server-side FALL detection: {GForce:F1}g exceeds threshold {Threshold}g for user {UserId}",
                reading.GForce, SensorThresholds.FallGForceThreshold, reading.UserId);
        }
        if (!reading.IsCrashDetected && reading.GForce >= SensorThresholds.CrashGForceThreshold)
        {
            reading.IsCrashDetected = true;
            _logger.LogWarning(
                "Server-side CRASH detection: {GForce:F1}g exceeds threshold {Threshold}g for user {UserId}",
                reading.GForce, SensorThresholds.CrashGForceThreshold, reading.UserId);
        }

        var stored = await _sensorPort.RecordAccelerometerAsync(reading, ct);
        _logger.LogDebug(
            "Accelerometer reading: {GForce:F2}g, fall={IsFall}, crash={IsCrash}, user={UserId}",
            reading.GForce, reading.IsFallDetected, reading.IsCrashDetected, reading.UserId);

        // Immediate broadcast for fall/crash events regardless of fusion
        if (reading.IsFallDetected || reading.IsCrashDetected)
        {
            var eventType = reading.IsCrashDetected ? "CrashDetected" : "FallDetected";
            await _hubContext.Clients.Group($"user-{reading.UserId}").SendAsync("SensorAlert", new
            {
                AlertType = eventType,
                reading.UserId,
                reading.DeviceId,
                reading.GForce,
                reading.Latitude,
                reading.Longitude,
                Timestamp = DateTime.UtcNow
            }, ct);
        }

        return await RunPostIngestPipeline(reading.UserId, userSensitivity, ct);
    }

    public async Task<SensorIngestResult> IngestAcousticEventAsync(
        AcousticReading reading, int userSensitivity = 50, CancellationToken ct = default)
    {
        var stored = await _sensorPort.RecordAcousticEventAsync(reading, ct);
        _logger.LogDebug(
            "Acoustic event: {Classification}, {Decibels:F1}dBA, gunshot_conf={GunshotConf:F2}, user={UserId}",
            reading.Classification, reading.DecibelLevel, reading.GunshotConfidence, reading.UserId);

        // Immediate broadcast for high-confidence gunshot or scream
        if (reading.GunshotConfidence >= SensorThresholds.GunshotConfidenceMin ||
            (reading.Classification == AcousticClassification.Scream && reading.DecibelLevel >= SensorThresholds.ScreamDecibelMin))
        {
            var alertType = reading.GunshotConfidence >= SensorThresholds.GunshotConfidenceMin
                ? "GunshotDetected" : "ScreamDetected";

            await _hubContext.Clients.Group($"user-{reading.UserId}").SendAsync("SensorAlert", new
            {
                AlertType = alertType,
                reading.UserId,
                reading.DeviceId,
                reading.DecibelLevel,
                reading.GunshotConfidence,
                Classification = reading.Classification.ToString(),
                reading.Latitude,
                reading.Longitude,
                Timestamp = DateTime.UtcNow
            }, ct);

            _logger.LogWarning(
                "{AlertType}: {Classification} at {Decibels:F1}dBA for user {UserId}",
                alertType, reading.Classification, reading.DecibelLevel, reading.UserId);
        }

        return await RunPostIngestPipeline(reading.UserId, userSensitivity, ct);
    }

    public async Task<SensorIngestResult> IngestWearableHealthAsync(
        WearableHealthReading reading, int userSensitivity = 50, CancellationToken ct = default)
    {
        // Server-side heart rate spike evaluation
        if (!reading.IsHeartRateSpike && !reading.IsExercising &&
            reading.HeartRateBpm > reading.RestingHeartRateBpm * SensorThresholds.HeartRateSpikeMultiplier)
        {
            reading.IsHeartRateSpike = true;
        }

        var stored = await _sensorPort.RecordWearableHealthAsync(reading, ct);
        _logger.LogDebug(
            "Wearable health: HR={HR:F0}bpm, SpO2={SpO2:F1}%, stress={Stress}, user={UserId}",
            reading.HeartRateBpm, reading.SpO2Percent, reading.StressIndex, reading.UserId);

        // Immediate broadcast for critical health readings
        if (reading.SpO2Percent <= SensorThresholds.SpO2CriticalLow ||
            reading.SkinTempCelsius >= SensorThresholds.SkinTempHigh ||
            reading.StressIndex >= SensorThresholds.StressIndexCritical)
        {
            await _hubContext.Clients.Group($"user-{reading.UserId}").SendAsync("SensorAlert", new
            {
                AlertType = "MedicalDistress",
                reading.UserId,
                reading.DeviceId,
                reading.HeartRateBpm,
                reading.SpO2Percent,
                reading.SkinTempCelsius,
                reading.StressIndex,
                reading.IsExercising,
                reading.Latitude,
                reading.Longitude,
                Timestamp = DateTime.UtcNow
            }, ct);

            _logger.LogWarning(
                "Medical distress alert: SpO2={SpO2:F1}%, stress={Stress}, temp={Temp:F1}C for user {UserId}",
                reading.SpO2Percent, reading.StressIndex, reading.SkinTempCelsius, reading.UserId);
        }

        return await RunPostIngestPipeline(reading.UserId, userSensitivity, ct);
    }

    public async Task<SensorIngestResult> IngestVisualClassificationAsync(
        VisualClassification classification, int userSensitivity = 50, CancellationToken ct = default)
    {
        var stored = await _sensorPort.RecordVisualClassificationAsync(classification, ct);
        _logger.LogDebug(
            "Visual classification: {Type} ({Confidence:P0}) from camera {Camera}, user={UserId}",
            classification.ClassificationType, classification.ClassificationConfidence,
            classification.CameraId, classification.UserId);

        // Immediate broadcast for weapon, fire, smoke detections above high confidence
        var criticalTypes = new[] { "Weapon", "Fire", "Smoke" };
        if (criticalTypes.Contains(classification.ClassificationType, StringComparer.OrdinalIgnoreCase) &&
            classification.ClassificationConfidence >= 0.70f)
        {
            await _hubContext.Clients.Group($"user-{classification.UserId}").SendAsync("SensorAlert", new
            {
                AlertType = $"Visual{classification.ClassificationType}Detected",
                classification.UserId,
                classification.DeviceId,
                classification.ClassificationType,
                classification.ClassificationConfidence,
                classification.CameraId,
                classification.Latitude,
                classification.Longitude,
                Timestamp = DateTime.UtcNow
            }, ct);
        }

        return await RunPostIngestPipeline(classification.UserId, userSensitivity, ct);
    }

    public async Task<SensorIngestResult> IngestCCTVDetectionAsync(
        DetectionEvent cctvEvent, int userSensitivity = 50, CancellationToken ct = default)
    {
        // Convert CCTV detection event to a generic SensorReading for fusion
        var sensorReading = new SensorReading
        {
            Id = $"cctv-{cctvEvent.EventId}",
            SensorType = SensorType.Camera,
            DeviceId = $"cctv-{cctvEvent.FeedId}",
            UserId = cctvEvent.UserId,
            Timestamp = cctvEvent.DetectedAt,
            RawValue = cctvEvent.Confidence,
            Unit = "confidence",
            Confidence = (float)cctvEvent.Confidence
        };

        // Get camera location from the feed
        var feed = await _cctvPort.GetFeedAsync(cctvEvent.FeedId, ct);
        if (feed?.Latitude.HasValue == true)
        {
            sensorReading.Latitude = feed.Latitude.Value;
            sensorReading.Longitude = feed.Longitude!.Value;
        }

        await _sensorPort.RecordSensorReadingAsync(sensorReading, ct);

        _logger.LogInformation(
            "CCTV event ingested into fusion: {EventId}, type={DetectionType}, confidence={Confidence:P0}",
            cctvEvent.EventId, cctvEvent.DetectionType, cctvEvent.Confidence);

        // Broadcast CCTV event directly (in addition to fusion pipeline)
        await _hubContext.Clients.Group($"user-{cctvEvent.UserId}").SendAsync("CCTVDetectionEvent", new
        {
            cctvEvent.EventId,
            cctvEvent.FeedId,
            FeedName = feed?.FeedName,
            DetectionType = cctvEvent.DetectionType.ToString(),
            cctvEvent.Confidence,
            cctvEvent.Label,
            cctvEvent.SnapshotBlobReference,
            cctvEvent.DetectedAt
        }, ct);

        return await RunPostIngestPipeline(cctvEvent.UserId, userSensitivity, ct);
    }

    // ═══════════════════════════════════════════════════════════════
    // Fusion & Query
    // ═══════════════════════════════════════════════════════════════

    public async Task<IReadOnlyList<CompositeSensorEvent>> FuseAndBroadcastAsync(
        string userId, TimeSpan window, int userSensitivity = 50, CancellationToken ct = default)
    {
        var compositeEvents = await _sensorPort.FuseSensorDataAsync(userId, window, ct);
        if (compositeEvents is null || compositeEvents.Count == 0)
            return Array.Empty<CompositeSensorEvent>();

        var threshold = SensitivityToThreshold(userSensitivity);
        var alertableEvents = compositeEvents
            .Where(e => e.Confidence >= threshold)
            .ToList();

        string? sosRequestId = null;
        var sosTriggered = false;

        foreach (var evt in alertableEvents)
        {
            // Broadcast each event via SignalR
            await _hubContext.Clients.Group($"user-{userId}").SendAsync("CompositeSensorEvent", new
            {
                evt.EventId,
                EventType = evt.EventType.ToString(),
                evt.Confidence,
                evt.SeverityScore,
                evt.RequiresImmediateAction,
                evt.Description,
                evt.Latitude,
                evt.Longitude,
                ContributingSensorCount = evt.ContributingSensorIds.Count,
                evt.Timestamp
            }, ct);

            // Also broadcast to the "all" channel for dashboard monitoring
            await _hubContext.Clients.All.SendAsync("SensorCompositeEvent", new
            {
                UserId = userId,
                evt.EventId,
                EventType = evt.EventType.ToString(),
                evt.Confidence,
                evt.SeverityScore,
                evt.RequiresImmediateAction,
                evt.Description,
                evt.Timestamp
            }, ct);

            _logger.LogInformation(
                "Composite event: {EventType} for user {UserId}, confidence={Confidence:P0}, " +
                "severity={Severity}, immediate={Immediate}",
                evt.EventType, userId, evt.Confidence, evt.SeverityScore, evt.RequiresImmediateAction);

            // Auto-trigger SOS for life-safety events
            if (evt.RequiresImmediateAction && !sosTriggered)
            {
                try
                {
                    var response = await _coordinationService.CreateResponseAsync(
                        userId,
                        ResponseScope.Neighborhood,
                        evt.Latitude,
                        evt.Longitude,
                        $"[Auto-SOS: Sensor Fusion] {evt.Description}\n" +
                        $"Event: {evt.EventType}, Confidence: {evt.Confidence:P0}, Severity: {evt.SeverityScore}\n" +
                        $"Contributing sensors: {evt.ContributingSensorIds.Count}",
                        $"SENSOR_FUSION:{evt.EventType}",
                        ct);

                    sosRequestId = response.RequestId;
                    sosTriggered = true;

                    _logger.LogWarning(
                        "AUTO-SOS triggered by sensor fusion: {EventType} for user {UserId} -> {RequestId}",
                        evt.EventType, userId, response.RequestId);

                    await _hubContext.Clients.Group($"user-{userId}").SendAsync("AutoSosTriggered", new
                    {
                        evt.EventId,
                        EventType = evt.EventType.ToString(),
                        ResponseRequestId = response.RequestId,
                        evt.Description,
                        TriggeredAt = DateTime.UtcNow
                    }, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to auto-trigger SOS for sensor fusion event {EventType}, user {UserId}",
                        evt.EventType, userId);
                }
            }
        }

        return alertableEvents;
    }

    public async Task<IReadOnlyList<SensorReading>> GetRecentReadingsAsync(
        string userId, SensorType? type = null, int limit = 50, CancellationToken ct = default)
    {
        return await _sensorPort.GetRecentReadingsAsync(userId, type, limit, ct);
    }

    public async Task<IReadOnlyList<CompositeSensorEvent>> GetActiveEventsAsync(
        string userId, CancellationToken ct = default)
    {
        return await _sensorPort.GetActiveCompositeEventsAsync(userId, ct);
    }

    public async Task<IReadOnlyDictionary<SensorType, SensorStatus>> GetSensorStatusAsync(
        string userId, string deviceId, CancellationToken ct = default)
    {
        return await _sensorPort.GetSensorStatusAsync(userId, deviceId, ct);
    }

    public async Task<bool> CalibrateSensorAsync(
        string userId, string deviceId, SensorType sensorType, CancellationToken ct = default)
    {
        var result = await _sensorPort.CalibrateSensorAsync(userId, deviceId, sensorType, ct);

        if (result)
        {
            await _hubContext.Clients.Group($"user-{userId}").SendAsync("SensorCalibrated", new
            {
                UserId = userId,
                DeviceId = deviceId,
                SensorType = sensorType.ToString(),
                CalibratedAt = DateTime.UtcNow
            }, ct);
        }

        return result;
    }

    // ═══════════════════════════════════════════════════════════════
    // Internal Pipeline
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Run the post-ingest pipeline: check cooldown, run fusion, broadcast results.
    /// Called after every sensor reading ingest.
    /// </summary>
    private async Task<SensorIngestResult> RunPostIngestPipeline(
        string userId, int userSensitivity, CancellationToken ct)
    {
        // Check fusion cooldown to avoid processing storms
        var now = DateTime.UtcNow;
        if (_lastFusionRun.TryGetValue(userId, out var lastRun) && (now - lastRun) < FusionCooldown)
        {
            return new SensorIngestResult(
                ReadingStored: true,
                CompositeEventsTriggered: 0,
                CompositeEvents: Array.Empty<CompositeSensorEvent>(),
                AlertBroadcast: false,
                SosTriggered: false,
                SosRequestId: null);
        }

        _lastFusionRun[userId] = now;

        // Run fusion pipeline
        var events = await FuseAndBroadcastAsync(userId, DefaultFusionWindow, userSensitivity, ct);

        return new SensorIngestResult(
            ReadingStored: true,
            CompositeEventsTriggered: events.Count,
            CompositeEvents: events,
            AlertBroadcast: events.Count > 0,
            SosTriggered: events.Any(e => e.RequiresImmediateAction),
            SosRequestId: null);
    }

    /// <summary>
    /// Convert user sensitivity setting (0-100) to a confidence threshold (0.0-1.0).
    /// Higher sensitivity = lower threshold = more alerts.
    ///
    /// Example: sensitivity=70 -> threshold=0.30 (trigger on 30%+ confidence)
    /// Example: sensitivity=30 -> threshold=0.70 (only 70%+ confidence triggers)
    /// Example: sensitivity=0  -> threshold=1.01 (nothing triggers, effectively off)
    /// Example: sensitivity=100 -> threshold=0.0 (everything triggers)
    /// </summary>
    private static float SensitivityToThreshold(int sensitivity)
    {
        if (sensitivity <= 0) return 1.01f; // Effectively disable
        if (sensitivity >= 100) return 0.0f;
        return 1.0f - (sensitivity / 100.0f);
    }
}
