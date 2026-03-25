// SensorModels.kt — Android domain models for TheWatch sensor fusion subsystem.
//
// Mirrors the C# models in TheWatch.Shared for cross-platform consistency.
// All sensor readings are captured on-device and transmitted to the backend API.
// No raw audio or video leaves the device — only classified events and telemetry.
//
// Standards referenced:
//   - ANSI S1.4-2014: dBA weighting for acoustic decibel measurements
//   - MIL-STD-1474E: Impulse noise thresholds for gunshot classification
//   - NFPA 1584: Stress index thresholds for responder rehabilitation
//   - FCC E911 Z-axis: Floor level for vertical location in multi-story buildings
//
// Example — recording a fall:
//   val reading = AccelerometerReading(
//       userId = "u-123", deviceId = "pixel-8-xyz",
//       gForce = 4.2, isFallDetected = true,
//       fallAngleDegrees = 78.5, freeFallDurationMs = 420.0,
//       latitude = 32.7767, longitude = -96.7970
//   )
//   sensorRepository.recordAccelerometer(reading)
//
// Example — fusing sensor data:
//   val events = sensorRepository.fuseSensorData(userId = "u-123", windowSeconds = 10)
//   events.filter { it.requiresImmediateAction }.forEach { sosTrigger.fire(it) }

package com.thewatch.app.data.model

// ═══════════════════════════════════════════════════════════════
// Enums
// ═══════════════════════════════════════════════════════════════

/**
 * Physical or virtual sensor type producing readings on the user's device.
 * Maps to Android SensorManager sensor types.
 */
enum class SensorType {
    Accelerometer,
    Microphone,
    Camera,
    Barometer,
    AmbientSound,
    WearableHealth,
    GPS,
    Gyroscope,
    Magnetometer,
    Proximity,
    LightSensor,
    Thermometer
}

/**
 * Composite events produced by fusing multiple sensor readings within a time window.
 * Each type has defined contributing sensor combinations and confidence thresholds.
 */
enum class CompositeEventType {
    Gunshot,
    Fall,
    FireVisual,
    Explosion,
    GlassBreak,
    Crash,
    FloodVisual,
    Scream,
    MedicalDistress,
    IntruderDetected,
    EnvironmentalAnomaly
}

/**
 * Operational status of a sensor on a specific device.
 */
enum class SensorStatus {
    Active,
    Inactive,
    Calibrating,
    Error,
    PermissionDenied,
    NotAvailable
}

/**
 * Acoustic event classification from on-device audio ML model (TensorFlow Lite).
 * Reference: ANSI S1.4-2014 (dBA weighting), MIL-STD-1474E (impulse noise).
 */
enum class AcousticClassification {
    Gunshot,
    GlassBreak,
    Scream,
    RaisedVoices,
    ImpactSound,
    Explosion,
    Silence,
    AmbientNormal,
    Alarm,
    Siren
}

// ═══════════════════════════════════════════════════════════════
// Threshold Constants
// ═══════════════════════════════════════════════════════════════

/**
 * Threshold constants used by the sensor fusion engine.
 * Derived from published standards and device manufacturer specifications.
 */
object SensorThresholds {
    /** Minimum g-force to classify as a fall (Apple Watch / Pixel use ~3.0g). */
    const val FALL_G_FORCE_THRESHOLD: Double = 3.0
    /** Minimum g-force to classify as a vehicle crash (NHTSA frontal: 10-30g). */
    const val CRASH_G_FORCE_THRESHOLD: Double = 10.0
    /** Minimum acoustic confidence to report gunshot (per MIL-STD-1474E). */
    const val GUNSHOT_CONFIDENCE_MIN: Double = 0.85
    /** Minimum dBA for scream classification (per ANSI S1.4 A-weighted). */
    const val SCREAM_DECIBEL_MIN: Double = 85.0
    /** HR spike multiplier relative to resting (HR > resting * 1.5 without exercise). */
    const val HEART_RATE_SPIKE_MULTIPLIER: Double = 1.5
    /** Stress index critical threshold (adapted from NFPA 1584). */
    const val STRESS_INDEX_CRITICAL: Int = 80
    /** SpO2 critical low percentage (per WHO pulse oximetry guidelines). */
    const val SPO2_CRITICAL_LOW: Double = 90.0
    /** Skin temperature high threshold in Celsius. */
    const val SKIN_TEMP_HIGH: Double = 39.0
}

// ═══════════════════════════════════════════════════════════════
// Sensor Reading Models
// ═══════════════════════════════════════════════════════════════

/**
 * Base sensor reading from any device sensor.
 * Contains common fields shared by all sensor types: identity, location, timing, confidence.
 */
