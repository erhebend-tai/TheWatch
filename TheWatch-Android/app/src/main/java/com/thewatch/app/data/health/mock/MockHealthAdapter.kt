/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         MockHealthAdapter.kt                                   │
 * │ Purpose:      Mock (Tier 1) adapter for HealthPort. Returns          │
 * │               simulated health data for development and testing.     │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: HealthPort                                             │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   @Provides fun provideHealthPort(                                   │
 * │       mock: MockHealthAdapter                                        │
 * │   ): HealthPort = mock                                               │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.health.mock

import android.util.Log
import com.thewatch.app.data.health.AlertSeverity
import com.thewatch.app.data.health.HealthAlertThresholds
import com.thewatch.app.data.health.HealthAlertViolation
import com.thewatch.app.data.health.HealthDataType
import com.thewatch.app.data.health.HealthPort
import com.thewatch.app.data.health.HealthReading
import com.thewatch.app.data.health.HealthSummary
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.flow
import javax.inject.Inject
import javax.inject.Singleton
import kotlin.random.Random

@Singleton
class MockHealthAdapter @Inject constructor() : HealthPort {

    companion object {
        private const val TAG = "TheWatch.MockHealth"
    }

    @Volatile
    var simulateUnavailable: Boolean = false

    @Volatile
    var simulateElevatedHR: Boolean = false

    override suspend fun isAvailable(): Boolean = !simulateUnavailable
    override suspend fun isHealthConnectInstalled(): Boolean = true
    override suspend fun requestPermissions(): Boolean = true

    override suspend fun getLatestHeartRate(): HealthReading {
        val bpm = if (simulateElevatedHR) Random.nextDouble(140.0, 180.0) else Random.nextDouble(60.0, 85.0)
        return HealthReading(bpm, "bpm", source = "Mock Wearable", type = HealthDataType.HEART_RATE)
    }

    override suspend fun getLatestBloodOxygen(): HealthReading {
        return HealthReading(Random.nextDouble(95.0, 99.0), "%", source = "Mock Wearable", type = HealthDataType.BLOOD_OXYGEN)
    }

    override suspend fun getStepsToday(): Int = Random.nextInt(2000, 12000)

    override suspend fun getHealthSummary(): HealthSummary {
        return HealthSummary(
            latestHeartRate = getLatestHeartRate(),
            latestBloodOxygen = getLatestBloodOxygen(),
            stepsToday = getStepsToday(),
            distanceTodayMeters = Random.nextDouble(1000.0, 8000.0),
            caloriesToday = Random.nextDouble(800.0, 2500.0),
            sleepLastNightMinutes = Random.nextInt(300, 540),
            restingHeartRate = Random.nextDouble(55.0, 72.0)
        )
    }

    override suspend fun getHistoricalReadings(
        type: HealthDataType,
        fromEpochMillis: Long,
        toEpochMillis: Long
    ): List<HealthReading> {
        val readings = mutableListOf<HealthReading>()
        var t = fromEpochMillis
        val interval = 300_000L // 5 minutes
        while (t < toEpochMillis) {
            readings.add(HealthReading(
                value = when (type) {
                    HealthDataType.HEART_RATE -> Random.nextDouble(55.0, 100.0)
                    HealthDataType.BLOOD_OXYGEN -> Random.nextDouble(94.0, 99.0)
                    HealthDataType.STEPS -> Random.nextDouble(0.0, 200.0)
                    else -> Random.nextDouble(0.0, 100.0)
                },
                unit = when (type) {
                    HealthDataType.HEART_RATE -> "bpm"
                    HealthDataType.BLOOD_OXYGEN -> "%"
                    HealthDataType.STEPS -> "steps"
                    else -> ""
                },
                timestamp = t,
                source = "Mock Wearable",
                type = type
            ))
            t += interval
        }
        return readings.sortedByDescending { it.timestamp }
    }

    override fun observeHeartRate(): Flow<HealthReading> = flow {
        while (true) {
            delay(5000)
            emit(getLatestHeartRate())
        }
    }

    override suspend fun checkAlertThresholds(thresholds: HealthAlertThresholds): List<HealthAlertViolation> {
        val violations = mutableListOf<HealthAlertViolation>()
        val hr = getLatestHeartRate()
        if (hr.value > thresholds.heartRateHighBpm) {
            violations.add(HealthAlertViolation(HealthDataType.HEART_RATE, hr, thresholds.heartRateHighBpm.toDouble(), true, AlertSeverity.CRITICAL))
        }
        if (hr.value < thresholds.heartRateLowBpm) {
            violations.add(HealthAlertViolation(HealthDataType.HEART_RATE, hr, thresholds.heartRateLowBpm.toDouble(), false, AlertSeverity.CRITICAL))
        }
        val spo2 = getLatestBloodOxygen()
        if (spo2.value < thresholds.bloodOxygenLowPercent) {
            violations.add(HealthAlertViolation(HealthDataType.BLOOD_OXYGEN, spo2, thresholds.bloodOxygenLowPercent, false, AlertSeverity.WARNING))
        }
        return violations
    }

    override suspend fun registerBackgroundObserver(): Boolean {
        Log.d(TAG, "Background observer registered (mock)")
        return true
    }

    override suspend fun unregisterBackgroundObserver() {
        Log.d(TAG, "Background observer unregistered (mock)")
    }
}
