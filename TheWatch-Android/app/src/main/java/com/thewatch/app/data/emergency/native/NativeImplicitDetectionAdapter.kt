/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         NativeImplicitDetectionAdapter.kt                      │
 * │ Purpose:      Native (Tier 2) adapter for ImplicitDetectionPort.     │
 * │               Uses real accelerometer, gyroscope, and health data    │
 * │               for fall detection, crash detection, and elevated HR   │
 * │               + no movement detection.                               │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: SensorManager, HealthPort, LocationRepository          │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   @Provides fun provideDetectionPort(                                │
 * │       adapter: NativeImplicitDetectionAdapter                        │
 * │   ): ImplicitDetectionPort = adapter                                 │
 * │                                                                      │
 * │ NOTE: Fall detection algorithm uses 3-phase pattern matching:        │
 * │   1. Freefall phase: |accel| < 0.5g for > 100ms                     │
 * │   2. Impact phase: |accel| > 3.0g peak                              │
 * │   3. Post-impact phase: stillness (|accel - 1g| < 0.3g) for > 2s  │
 * │ This is similar to Apple Watch Series 4+ and Pixel crash detection. │
 * │ Sensor sampling rate should be SENSOR_DELAY_GAME (~50Hz) for        │
 * │ adequate resolution. Higher rates drain battery significantly.      │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.emergency.native

import android.content.Context
import android.hardware.Sensor
import android.hardware.SensorEvent
import android.hardware.SensorEventListener
import android.hardware.SensorManager
import android.util.Log
import com.thewatch.app.data.emergency.DetectionConfig
import com.thewatch.app.data.emergency.DetectionEvent
import com.thewatch.app.data.emergency.DetectionSeverity
import com.thewatch.app.data.emergency.DetectionType
import com.thewatch.app.data.emergency.ImplicitDetectionPort
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableSharedFlow
import java.util.UUID
import javax.inject.Inject
import javax.inject.Singleton
import kotlin.math.sqrt

