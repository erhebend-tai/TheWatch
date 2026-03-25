// SensorModels.cs — domain models for the sensor fusion subsystem.
//
// These models represent raw sensor readings from device hardware, processed acoustic
// events, wearable health telemetry, visual classifications, and fused composite events
// that trigger emergency response workflows.
//
// Architecture:
//   ┌─────────────────┐   ┌──────────────────┐   ┌─────────────────────────┐
//   │ Device Sensors   │──▶│ ISensorFusionPort │──▶│ CompositeSensorEvent    │
//   │ (Accel, Mic,    │   │ .FuseSensorData() │   │ (Gunshot, Fall, Crash)  │
//   │  Camera, Health) │   └──────────────────┘   └────────────┬────────────┘
//   └─────────────────┘                                        │
//                                                    Feeds into SOS pipeline
//                                                    and responder dispatch
//
// Standards referenced:
//   - FCC E911 Z-axis: FloorLevel for vertical location in multi-story buildings
//   - ANSI S1.4-2014: dBA weighting for acoustic decibel measurements
//   - MIL-STD-1474E: Impulse noise thresholds for gunshot classification
//   - NFPA 1584: Stress index thresholds for responder rehabilitation
//   - IEEE 1451: Smart transducer interface metadata conventions
//
// Example — recording an accelerometer fall event:
//   var reading = new AccelerometerReading
//   {
//       UserId = "u-123",
//       DeviceId = "iphone-14-abc",
//       GForce = 4.2,
//       IsFallDetected = true,  // exceeds FallGForceThreshold (3.0g)
//       FallAngleDegrees = 78.5,
//       FreeFallDurationMs = 420,
//       Latitude = 32.7767,
//       Longitude = -96.7970
//   };
//   await sensorPort.RecordAccelerometerAsync(reading);
//
// Example — fusing sensor data into composite events:
//   var events = await sensorPort.FuseSensorDataAsync("u-123", TimeSpan.FromSeconds(10));
//   // Returns: [CompositeSensorEvent { EventType = Fall, Confidence = 0.94, ... }]

using TheWatch.Shared.Enums;

namespace TheWatch.Shared.Domain.Models;

/// <summary>
/// Threshold constants used by the sensor fusion engine to classify events.
/// These values are derived from published standards and device manufacturer specifications.
/// All thresholds are configurable at runtime via IConfiguration but default to these values.
/// </summary>
public static class SensorThresholds
{
    /// <summary>
    /// Minimum g-force to classify as a fall event.
    /// Based on research: typical fall impact is 3-6g, normal daily activity rarely exceeds 2g.
    /// Apple Watch uses approximately 3g; we match that threshold.
    /// </summary>
    public const double FallGForceThreshold = 3.0;

    /// <summary>
    /// Minimum g-force to classify as a vehicle crash event.
    /// NHTSA frontal crash tests produce 10-30g at occupant level.
    /// Apple Crash Detection uses a similar ~10g threshold with gyroscope corroboration.
    /// </summary>
    public const double CrashGForceThreshold = 10.0;

    /// <summary>
    /// Minimum acoustic classifier confidence to report a gunshot event.
    /// Set high (0.85) to minimize false positives from fireworks, car backfires, door slams.
    /// Per MIL-STD-1474E impulse noise signature: rise time <10ms, peak >140 dB SPL.
    /// </summary>
    public const double GunshotConfidenceMin = 0.85;

    /// <summary>
    /// Minimum dBA level to classify sustained vocalization as a scream.
    /// Normal conversation: 55-65 dBA. Raised voices: 75-85 dBA. Scream: >85 dBA.
    /// Per ANSI S1.4 A-weighted sound level measurement.
    /// </summary>
    public const double ScreamDecibelMin = 85.0;

    /// <summary>
    /// Heart rate spike multiplier relative to resting heart rate.
    /// HR > resting * 1.5 without detected exercise indicates stress or medical event.
    /// Example: resting 70 bpm, spike threshold = 105 bpm.
    /// </summary>
    public const double HeartRateSpikeMultiplier = 1.5;

    /// <summary>
    /// Stress index value (0-100) at or above which medical distress is indicated.
    /// Adapted from NFPA 1584 rehabilitation criteria for emergency responders.
    /// Combines heart rate variability, skin conductance, and temperature.
    /// </summary>
    public const int StressIndexCritical = 80;

    /// <summary>
    /// SpO2 percentage at or below which critical hypoxemia is indicated.
    /// Normal: 95-100%. Below 90% requires immediate medical attention.
    /// Per WHO pulse oximetry guidelines.
    /// </summary>
    public const double SpO2CriticalLow = 90.0;

