// =============================================================================
// MockSensorFusionPort — in-memory mock implementation of ISensorFusionPort.
// =============================================================================
// Stores raw sensor readings in memory and simulates the fusion engine by
// correlating readings within a time window and producing CompositeSensorEvents.
//
// Seeded data:
//   - Sample accelerometer, acoustic, wearable health, and visual readings
//   - Sample composite events (fall, gunshot)
//
// Fusion Algorithm (Mock):
//   1. Collect all readings within the specified time window for the user
//   2. Check for known event patterns:
//      - Gunshot: AcousticReading with GunshotConfidence >= 0.85
//      - Fall: AccelerometerReading with IsFallDetected = true
//      - Crash: AccelerometerReading with IsCrashDetected = true
//      - Medical Distress: WearableHealthReading with SpO2 <= 90 or StressIndex >= 80
//      - Fire/Smoke: VisualClassification with ClassificationType "Fire" or "Smoke"
//      - Scream: AcousticReading with Classification == Scream and dBA >= 85
//      - Glass Break: AcousticReading with Classification == GlassBreak
//   3. Combine contributing readings and compute composite confidence
//   4. Set RequiresImmediateAction for severity >= 80
//
// WAL: All operations are in-memory only. No external calls.
// =============================================================================

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using TheWatch.Shared.Domain.Models;
using TheWatch.Shared.Domain.Ports;
using TheWatch.Shared.Enums;

namespace TheWatch.Adapters.Mock;

public class MockSensorFusionPort : ISensorFusionPort
{
    private readonly ConcurrentBag<SensorReading> _readings = new();
    private readonly ConcurrentBag<CompositeSensorEvent> _compositeEvents = new();
    private readonly ILogger<MockSensorFusionPort> _logger;

    public MockSensorFusionPort(ILogger<MockSensorFusionPort> logger)
    {
        _logger = logger;
        SeedMockData();
    }

    // Parameterless constructor for backward compatibility with DI that doesn't have ILogger registered
    public MockSensorFusionPort()
        : this(Microsoft.Extensions.Logging.Abstractions.NullLogger<MockSensorFusionPort>.Instance)
    {
    }

