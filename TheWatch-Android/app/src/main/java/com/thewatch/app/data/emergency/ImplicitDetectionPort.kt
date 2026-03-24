/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         ImplicitDetectionPort.kt                               │
 * │ Purpose:      Hexagonal port interface for implicit emergency        │
 * │               detection. Detects potential emergencies from sensor   │
 * │               data without explicit user trigger: fall detection     │
 * │               (accelerometer), elevated HR + no movement, sudden    │
 * │               stop after high speed (crash detection).               │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: SensorManager (accelerometer, gyroscope),              │
 * │               HealthPort (heart rate), LocationRepository (speed)    │
 * │                                                                      │
 * │ Adapter tiers:                                                       │
 * │   - Mock:   Simulated detections on demand. Dev/test.                │
 * │   - Native: Real sensor fusion using accelerometer + gyroscope +    │
 * │             health data + location speed.                            │
 * │   - Live:   Native + ML model for improved accuracy (future).        │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   val detector: ImplicitDetectionPort = hiltGet()                    │
 * │   detector.startMonitoring()                                         │
 * │   detector.observeDetections().collect { event ->                    │
 * │       when (event.type) {                                            │
 * │           DetectionType.FALL -> showSOSConfirmationDialog(event)     │
 * │           DetectionType.CRASH -> triggerSOSWithDelay(30_000, event)  │
 * │       }                                                              │
 * │   }                                                                  │
 * │                                                                      │
 * │ NOTE: Fall detection uses a threshold of ~3g peak acceleration       │
 * │ followed by < 0.5g (freefall-to-impact pattern). False positives    │
 * │ are common during vigorous exercise — always show a confirmation    │
 * │ dialog with auto-trigger countdown (30 seconds). Crash detection    │
 * │ uses sudden deceleration from > 25 mph to < 2 mph in < 3 seconds.  │
 * │ Google Pixel and Samsung phones have built-in crash detection —     │
 * │ we should detect and defer to the OS implementation when present.   │
 * │ iPhone has similar via SOS Emergency — not applicable on Android.   │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.emergency

import kotlinx.coroutines.flow.Flow

/**
 * Types of implicitly detected emergencies.
 */
enum class DetectionType {
    /** Free-fall followed by impact — possible fall. */
    FALL,
    /** Elevated heart rate with no movement — possible medical event. */
    ELEVATED_HR_NO_MOVEMENT,
    /** Sudden deceleration from high speed — possible vehicle crash. */
    CRASH,
    /** Prolonged inactivity after movement — possible incapacitation. */
    INACTIVITY_AFTER_MOVEMENT,
    /** Rapid repeated taps detected — possible distress signal. */
    DISTRESS_TAPS,
    /** Device thrown (high rotation + acceleration) — possible altercation. */
    DEVICE_THROWN
}

/**
 * Severity of the detection — determines response urgency.
 */
enum class DetectionSeverity {
    /** Low confidence — show subtle notification. */
    LOW,
    /** Medium confidence — show confirmation dialog. */
    MEDIUM,
    /** High confidence — show confirmation dialog with 30s auto-trigger. */
    HIGH,
    /** Critical — auto-trigger SOS immediately (e.g., confirmed crash). */
    CRITICAL
}

/**
 * An implicit emergency detection event.
 */
data class DetectionEvent(
    /** Unique ID for this detection event. */
    val id: String,
    /** Type of emergency detected. */
    val type: DetectionType,
    /** Confidence score (0.0 to 1.0). */
    val confidence: Double,
    /** Severity based on confidence and context. */
    val severity: DetectionSeverity,
    /** Epoch millis when the detection occurred. */
    val timestamp: Long = System.currentTimeMillis(),
    /** Last known latitude at time of detection. */
    val latitude: Double? = null,
    /** Last known longitude at time of detection. */
    val longitude: Double? = null,
    /** Peak acceleration value (g-force) for fall/crash. */
    val peakAccelerationG: Double? = null,
    /** Speed at time of detection (m/s) for crash. */
    val speedMps: Double? = null,
    /** Heart rate at time of detection (bpm). */
    val heartRateBpm: Double? = null,
    /** Seconds since last detected movement. */
    val secondsSinceLastMovement: Long? = null,
    /** Whether the user dismissed the detection (chose "I'm OK"). */
    var dismissed: Boolean = false,
    /** Whether the detection triggered an SOS. */
    var triggeredSOS: Boolean = false
)

/**
 * Configuration for detection thresholds.
 */
data class DetectionConfig(
    /** Fall detection: minimum peak acceleration (g-force). Default 3.0g. */
    val fallThresholdG: Double = 3.0,
    /** Fall detection: maximum post-impact acceleration. Default 0.5g. */
    val fallPostImpactMaxG: Double = 0.5,
    /** Crash detection: minimum speed before deceleration (m/s). ~25 mph. */
    val crashMinSpeedMps: Double = 11.2,
    /** Crash detection: maximum time for deceleration (seconds). */
    val crashDecelerationWindowSeconds: Int = 3,
    /** Elevated HR: minimum heart rate to consider elevated (bpm). */
    val elevatedHRThresholdBpm: Int = 120,
    /** Elevated HR: minimum seconds of no movement. */
    val noMovementThresholdSeconds: Long = 120,
    /** Inactivity: seconds after movement before alerting. */
    val inactivityTimeoutSeconds: Long = 300,
    /** Auto-trigger countdown duration after detection (seconds). */
    val autoTriggerCountdownSeconds: Int = 30,
    /** Enable/disable individual detection types. */
    val enabledDetections: Set<DetectionType> = DetectionType.entries.toSet()
)

/**
 * Port interface for implicit emergency detection.
 */
interface ImplicitDetectionPort {

    /**
     * Start monitoring sensors for implicit emergency conditions.
     * Should be called when the app is foregrounded or from a foreground service.
     *
     * @param config Detection thresholds and enabled types.
     */
    suspend fun startMonitoring(config: DetectionConfig = DetectionConfig())

    /**
     * Stop all sensor monitoring.
     */
    suspend fun stopMonitoring()

    /**
     * Whether monitoring is currently active.
     */
    suspend fun isMonitoring(): Boolean

    /**
     * Observe detection events as they occur.
     * Each emission represents a potential emergency that should prompt
     * the user with a confirmation dialog.
     */
    fun observeDetections(): Flow<DetectionEvent>

    /**
     * User dismissed a detection event (chose "I'm OK").
     * Stops the auto-trigger countdown and logs the false positive.
     *
     * @param eventId The detection event ID to dismiss.
     */
    suspend fun dismissDetection(eventId: String)

    /**
     * User confirmed a detection event (chose "Send SOS").
     * Triggers the full SOS flow.
     *
     * @param eventId The detection event ID to confirm.
     */
    suspend fun confirmDetection(eventId: String)

    /**
     * Get detection history for the last N days.
     * Useful for tuning thresholds and reviewing false positives.
     *
     * @param days Number of days of history to retrieve.
     */
    suspend fun getDetectionHistory(days: Int = 7): List<DetectionEvent>

    /**
     * Update detection configuration (thresholds, enabled types).
     */
    suspend fun updateConfig(config: DetectionConfig)

    /**
     * Get the current detection configuration.
     */
    suspend fun getConfig(): DetectionConfig
}