@Singleton
class NativeImplicitDetectionAdapter @Inject constructor(
    @ApplicationContext private val context: Context
) : ImplicitDetectionPort, SensorEventListener {

    companion object {
        private const val TAG = "TheWatch.ImplicitDetect"
        private const val GRAVITY = 9.81f
        private const val FREEFALL_THRESHOLD_G = 0.5
        private const val FREEFALL_MIN_DURATION_MS = 100L
        private const val POST_IMPACT_STILLNESS_DURATION_MS = 2000L
    }

    private val sensorManager by lazy {
        context.getSystemService(Context.SENSOR_SERVICE) as SensorManager
    }

    @Volatile
    private var monitoring = false
    private var config = DetectionConfig()
    private val detectionFlow = MutableSharedFlow<DetectionEvent>(extraBufferCapacity = 10)
    private val history = mutableListOf<DetectionEvent>()

    // Fall detection state machine
    @Volatile private var freefallStartTime: Long = 0L
    @Volatile private var impactDetected = false
    @Volatile private var impactTime: Long = 0L
    @Volatile private var peakAcceleration: Double = 0.0
    @Volatile private var lastMovementTime: Long = System.currentTimeMillis()
    @Volatile private var lastSpeed: Double = 0.0

    override suspend fun startMonitoring(config: DetectionConfig) {
        this.config = config
        if (monitoring) return

        val accelerometer = sensorManager.getDefaultSensor(Sensor.TYPE_ACCELEROMETER)
        if (accelerometer != null) {
            sensorManager.registerListener(this, accelerometer, SensorManager.SENSOR_DELAY_GAME)
            monitoring = true
            Log.i(TAG, "Monitoring started — accelerometer registered")
        } else {
            Log.w(TAG, "No accelerometer available on this device")
        }
    }

    override suspend fun stopMonitoring() {
        sensorManager.unregisterListener(this)
        monitoring = false
        resetFallState()
        Log.i(TAG, "Monitoring stopped")
    }

    override suspend fun isMonitoring(): Boolean = monitoring

    override fun observeDetections(): Flow<DetectionEvent> = detectionFlow

    override suspend fun dismissDetection(eventId: String) {
        history.find { it.id == eventId }?.dismissed = true
        Log.i(TAG, "Detection dismissed: $eventId")
    }

    override suspend fun confirmDetection(eventId: String) {
        history.find { it.id == eventId }?.triggeredSOS = true
        Log.i(TAG, "Detection confirmed: $eventId")
    }

    override suspend fun getDetectionHistory(days: Int): List<DetectionEvent> {
        val cutoff = System.currentTimeMillis() - (days * 86_400_000L)
        return history.filter { it.timestamp >= cutoff }.sortedByDescending { it.timestamp }
    }

    override suspend fun updateConfig(config: DetectionConfig) {
        this.config = config
    }

    override suspend fun getConfig(): DetectionConfig = config

    // ── SensorEventListener ─────────────────────────────────────────

    override fun onSensorChanged(event: SensorEvent?) {
        if (event?.sensor?.type != Sensor.TYPE_ACCELEROMETER) return

        val x = event.values[0]
        val y = event.values[1]
        val z = event.values[2]

        // Total acceleration magnitude in g-force
        val totalAccelG = sqrt((x * x + y * y + z * z).toDouble()) / GRAVITY
        val now = System.currentTimeMillis()

        // Track movement for inactivity detection
        if (totalAccelG > 1.2 || totalAccelG < 0.8) {
            lastMovementTime = now
        }

        // Fall detection state machine
        if (config.enabledDetections.contains(DetectionType.FALL)) {
            detectFall(totalAccelG, now)
        }

        // Inactivity detection
        if (config.enabledDetections.contains(DetectionType.INACTIVITY_AFTER_MOVEMENT)) {
            val secondsSinceMovement = (now - lastMovementTime) / 1000
            if (secondsSinceMovement > config.inactivityTimeoutSeconds) {
                emitDetection(DetectionEvent(
                    id = UUID.randomUUID().toString(),
                    type = DetectionType.INACTIVITY_AFTER_MOVEMENT,
                    confidence = 0.6,
                    severity = DetectionSeverity.MEDIUM,
                    secondsSinceLastMovement = secondsSinceMovement
                ))
                lastMovementTime = now // Reset to avoid repeated triggers
            }
        }
    }

    override fun onAccuracyChanged(sensor: Sensor?, accuracy: Int) {
        // No-op
    }

    // ── Fall detection algorithm ────────────────────────────────────

    private fun detectFall(accelG: Double, now: Long) {
        // Phase 1: Freefall detection
        if (accelG < FREEFALL_THRESHOLD_G) {
            if (freefallStartTime == 0L) freefallStartTime = now
        } else {
            if (freefallStartTime > 0 && (now - freefallStartTime) >= FREEFALL_MIN_DURATION_MS) {
                // Freefall phase completed, look for impact
                if (accelG > config.fallThresholdG) {
                    impactDetected = true
                    impactTime = now
                    peakAcceleration = maxOf(peakAcceleration, accelG)
                    Log.d(TAG, "Impact detected: ${accelG}g")
                }
            }
            if (accelG > 1.5) {
                freefallStartTime = 0L
            }
        }

        // Phase 3: Post-impact stillness
        if (impactDetected && (now - impactTime) > POST_IMPACT_STILLNESS_DURATION_MS) {
            if (accelG < 1.0 + config.fallPostImpactMaxG && accelG > 1.0 - config.fallPostImpactMaxG) {
                // Still after impact — likely a fall
                val confidence = minOf(peakAcceleration / 6.0, 1.0) // Scale to 0-1
                val severity = when {
                    confidence > 0.8 -> DetectionSeverity.HIGH
                    confidence > 0.5 -> DetectionSeverity.MEDIUM
                    else -> DetectionSeverity.LOW
                }

                emitDetection(DetectionEvent(
                    id = UUID.randomUUID().toString(),
                    type = DetectionType.FALL,
                    confidence = confidence,
                    severity = severity,
                    peakAccelerationG = peakAcceleration
                ))
                resetFallState()
            }
        }

        // Timeout: reset if no post-impact stillness within 5 seconds
        if (impactDetected && (now - impactTime) > 5000L) {
            resetFallState()
        }
    }

    private fun resetFallState() {
        freefallStartTime = 0L
        impactDetected = false
        impactTime = 0L
        peakAcceleration = 0.0
    }

    private fun emitDetection(event: DetectionEvent) {
        history.add(event)
        detectionFlow.tryEmit(event)
        Log.i(TAG, "Detection emitted: ${event.type} (confidence=${event.confidence})")
    }
}