    /// <summary>
    /// Skin temperature (Celsius) at or above which fever/hyperthermia is indicated.
    /// Normal skin temp: 33-36C. Core body temp fever: >38C. Skin >39C = significant.
    /// </summary>
    public const double SkinTempHigh = 39.0;
}

/// <summary>
/// Base sensor reading from any device sensor.
/// Contains the common fields shared by all sensor types: identity, location, timing, and confidence.
/// </summary>
public class SensorReading
{
    // ── Identity ────────────────────────────────────────────────

    /// <summary>Unique reading ID.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Type of sensor that produced this reading.</summary>
    public SensorType SensorType { get; set; }

    /// <summary>Device ID that produced this reading (e.g., "iphone-14-abc", "pixel-8-xyz").</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>User ID who owns the device.</summary>
    public string UserId { get; set; } = string.Empty;

    // ── WHEN ────────────────────────────────────────────────────

    /// <summary>UTC timestamp when the reading was captured on the device.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // ── WHAT ────────────────────────────────────────────────────

    /// <summary>Raw numeric value from the sensor (interpretation depends on SensorType and Unit).</summary>
    public double RawValue { get; set; }

    /// <summary>
    /// Unit of measurement for RawValue.
    /// Examples: "g" (accelerometer), "dBA" (microphone), "hPa" (barometer),
    /// "bpm" (heart rate), "lux" (light), "°C" (temperature), "µT" (magnetometer).
    /// </summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>
    /// Confidence in the reading accuracy (0.0 = no confidence, 1.0 = fully calibrated).
    /// Accounts for sensor noise, calibration state, and environmental interference.
    /// </summary>
    public float Confidence { get; set; } = 1.0f;

    /// <summary>
    /// Calibration offset applied to RawValue. The displayed value = RawValue + CalibrationOffset.
    /// Set during CalibrateSensorAsync(). Defaults to 0 (no offset).
    /// </summary>
    public double CalibrationOffset { get; set; }

    // ── WHERE ───────────────────────────────────────────────────

    /// <summary>Latitude of the device when the reading was captured (WGS-84).</summary>
    public double Latitude { get; set; }

    /// <summary>Longitude of the device when the reading was captured (WGS-84).</summary>
    public double Longitude { get; set; }

    /// <summary>Altitude in meters above sea level (GPS-derived or barometric).</summary>
    public double AltitudeMeters { get; set; }

    /// <summary>
    /// Floor level within a building (0 = ground floor, negative = basement).
    /// Per FCC E911 Z-axis vertical location requirements for indoor positioning.
    /// Derived from barometric pressure differentials or building floor plans.
    /// Null if floor level cannot be determined.
    /// </summary>
    public int? FloorLevel { get; set; }
}

/// <summary>
/// Accelerometer reading with fall and crash detection fields.
/// Fall detection: free-fall phase followed by impact exceeding FallGForceThreshold (3.0g).
/// Crash detection: sudden deceleration exceeding CrashGForceThreshold (10.0g) with GPS speed context.
/// </summary>
public class AccelerometerReading : SensorReading
{
    /// <summary>
    /// Peak g-force magnitude (vector sum of X, Y, Z axes).
    /// sqrt(x² + y² + z²). Normal standing = ~1.0g. Walking = ~1.2g.
    /// </summary>
    public double GForce { get; set; }

    /// <summary>
    /// Whether this reading indicates a fall event.
    /// True when GForce exceeds SensorThresholds.FallGForceThreshold (3.0g)
    /// AND preceded by a free-fall phase (near-zero g for >200ms).
    /// </summary>
    public bool IsFallDetected { get; set; }

    /// <summary>
    /// Whether this reading indicates a vehicle crash event.
    /// True when GForce exceeds SensorThresholds.CrashGForceThreshold (10.0g)
    /// AND GPS indicates prior movement >25 mph.
    /// </summary>
    public bool IsCrashDetected { get; set; }

    /// <summary>
    /// Peak impact g-force during the event (may differ from GForce if GForce is average).
    /// This is the single highest sample within the impact window.
    /// </summary>
    public double ImpactGForce { get; set; }

    /// <summary>
    /// Angle of the body/device during fall (degrees from vertical).
    /// 0 = upright, 90 = horizontal, 180 = inverted.
    /// Derived from gyroscope integration during the fall event.
    /// </summary>
    public double FallAngleDegrees { get; set; }

    /// <summary>
    /// Duration of the free-fall phase in milliseconds (near-zero g before impact).
    /// Typical human fall: 300-600ms. Longer durations may indicate fall from height.
    /// </summary>
    public double FreeFallDurationMs { get; set; }
}