data class SensorReading(
    val readingId: String = "",
    val sensorType: SensorType = SensorType.Accelerometer,
    val deviceId: String = "",
    val userId: String = "",
    val timestamp: String = "",
    val rawValue: Double = 0.0,
    val unit: String = "",
    val confidence: Float = 1.0f,
    val calibrationOffset: Double = 0.0,
    val latitude: Double = 0.0,
    val longitude: Double = 0.0,
    val altitudeMeters: Double = 0.0,
    /** Floor level per FCC E911 Z-axis. Null if undetermined. */
    val floorLevel: Int? = null
)

/**
 * Accelerometer reading with fall and crash detection.
 * Fall: free-fall phase + impact >3.0g. Crash: sudden deceleration >10.0g with GPS speed context.
 *
 * Example:
 *   val reading = AccelerometerReading(
 *       userId = "u-123", gForce = 4.2,
 *       isFallDetected = true, fallAngleDegrees = 78.5
 *   )
 */
data class AccelerometerReading(
    val readingId: String = "",
    val sensorType: SensorType = SensorType.Accelerometer,
    val deviceId: String = "",
    val userId: String = "",
    val timestamp: String = "",
    val rawValue: Double = 0.0,
    val unit: String = "g",
    val confidence: Float = 1.0f,
    val calibrationOffset: Double = 0.0,
    val latitude: Double = 0.0,
    val longitude: Double = 0.0,
    val altitudeMeters: Double = 0.0,
    val floorLevel: Int? = null,
    /** Peak g-force magnitude: sqrt(x² + y² + z²). */
    val gForce: Double = 0.0,
    /** True when GForce > FallGForceThreshold (3.0g) with preceding free-fall. */
    val isFallDetected: Boolean = false,
    /** True when GForce > CrashGForceThreshold (10.0g) with GPS speed context. */
    val isCrashDetected: Boolean = false,
    /** Peak single-sample impact g-force. */
    val impactGForce: Double = 0.0,
    /** Angle of fall in degrees from vertical (0=upright, 90=horizontal). */
    val fallAngleDegrees: Double = 0.0,
    /** Free-fall duration in milliseconds before impact. */
    val freeFallDurationMs: Double = 0.0
)

/**
 * Acoustic event reading from microphone or ambient sound sensor.
 * Classification by TensorFlow Lite model. Audio processed in 50ms windows.
 * No raw audio leaves the device.
 *
 * Reference: ANSI S1.4-2014 for dBA, MIL-STD-1474E for impulse noise.
 */
data class AcousticReading(
    val readingId: String = "",
    val sensorType: SensorType = SensorType.Microphone,
    val deviceId: String = "",
    val userId: String = "",
    val timestamp: String = "",
    val rawValue: Double = 0.0,
    val unit: String = "dBA",
    val confidence: Float = 1.0f,
    val calibrationOffset: Double = 0.0,
    val latitude: Double = 0.0,
    val longitude: Double = 0.0,
    val altitudeMeters: Double = 0.0,
    val floorLevel: Int? = null,
    /** Classified acoustic event type. */
    val classification: AcousticClassification = AcousticClassification.AmbientNormal,
    /** Sound pressure level in dBA (A-weighted per ANSI S1.4-2014). */
    val decibelLevel: Double = 0.0,
    /** Gunshot confidence (0.0-1.0). Must exceed 0.85 to trigger composite event. */
    val gunshotConfidence: Float = 0.0f,
    /** Dominant frequency in Hz. */
    val frequencyHz: Double = 0.0,
    /** Event duration in milliseconds. */
    val durationMs: Double = 0.0,
    /** Direction to sound source in degrees (0-360, 0=device front). */
    val directionDegrees: Double = 0.0,
    /** Estimated distance to sound source in meters. */
    val distanceEstimateMeters: Double = 0.0
)

/**
 * Wearable health reading from smartwatch or fitness band.
 * Combines heart rate, SpO2, skin temperature, and stress index.
 * Medical distress inferred when multiple indicators exceed thresholds.
 *
 * Reference: NFPA 1584 for stress index, WHO for SpO2 thresholds.
 */
