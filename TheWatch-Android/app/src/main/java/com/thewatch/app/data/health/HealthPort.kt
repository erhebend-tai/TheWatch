/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         HealthPort.kt                                          │
 * │ Purpose:      Hexagonal port interface for health data integration.  │
 * │               Reads heart rate, steps, blood oxygen, and other       │
 * │               vitals from Health Connect API (Android 14+) or        │
 * │               Google Fit (legacy). Enables implicit emergency        │
 * │               detection via abnormal vital signs.                    │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: Health Connect SDK (androidx.health.connect)           │
 * │                                                                      │
 * │ Adapter tiers:                                                       │
 * │   - Mock:   Returns simulated health data. Dev/test.                 │
 * │   - Native: Reads from Health Connect API on device.                 │
 * │   - Live:   Native + cloud sync to TheWatch backend (future).        │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   val health: HealthPort = hiltGet()                                 │
 * │   if (health.isAvailable()) {                                        │
 * │       val hr = health.getLatestHeartRate()                           │
 * │       val spo2 = health.getLatestBloodOxygen()                       │
 * │       val steps = health.getStepsToday()                             │
 * │   }                                                                  │
 * │                                                                      │
 * │ NOTE: Health Connect requires the app to declare                     │
 * │ android.permission.health.READ_HEART_RATE etc in manifest.          │
 * │ Samsung Health, Fitbit, Garmin Connect, and Oura all write to       │
 * │ Health Connect on supported devices. Blood oxygen (SpO2) may not    │
 * │ be available on all wearables. Heart rate during sleep may differ   │
 * │ significantly from resting HR — use appropriate thresholds.         │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.health

import kotlinx.coroutines.flow.Flow

/**
 * A single health data reading with timestamp and source.
 */
data class HealthReading(
    /** The numeric value of the reading. */
    val value: Double,
    /** Unit of measurement (bpm, steps, %, etc.). */
    val unit: String,
    /** Epoch millis when the reading was taken. */
    val timestamp: Long = System.currentTimeMillis(),
    /** Source app/device that produced the reading (e.g., "Samsung Health", "Fitbit"). */
    val source: String = "Unknown",
    /** Data type identifier. */
    val type: HealthDataType = HealthDataType.HEART_RATE
)

/**
 * Supported health data types.
 */
enum class HealthDataType {
    HEART_RATE,
    BLOOD_OXYGEN,
    STEPS,
    DISTANCE,
    CALORIES,
    SLEEP_DURATION,
    RESPIRATORY_RATE,
    BODY_TEMPERATURE,
    BLOOD_PRESSURE_SYSTOLIC,
    BLOOD_PRESSURE_DIASTOLIC,
    BLOOD_GLUCOSE,
    STRESS_LEVEL
}

/**
 * Summary of health metrics for dashboard display.
 */
data class HealthSummary(
    val latestHeartRate: HealthReading? = null,
    val latestBloodOxygen: HealthReading? = null,
    val stepsToday: Int = 0,
    val distanceTodayMeters: Double = 0.0,
    val caloriesToday: Double = 0.0,
    val sleepLastNightMinutes: Int = 0,
    val restingHeartRate: Double? = null,
    val lastUpdated: Long = System.currentTimeMillis()
)

/**
 * Alert thresholds for health-based emergency detection.
 */
data class HealthAlertThresholds(
    val heartRateHighBpm: Int = 150,
    val heartRateLowBpm: Int = 40,
    val bloodOxygenLowPercent: Double = 90.0,
    val respiratoryRateHighBreathsPerMin: Int = 30,
    val bodyTemperatureHighCelsius: Double = 39.5
)

/**
 * Port interface for health data access.
 */
interface HealthPort {

    /**
     * Check whether Health Connect (or fallback) is available and permissions granted.
     */
    suspend fun isAvailable(): Boolean

    /**
     * Check whether the Health Connect app is installed on the device.
     */
    suspend fun isHealthConnectInstalled(): Boolean

    /**
     * Request necessary Health Connect permissions.
     * @return true if all required permissions were granted.
     */
    suspend fun requestPermissions(): Boolean

    /**
     * Get the latest heart rate reading.
     */
    suspend fun getLatestHeartRate(): HealthReading?

    /**
     * Get the latest blood oxygen (SpO2) reading.
     */
    suspend fun getLatestBloodOxygen(): HealthReading?

    /**
     * Get total steps for today.
     */
    suspend fun getStepsToday(): Int

    /**
     * Get a full health summary for dashboard display.
     */
    suspend fun getHealthSummary(): HealthSummary

    /**
     * Get historical readings for a specific data type.
     *
     * @param type The health data type to query.
     * @param fromEpochMillis Start of the time range.
     * @param toEpochMillis End of the time range.
     * @return List of readings ordered by timestamp descending.
     */
    suspend fun getHistoricalReadings(
        type: HealthDataType,
        fromEpochMillis: Long,
        toEpochMillis: Long = System.currentTimeMillis()
    ): List<HealthReading>

    /**
     * Observe heart rate changes in real-time (if wearable is streaming).
     */
    fun observeHeartRate(): Flow<HealthReading>

    /**
     * Check current vitals against alert thresholds.
     * Returns a list of threshold violations (empty if all normal).
     */
    suspend fun checkAlertThresholds(
        thresholds: HealthAlertThresholds = HealthAlertThresholds()
    ): List<HealthAlertViolation>

    /**
     * Register a background observer for health data changes.
     * Used for implicit emergency detection even when app is backgrounded.
     */
    suspend fun registerBackgroundObserver(): Boolean

    /**
     * Unregister the background observer.
     */
    suspend fun unregisterBackgroundObserver()
}

/**
 * A threshold violation detected in health data.
 */
data class HealthAlertViolation(
    val dataType: HealthDataType,
    val reading: HealthReading,
    val thresholdValue: Double,
    val isAbove: Boolean,
    val severity: AlertSeverity = AlertSeverity.WARNING
)

enum class AlertSeverity {
    INFO,
    WARNING,
    CRITICAL
}