    private void SeedMockData()
    {
        var now = DateTime.UtcNow;

        // Seed accelerometer readings
        _readings.Add(new AccelerometerReading
        {
            Id = "accel-001",
            SensorType = SensorType.Accelerometer,
            DeviceId = "iphone-14-mock",
            UserId = "mock-user-001",
            Timestamp = now.AddMinutes(-5),
            RawValue = 1.02,
            Unit = "g",
            Confidence = 0.99f,
            Latitude = 30.2672,
            Longitude = -97.7431,
            GForce = 1.02,
            IsFallDetected = false,
            IsCrashDetected = false
        });

        _readings.Add(new AccelerometerReading
        {
            Id = "accel-002",
            SensorType = SensorType.Accelerometer,
            DeviceId = "iphone-14-mock",
            UserId = "mock-user-001",
            Timestamp = now.AddMinutes(-2),
            RawValue = 3.8,
            Unit = "g",
            Confidence = 0.95f,
            Latitude = 30.2672,
            Longitude = -97.7431,
            GForce = 3.8,
            IsFallDetected = true,
            IsCrashDetected = false,
            ImpactGForce = 4.1,
            FallAngleDegrees = 72.5,
            FreeFallDurationMs = 380
        });

        // Seed acoustic readings
        _readings.Add(new AcousticReading
        {
            Id = "acoustic-001",
            SensorType = SensorType.Microphone,
            DeviceId = "iphone-14-mock",
            UserId = "mock-user-001",
            Timestamp = now.AddMinutes(-10),
            RawValue = 62.0,
            Unit = "dBA",
            Confidence = 0.97f,
            Latitude = 30.2672,
            Longitude = -97.7431,
            Classification = AcousticClassification.AmbientNormal,
            DecibelLevel = 62.0,
            GunshotConfidence = 0.02f,
            FrequencyHz = 800,
            DurationMs = 5000
        });

        _readings.Add(new AcousticReading
        {
            Id = "acoustic-002",
            SensorType = SensorType.Microphone,
            DeviceId = "iphone-14-mock",
            UserId = "mock-user-001",
            Timestamp = now.AddMinutes(-1),
            RawValue = 142.0,
            Unit = "dBA",
            Confidence = 0.91f,
            Latitude = 30.2673,
            Longitude = -97.7432,
            Classification = AcousticClassification.Gunshot,
            DecibelLevel = 142.0,
            GunshotConfidence = 0.92f,
            FrequencyHz = 2500,
            DurationMs = 1.5,
            DirectionDegrees = 45.0,
            DistanceEstimateMeters = 80.0
        });

        // Seed wearable health readings
        _readings.Add(new WearableHealthReading
        {
            Id = "health-001",
            SensorType = SensorType.WearableHealth,
            DeviceId = "apple-watch-mock",
            UserId = "mock-user-001",
            Timestamp = now.AddMinutes(-3),
            RawValue = 72.0,
            Unit = "bpm",
            Confidence = 0.98f,
            Latitude = 30.2672,
            Longitude = -97.7431,
            HeartRateBpm = 72.0,
            RestingHeartRateBpm = 68.0,
            IsHeartRateSpike = false,
            SpO2Percent = 97.5,
            SkinTempCelsius = 35.2,
            StressIndex = 25,
            IsExercising = false,
            StepCountLast5Min = 42
        });

        // Seed visual classification
        _readings.Add(new VisualClassification
        {
            Id = "visual-001",
            SensorType = SensorType.Camera,
            DeviceId = "iphone-14-mock",
            UserId = "mock-user-001",
            Timestamp = now.AddMinutes(-8),
            RawValue = 0.82,
            Unit = "confidence",
            Confidence = 0.82f,
            Latitude = 30.2672,
            Longitude = -97.7431,
            ClassificationType = "Person",
            ClassificationConfidence = 0.82f,
            BoundingBoxX = 0.3,
            BoundingBoxY = 0.2,
            BoundingBoxW = 0.15,
            BoundingBoxH = 0.45,
            CameraId = "rear"
        });

        // Seed composite events
        _compositeEvents.Add(new CompositeSensorEvent
        {
            EventId = "composite-001",
            EventType = CompositeEventType.Fall,
            Confidence = 0.94f,
            ContributingSensorIds = new List<string> { "accel-002" },
            Timestamp = now.AddMinutes(-2),
            Latitude = 30.2672,
            Longitude = -97.7431,
            SeverityScore = 72,
            RequiresImmediateAction = false,
            Description = "Fall detected — 3.8g impact at 72.5 degrees. Accelerometer corroborated by gyroscope orientation change.",
            CorrelationId = ""
        });
    }

    // ═══════════════════════════════════════════════════════════════
    // Record Raw Readings
    // ═══════════════════════════════════════════════════════════════

    public Task<SensorReading> RecordSensorReadingAsync(SensorReading reading, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(reading.Id))
            reading.Id = Guid.NewGuid().ToString();
        reading.Timestamp = reading.Timestamp == default ? DateTime.UtcNow : reading.Timestamp;