/// <summary>
/// Acoustic event reading from microphone or ambient sound sensor.
/// Classification performed by on-device ML model (Core ML / TensorFlow Lite).
/// Audio processed in 50ms windows with 25ms overlap. No raw audio leaves the device.
/// </summary>
public class AcousticReading : SensorReading
{
    /// <summary>
    /// Classified acoustic event type from the on-device ML model.
    /// See AcousticClassification enum for detailed descriptions and standards references.
    /// </summary>
    public AcousticClassification Classification { get; set; }

    /// <summary>
    /// Sound pressure level in dBA (A-weighted per ANSI S1.4-2014).
    /// A-weighting approximates human ear frequency response.
    /// Whisper: ~30 dBA, Conversation: ~60 dBA, Scream: >85 dBA, Gunshot: >140 dBA.
    /// </summary>
    public double DecibelLevel { get; set; }

    /// <summary>
    /// Confidence that the acoustic event is a gunshot (0.0 to 1.0).
    /// Must exceed SensorThresholds.GunshotConfidenceMin (0.85) to trigger composite event.
    /// Evaluated even when Classification is not Gunshot to catch edge cases.
    /// </summary>
    public float GunshotConfidence { get; set; }

    /// <summary>
    /// Dominant frequency of the acoustic event in Hz.
    /// Gunshot: broadband 500-4000Hz. Glass break: 5000-20000Hz. Scream: 2000-4000Hz.
    /// </summary>
    public double FrequencyHz { get; set; }

    /// <summary>
    /// Duration of the acoustic event in milliseconds.
    /// Gunshot impulse: <2ms. Glass break: 100-500ms. Scream: >500ms.
    /// </summary>
    public double DurationMs { get; set; }

    /// <summary>
    /// Estimated direction of the sound source in degrees (0-360, 0 = device front).
    /// Computed from multi-microphone time-of-arrival differences on supported devices.
    /// Null or 0 on single-microphone devices.
    /// </summary>
    public double DirectionDegrees { get; set; }

    /// <summary>
    /// Estimated distance to the sound source in meters.
    /// Computed from sound attenuation model (inverse square law with environmental correction).
    /// Highly approximate — accuracy depends on environment and source power.
    /// </summary>
    public double DistanceEstimateMeters { get; set; }
}

/// <summary>
/// Health reading from wearable device (smartwatch, fitness band).
/// Combines heart rate, blood oxygen, skin temperature, and computed stress index.
/// Medical distress is inferred when multiple indicators exceed thresholds simultaneously.
/// </summary>
public class WearableHealthReading : SensorReading
{
    /// <summary>Current heart rate in beats per minute from optical sensor.</summary>
    public double HeartRateBpm { get; set; }

    /// <summary>
    /// User's resting heart rate in bpm (rolling 7-day average during sleep).
    /// Used as baseline for spike detection.
    /// </summary>
    public double RestingHeartRateBpm { get; set; }

    /// <summary>
    /// Whether current heart rate constitutes a spike relative to resting rate.
    /// True when HeartRateBpm > RestingHeartRateBpm * SensorThresholds.HeartRateSpikeMultiplier (1.5)
    /// AND IsExercising is false.
    /// </summary>
    public bool IsHeartRateSpike { get; set; }

    /// <summary>
    /// Blood oxygen saturation percentage (SpO2) from pulse oximeter.
    /// Normal: 95-100%. Below SensorThresholds.SpO2CriticalLow (90%) = critical.
    /// Per WHO pulse oximetry screening guidelines.
    /// </summary>
    public double SpO2Percent { get; set; }

    /// <summary>
    /// Skin surface temperature in Celsius from wearable thermistor.
    /// Normal: 33-36°C. Above SensorThresholds.SkinTempHigh (39°C) = fever/hyperthermia.
    /// </summary>
    public double SkinTempCelsius { get; set; }

    /// <summary>
    /// Composite stress index (0-100) combining HRV, skin conductance, and temperature.
    /// Adapted from NFPA 1584 responder rehabilitation thresholds.
    /// Above SensorThresholds.StressIndexCritical (80) = critical stress.
    /// 0 = fully relaxed, 100 = maximum physiological stress.
    /// </summary>
    public int StressIndex { get; set; }

    /// <summary>
    /// Whether the user is currently engaged in physical exercise.
    /// Determined by accelerometer step cadence, heart rate pattern, and activity recognition.
    /// When true, heart rate spikes are expected and do not trigger medical distress.
    /// </summary>
    public bool IsExercising { get; set; }

    /// <summary>
    /// Number of steps detected in the last 5 minutes.
    /// Used to corroborate IsExercising and distinguish exercise from distress.
    /// Typical brisk walking: 100-130 steps/min = 500-650 in 5 minutes.
    /// </summary>
    public int StepCountLast5Min { get; set; }
}