data class WearableHealthReading(
    val readingId: String = "",
    val sensorType: SensorType = SensorType.WearableHealth,
    val deviceId: String = "",
    val userId: String = "",
    val timestamp: String = "",
    val rawValue: Double = 0.0,
    val unit: String = "bpm",
    val confidence: Float = 1.0f,
    val calibrationOffset: Double = 0.0,
    val latitude: Double = 0.0,
    val longitude: Double = 0.0,
    val altitudeMeters: Double = 0.0,
    val floorLevel: Int? = null,
    /** Current heart rate in bpm. */
    val heartRateBpm: Double = 0.0,
    /** Resting heart rate in bpm (7-day rolling average during sleep). */
    val restingHeartRateBpm: Double = 0.0,
    /** True when HR > resting * 1.5 without exercise. */
    val isHeartRateSpike: Boolean = false,
    /** Blood oxygen saturation %. Below 90% = critical. */
    val spO2Percent: Double = 0.0,
    /** Skin temperature in Celsius. Above 39°C = fever/hyperthermia. */
    val skinTempCelsius: Double = 0.0,
    /** Composite stress index 0-100 (per NFPA 1584). Above 80 = critical. */
    val stressIndex: Int = 0,
    /** Whether user is currently exercising (suppresses HR spike alerts). */
    val isExercising: Boolean = false,
    /** Steps in last 5 minutes. Brisk walking: 500-650. */
    val stepCountLast5Min: Int = 0
)

/**
 * Visual classification from on-device camera ML model (TensorFlow Lite).
 * No raw images transmitted — only classification results.
 *
 * Example:
 *   val classification = VisualClassification(
 *       classificationType = "Fire",
 *       classificationConfidence = 0.93f,
 *       cameraId = "rear"
 *   )
 */
data class VisualClassification(
    val readingId: String = "",
    val sensorType: SensorType = SensorType.Camera,
    val deviceId: String = "",
    val userId: String = "",
    val timestamp: String = "",
    val rawValue: Double = 0.0,
    val unit: String = "",
    val confidence: Float = 1.0f,
    val calibrationOffset: Double = 0.0,
    val latitude: Double = 0.0,
    val longitude: Double = 0.0,
    val altitudeMeters: Double = 0.0,
    val floorLevel: Int? = null,
    /** Detection type: "Smoke", "Fire", "Flood", "Person", "Vehicle", "Weapon". */
    val classificationType: String = "",
    /** Model confidence 0.0-1.0. Threshold: >0.70 alerting, >0.90 autonomous. */
    val classificationConfidence: Float = 0.0f,
    /** Bounding box X (normalized 0.0-1.0, left=0). */
    val boundingBoxX: Double = 0.0,
    /** Bounding box Y (normalized 0.0-1.0, top=0). */
    val boundingBoxY: Double = 0.0,
    /** Bounding box width (normalized 0.0-1.0). */
    val boundingBoxW: Double = 0.0,
    /** Bounding box height (normalized 0.0-1.0). */
    val boundingBoxH: Double = 0.0,
    /** Timestamp of the classified video frame. */
    val frameTimestamp: String = "",
    /** Camera ID: "front", "rear", "external-001", or CCTV camera ID. */
    val cameraId: String = ""
)

/**
 * Composite sensor event produced by fusing multiple raw readings within a time window.
 * The fusion engine correlates readings by time, location, and type for high-confidence events.
 *
 * Example:
 *   val event = CompositeSensorEvent(
 *       eventType = CompositeEventType.Gunshot,
 *       confidence = 0.92f,
 *       severityScore = 95,
 *       requiresImmediateAction = true,
 *       description = "Gunshot detected with 92% confidence. Acoustic + accelerometer corroboration."
 *   )
 */
data class CompositeSensorEvent(
    val eventId: String = "",
    val eventType: CompositeEventType = CompositeEventType.Fall,
    /** Combined confidence from all contributing sensors (0.0-1.0). */
    val confidence: Float = 0.0f,
    /** IDs of sensor readings that contributed to this event. */
    val contributingSensorIds: List<String> = emptyList(),
    /** UTC timestamp when the fusion engine produced this event. */
    val timestamp: String = "",
    /** Latitude of the event (centroid of contributing readings). */
    val latitude: Double = 0.0,
    /** Longitude of the event (centroid of contributing readings). */
    val longitude: Double = 0.0,
    /** Severity 0-100. 0-30=info, 31-60=warning, 61-80=urgent, 81-100=critical. */
    val severityScore: Int = 0,
    /** True when SeverityScore >= 80 or event type is inherently life-threatening. */
    val requiresImmediateAction: Boolean = false,
    /** Human-readable description for display. */
    val description: String = "",
    /** Correlation ID linking to SOS/response chain. */
    val correlationId: String = ""
) {
    /** Display-friendly severity label. */
    val severityLabel: String
        get() = when (severityScore) {
            in 0..30 -> "Info"
            in 31..60 -> "Warning"
            in 61..80 -> "Urgent"
            else -> "Critical"
        }
}