        _readings.Add(reading);
        _logger.LogDebug("[MockSensor] Recorded {SensorType} reading {Id} for user {UserId}",
            reading.SensorType, reading.Id, reading.UserId);
        return Task.FromResult(reading);
    }

    public Task<AccelerometerReading> RecordAccelerometerAsync(AccelerometerReading reading, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(reading.Id))
            reading.Id = Guid.NewGuid().ToString();
        reading.SensorType = SensorType.Accelerometer;
        reading.Unit = "g";
        reading.RawValue = reading.GForce;
        reading.Timestamp = reading.Timestamp == default ? DateTime.UtcNow : reading.Timestamp;

        // Server-side threshold evaluation
        if (!reading.IsFallDetected && reading.GForce >= SensorThresholds.FallGForceThreshold)
            reading.IsFallDetected = true;
        if (!reading.IsCrashDetected && reading.GForce >= SensorThresholds.CrashGForceThreshold)
            reading.IsCrashDetected = true;

        _readings.Add(reading);
        _logger.LogDebug("[MockSensor] Accelerometer: {GForce:F2}g, fall={Fall}, crash={Crash}, user={UserId}",
            reading.GForce, reading.IsFallDetected, reading.IsCrashDetected, reading.UserId);
        return Task.FromResult(reading);
    }

    public Task<AcousticReading> RecordAcousticEventAsync(AcousticReading reading, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(reading.Id))
            reading.Id = Guid.NewGuid().ToString();
        reading.SensorType = SensorType.Microphone;
        reading.Unit = "dBA";
        reading.RawValue = reading.DecibelLevel;
        reading.Timestamp = reading.Timestamp == default ? DateTime.UtcNow : reading.Timestamp;

        _readings.Add(reading);
        _logger.LogDebug("[MockSensor] Acoustic: {Classification}, {Decibels:F1}dBA, gunshot={Gunshot:F2}, user={UserId}",
            reading.Classification, reading.DecibelLevel, reading.GunshotConfidence, reading.UserId);
        return Task.FromResult(reading);
    }

    public Task<WearableHealthReading> RecordWearableHealthAsync(WearableHealthReading reading, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(reading.Id))
            reading.Id = Guid.NewGuid().ToString();
        reading.SensorType = SensorType.WearableHealth;
        reading.Unit = "bpm";
        reading.RawValue = reading.HeartRateBpm;
        reading.Timestamp = reading.Timestamp == default ? DateTime.UtcNow : reading.Timestamp;

        // Server-side spike evaluation
        if (!reading.IsHeartRateSpike && !reading.IsExercising &&
            reading.HeartRateBpm > reading.RestingHeartRateBpm * SensorThresholds.HeartRateSpikeMultiplier)
        {
            reading.IsHeartRateSpike = true;
        }

        _readings.Add(reading);
        _logger.LogDebug("[MockSensor] Wearable: HR={HR:F0}bpm, SpO2={SpO2:F1}%, stress={Stress}, user={UserId}",
            reading.HeartRateBpm, reading.SpO2Percent, reading.StressIndex, reading.UserId);
        return Task.FromResult(reading);
    }

    public Task<VisualClassification> RecordVisualClassificationAsync(VisualClassification classification, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(classification.Id))
            classification.Id = Guid.NewGuid().ToString();
        classification.SensorType = SensorType.Camera;
        classification.Unit = "confidence";
        classification.RawValue = classification.ClassificationConfidence;
        classification.Timestamp = classification.Timestamp == default ? DateTime.UtcNow : classification.Timestamp;

        _readings.Add(classification);
        _logger.LogDebug("[MockSensor] Visual: {Type} ({Confidence:P0}), camera={Camera}, user={UserId}",
            classification.ClassificationType, classification.ClassificationConfidence,
            classification.CameraId, classification.UserId);
        return Task.FromResult(classification);
    }

    // ═══════════════════════════════════════════════════════════════
    // Fusion
    // ═══════════════════════════════════════════════════════════════

    public Task<IReadOnlyList<CompositeSensorEvent>> FuseSensorDataAsync(
        string userId, TimeSpan window, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - window;
        var userReadings = _readings
            .Where(r => r.UserId == userId && r.Timestamp >= cutoff)
            .ToList();

        if (userReadings.Count == 0)
            return Task.FromResult<IReadOnlyList<CompositeSensorEvent>>(Array.Empty<CompositeSensorEvent>());

        var newEvents = new List<CompositeSensorEvent>();

        // Check for gunshot composite event
        var gunshotAcoustic = userReadings
            .OfType<AcousticReading>()
            .Where(a => a.GunshotConfidence >= SensorThresholds.GunshotConfidenceMin)
            .ToList();

        foreach (var gunshot in gunshotAcoustic)
        {
            // Look for corroborating accelerometer shock within 2 seconds
            var accelCorroboration = userReadings
                .OfType<AccelerometerReading>()
                .Where(a => Math.Abs((a.Timestamp - gunshot.Timestamp).TotalSeconds) < 2)
                .FirstOrDefault();

            var contributingIds = new List<string> { gunshot.Id };
            var contributingReadings = new List<SensorReading> { gunshot };
            float combinedConfidence = gunshot.GunshotConfidence;

            if (accelCorroboration != null)
            {
                contributingIds.Add(accelCorroboration.Id);
                contributingReadings.Add(accelCorroboration);
                combinedConfidence = Math.Min(1.0f, combinedConfidence + 0.05f); // Boost for corroboration
            }

            newEvents.Add(new CompositeSensorEvent
            {
                EventId = $"fused-gunshot-{Guid.NewGuid():N}"[..24],
                EventType = CompositeEventType.Gunshot,
                Confidence = combinedConfidence,
                ContributingSensorIds = contributingIds,
                ContributingReadings = contributingReadings,
                Timestamp = gunshot.Timestamp,
                Latitude = gunshot.Latitude,
                Longitude = gunshot.Longitude,
                SeverityScore = 95,
                RequiresImmediateAction = true,
                Description = $"Gunshot detected with {combinedConfidence:P0} confidence. " +
                    $"Acoustic classification at {gunshot.DecibelLevel:F0}dBA" +
                    (accelCorroboration != null ? " + accelerometer shock corroboration." : "."),
                CorrelationId = ""
            });
        }

        // Check for fall composite event
        var fallReadings = userReadings
            .OfType<AccelerometerReading>()
            .Where(a => a.IsFallDetected)
            .ToList();

        foreach (var fall in fallReadings)
        {
            // Check for post-fall stillness (lack of subsequent accel readings with normal values)
            var postFallReadings = userReadings
                .OfType<AccelerometerReading>()
                .Where(a => a.Timestamp > fall.Timestamp && a.GForce < 1.5)
                .Count();

            var severity = fall.GForce >= 5.0 ? 85 : 72;
            var immediate = severity >= 80;

            newEvents.Add(new CompositeSensorEvent
            {
                EventId = $"fused-fall-{Guid.NewGuid():N}"[..24],
                EventType = CompositeEventType.Fall,
                Confidence = Math.Min(0.99f, (float)(fall.GForce / 6.0)),
                ContributingSensorIds = new List<string> { fall.Id },
                ContributingReadings = new List<SensorReading> { fall },
                Timestamp = fall.Timestamp,
                Latitude = fall.Latitude,
                Longitude = fall.Longitude,
                SeverityScore = severity,
                RequiresImmediateAction = immediate,
                Description = $"Fall detected — {fall.GForce:F1}g impact at {fall.FallAngleDegrees:F0} degrees. " +
                    $"Free-fall duration: {fall.FreeFallDurationMs:F0}ms.",
                CorrelationId = ""
            });
        }

        // Check for crash composite event
        var crashReadings = userReadings
            .OfType<AccelerometerReading>()
            .Where(a => a.IsCrashDetected)
            .ToList();

        foreach (var crash in crashReadings)
        {
            newEvents.Add(new CompositeSensorEvent
            {
                EventId = $"fused-crash-{Guid.NewGuid():N}"[..24],
                EventType = CompositeEventType.Crash,
                Confidence = 0.95f,
                ContributingSensorIds = new List<string> { crash.Id },
                ContributingReadings = new List<SensorReading> { crash },
                Timestamp = crash.Timestamp,
                Latitude = crash.Latitude,
                Longitude = crash.Longitude,
                SeverityScore = 95,
                RequiresImmediateAction = true,
                Description = $"Vehicle crash detected — {crash.GForce:F1}g deceleration.",
                CorrelationId = ""
            });
        }

        // Check for medical distress
        var medicalReadings = userReadings
            .OfType<WearableHealthReading>()
            .Where(h => h.SpO2Percent <= SensorThresholds.SpO2CriticalLow ||
                        h.StressIndex >= SensorThresholds.StressIndexCritical ||
                        h.SkinTempCelsius >= SensorThresholds.SkinTempHigh)
            .ToList();

        foreach (var medical in medicalReadings)
        {
            var reasons = new List<string>();
            if (medical.SpO2Percent <= SensorThresholds.SpO2CriticalLow)
                reasons.Add($"SpO2 critically low at {medical.SpO2Percent:F1}%");
            if (medical.StressIndex >= SensorThresholds.StressIndexCritical)
                reasons.Add($"stress index critical at {medical.StressIndex}");
            if (medical.SkinTempCelsius >= SensorThresholds.SkinTempHigh)
                reasons.Add($"skin temperature elevated at {medical.SkinTempCelsius:F1}C");

            newEvents.Add(new CompositeSensorEvent
            {
                EventId = $"fused-medical-{Guid.NewGuid():N}"[..24],
                EventType = CompositeEventType.MedicalDistress,
                Confidence = 0.88f,
                ContributingSensorIds = new List<string> { medical.Id },
                ContributingReadings = new List<SensorReading> { medical },
                Timestamp = medical.Timestamp,
                Latitude = medical.Latitude,
                Longitude = medical.Longitude,
                SeverityScore = 85,
                RequiresImmediateAction = true,
                Description = $"Medical distress: {string.Join(", ", reasons)}. HR={medical.HeartRateBpm:F0}bpm.",
                CorrelationId = ""
            });
        }

        // Check for scream
        var screamReadings = userReadings
            .OfType<AcousticReading>()
            .Where(a => a.Classification == AcousticClassification.Scream &&
                        a.DecibelLevel >= SensorThresholds.ScreamDecibelMin)
            .ToList();

        foreach (var scream in screamReadings)
        {
            newEvents.Add(new CompositeSensorEvent
            {
                EventId = $"fused-scream-{Guid.NewGuid():N}"[..24],
                EventType = CompositeEventType.Scream,
                Confidence = scream.Confidence,
                ContributingSensorIds = new List<string> { scream.Id },
                ContributingReadings = new List<SensorReading> { scream },
                Timestamp = scream.Timestamp,
                Latitude = scream.Latitude,
                Longitude = scream.Longitude,
                SeverityScore = 70,
                RequiresImmediateAction = false,
                Description = $"Scream detected at {scream.DecibelLevel:F0}dBA, duration {scream.DurationMs:F0}ms.",
                CorrelationId = ""
            });
        }

        // Check for fire/smoke visual
        var fireReadings = userReadings
            .OfType<VisualClassification>()
            .Where(v => (v.ClassificationType.Equals("Fire", StringComparison.OrdinalIgnoreCase) ||
                         v.ClassificationType.Equals("Smoke", StringComparison.OrdinalIgnoreCase)) &&
                        v.ClassificationConfidence >= 0.70f)
            .ToList();

        foreach (var fire in fireReadings)
        {
            newEvents.Add(new CompositeSensorEvent
            {
                EventId = $"fused-fire-{Guid.NewGuid():N}"[..24],
                EventType = CompositeEventType.FireVisual,
                Confidence = fire.ClassificationConfidence,
                ContributingSensorIds = new List<string> { fire.Id },
                ContributingReadings = new List<SensorReading> { fire },
                Timestamp = fire.Timestamp,
                Latitude = fire.Latitude,
                Longitude = fire.Longitude,
                SeverityScore = 90,
                RequiresImmediateAction = true,
                Description = $"{fire.ClassificationType} detected with {fire.ClassificationConfidence:P0} confidence from camera {fire.CameraId}.",
                CorrelationId = ""
            });
        }

        // Check for glass break
        var glassBreakReadings = userReadings
            .OfType<AcousticReading>()
            .Where(a => a.Classification == AcousticClassification.GlassBreak)
            .ToList();

        foreach (var gb in glassBreakReadings)
        {
            newEvents.Add(new CompositeSensorEvent
            {
                EventId = $"fused-glass-{Guid.NewGuid():N}"[..24],
                EventType = CompositeEventType.GlassBreak,
                Confidence = gb.Confidence,
                ContributingSensorIds = new List<string> { gb.Id },
                ContributingReadings = new List<SensorReading> { gb },
                Timestamp = gb.Timestamp,
                Latitude = gb.Latitude,
                Longitude = gb.Longitude,
                SeverityScore = 65,
                RequiresImmediateAction = false,
                Description = $"Glass break detected at {gb.DecibelLevel:F0}dBA, frequency {gb.FrequencyHz:F0}Hz.",
                CorrelationId = ""
            });
        }

        // Store new composite events
        foreach (var evt in newEvents)
            _compositeEvents.Add(evt);

        _logger.LogInformation("[MockSensor] Fusion for user {UserId}: {ReadingCount} readings -> {EventCount} composite events",
            userId, userReadings.Count, newEvents.Count);

        var result = newEvents.OrderByDescending(e => e.SeverityScore).ToList();
        return Task.FromResult<IReadOnlyList<CompositeSensorEvent>>(result);
    }

    // ═══════════════════════════════════════════════════════════════
    // Query
    // ═══════════════════════════════════════════════════════════════

    public Task<IReadOnlyList<SensorReading>> GetRecentReadingsAsync(
        string userId, SensorType? type, int limit, CancellationToken ct = default)
    {
        var query = _readings.Where(r => r.UserId == userId);
        if (type.HasValue) query = query.Where(r => r.SensorType == type.Value);

        var result = query
            .OrderByDescending(r => r.Timestamp)
            .Take(limit)
            .ToList();

        return Task.FromResult<IReadOnlyList<SensorReading>>(result);
    }

    public Task<IReadOnlyList<CompositeSensorEvent>> GetActiveCompositeEventsAsync(
        string userId, CancellationToken ct = default)
    {
        // In mock, "active" events are those from the last 5 minutes that require action
        var cutoff = DateTime.UtcNow.AddMinutes(-5);
        var result = _compositeEvents
            .Where(e => e.Timestamp >= cutoff)
            .OrderByDescending(e => e.SeverityScore)
            .ToList();

        return Task.FromResult<IReadOnlyList<CompositeSensorEvent>>(result);
    }

    // ═══════════════════════════════════════════════════════════════
    // Sensor Management
    // ═══════════════════════════════════════════════════════════════

    public Task<IReadOnlyDictionary<SensorType, SensorStatus>> GetSensorStatusAsync(
        string userId, string deviceId, CancellationToken ct = default)
    {
        // Return realistic mock sensor statuses for a typical smartphone
        var statuses = new Dictionary<SensorType, SensorStatus>
        {
            [SensorType.Accelerometer] = SensorStatus.Active,
            [SensorType.Microphone] = SensorStatus.Active,
            [SensorType.Camera] = SensorStatus.Active,
            [SensorType.Barometer] = SensorStatus.Active,
            [SensorType.AmbientSound] = SensorStatus.Active,
            [SensorType.GPS] = SensorStatus.Active,
            [SensorType.Gyroscope] = SensorStatus.Active,
            [SensorType.Magnetometer] = SensorStatus.Active,
            [SensorType.Proximity] = SensorStatus.Active,
            [SensorType.LightSensor] = SensorStatus.Active,
            [SensorType.Thermometer] = SensorStatus.NotAvailable, // Not on most phones
            [SensorType.WearableHealth] = deviceId.Contains("watch", StringComparison.OrdinalIgnoreCase)
                ? SensorStatus.Active
                : SensorStatus.NotAvailable
        };

        _logger.LogDebug("[MockSensor] Sensor status for {UserId}/{DeviceId}: {ActiveCount} active, {TotalCount} total",
            userId, deviceId, statuses.Count(s => s.Value == SensorStatus.Active), statuses.Count);

        return Task.FromResult<IReadOnlyDictionary<SensorType, SensorStatus>>(statuses);
    }

    public Task<bool> CalibrateSensorAsync(string userId, string deviceId, SensorType type, CancellationToken ct = default)
    {
        _logger.LogInformation("[MockSensor] Calibrating {SensorType} on {DeviceId} for user {UserId}",
            type, deviceId, userId);

        // Mock: always succeed unless the sensor type is not available
        var success = type != SensorType.Thermometer;
        return Task.FromResult(success);
    }
}