/// <summary>
/// Visual classification result from on-device camera ML model.
/// The model runs inference on camera frames locally — no raw images are transmitted.
/// Bounding box coordinates are normalized to frame dimensions.
/// </summary>
public class VisualClassification : SensorReading
{
    /// <summary>
    /// Type of object or condition detected.
    /// Known values: "Smoke", "Fire", "Flood", "Person", "Vehicle", "Weapon".
    /// Extensible string to support future model classes without enum changes.
    /// </summary>
    public string ClassificationType { get; set; } = string.Empty;

    /// <summary>
    /// Model confidence in the classification (0.0 to 1.0).
    /// Typical production threshold: >0.70 for alerting, >0.90 for autonomous action.
    /// </summary>
    public float ClassificationConfidence { get; set; }

    /// <summary>Bounding box X coordinate (normalized 0.0-1.0, left edge of frame = 0).</summary>
    public double BoundingBoxX { get; set; }

    /// <summary>Bounding box Y coordinate (normalized 0.0-1.0, top edge of frame = 0).</summary>
    public double BoundingBoxY { get; set; }

    /// <summary>Bounding box width (normalized 0.0-1.0).</summary>
    public double BoundingBoxW { get; set; }

    /// <summary>Bounding box height (normalized 0.0-1.0).</summary>
    public double BoundingBoxH { get; set; }

    /// <summary>Timestamp of the video frame that was classified (may differ from reading Timestamp if processing is delayed).</summary>
    public DateTime FrameTimestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Identifier of the camera that captured the frame.
    /// "front", "rear", "external-001", or CCTV camera ID for integrated systems.
    /// </summary>
    public string CameraId { get; set; } = string.Empty;
}

/// <summary>
/// Composite sensor event produced by fusing multiple raw sensor readings within a time window.
/// The fusion engine correlates readings by time, location, and type to produce high-confidence events.
///
/// Example: a Gunshot composite event might fuse:
///   - AcousticReading (Classification=Gunshot, Confidence=0.92)
///   - AccelerometerReading (shock wave vibration)
///   - VisualClassification (muzzle flash detected)
/// into a single CompositeSensorEvent with combined confidence 0.96.
/// </summary>
public class CompositeSensorEvent
{
    // ── Identity ────────────────────────────────────────────────

    /// <summary>Unique event ID.</summary>
    public string EventId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Type of composite event detected.</summary>
    public CompositeEventType EventType { get; set; }

    // ── WHAT ────────────────────────────────────────────────────

    /// <summary>
    /// Combined confidence from all contributing sensors (0.0 to 1.0).
    /// Computed using weighted Dempster-Shafer evidence combination or naive Bayes fusion.
    /// Higher when multiple independent sensors corroborate the same event.
    /// </summary>
    public float Confidence { get; set; }

    /// <summary>IDs of the sensor readings that contributed to this composite event.</summary>
    public List<string> ContributingSensorIds { get; set; } = new();

    /// <summary>The actual sensor readings that were fused into this event (for audit/review).</summary>
    public List<SensorReading> ContributingReadings { get; set; } = new();

    // ── WHEN ────────────────────────────────────────────────────

    /// <summary>UTC timestamp when the composite event was produced by the fusion engine.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // ── WHERE ───────────────────────────────────────────────────

    /// <summary>Latitude of the event (centroid of contributing readings).</summary>
    public double Latitude { get; set; }

    /// <summary>Longitude of the event (centroid of contributing readings).</summary>
    public double Longitude { get; set; }

    // ── Severity & Action ────────────────────────────────────────

    /// <summary>
    /// Severity score (0-100) combining event type danger level and confidence.
    /// 0-30 = informational, 31-60 = warning, 61-80 = urgent, 81-100 = critical/life-safety.
    /// </summary>
    public int SeverityScore { get; set; }

    /// <summary>
    /// Whether this event requires immediate action (auto-SOS trigger, 911 dispatch).
    /// True when SeverityScore >= 80 or event type is inherently life-threatening
    /// (Gunshot, Crash, Explosion, MedicalDistress with critical vitals).
    /// </summary>
    public bool RequiresImmediateAction { get; set; }

    /// <summary>
    /// Human-readable description of the composite event for display in the app and dashboard.
    /// Example: "Gunshot detected with 92% confidence. Acoustic + accelerometer corroboration."
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Correlation ID linking this event to the broader SOS/response chain.
    /// Matches ResponseRequest.RequestId when the event triggers an SOS.
    /// Used for end-to-end audit trail querying.
    /// </summary>
    public string CorrelationId { get; set; } = string.Empty;
}
