/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         MockImplicitDetectionAdapter.kt                        │
 * │ Purpose:      Mock (Tier 1) adapter for ImplicitDetectionPort.       │
 * │               Simulates emergency detections on demand for testing.  │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: ImplicitDetectionPort                                  │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   @Provides fun provideDetectionPort(                                │
 * │       mock: MockImplicitDetectionAdapter                             │
 * │   ): ImplicitDetectionPort = mock                                    │
 * │                                                                      │
 * │   // Trigger a simulated fall for testing:                           │
 * │   mock.simulateDetection(DetectionType.FALL)                         │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.emergency.mock

import android.util.Log
import com.thewatch.app.data.emergency.DetectionConfig
import com.thewatch.app.data.emergency.DetectionEvent
import com.thewatch.app.data.emergency.DetectionSeverity
import com.thewatch.app.data.emergency.DetectionType
import com.thewatch.app.data.emergency.ImplicitDetectionPort
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableSharedFlow
import java.util.UUID
import javax.inject.Inject
import javax.inject.Singleton

@Singleton
class MockImplicitDetectionAdapter @Inject constructor() : ImplicitDetectionPort {

    companion object {
        private const val TAG = "TheWatch.MockDetection"
    }

    @Volatile
    private var monitoring = false
    private var config = DetectionConfig()
    private val detectionFlow = MutableSharedFlow<DetectionEvent>(extraBufferCapacity = 10)
    private val history = mutableListOf<DetectionEvent>()

    /**
     * Simulate a detection event for testing purposes.
     */
    suspend fun simulateDetection(
        type: DetectionType,
        severity: DetectionSeverity = DetectionSeverity.HIGH,
        confidence: Double = 0.85
    ) {
        val event = DetectionEvent(
            id = UUID.randomUUID().toString(),
            type = type,
            confidence = confidence,
            severity = severity,
            latitude = 32.7767,
            longitude = -96.7970,
            peakAccelerationG = if (type == DetectionType.FALL) 4.2 else null,
            speedMps = if (type == DetectionType.CRASH) 15.6 else null,
            heartRateBpm = if (type == DetectionType.ELEVATED_HR_NO_MOVEMENT) 155.0 else 72.0,
            secondsSinceLastMovement = if (type == DetectionType.ELEVATED_HR_NO_MOVEMENT) 180 else null
        )
        history.add(event)
        detectionFlow.emit(event)
        Log.i(TAG, "Simulated detection: $type (severity=$severity, confidence=$confidence)")
    }

    override suspend fun startMonitoring(config: DetectionConfig) {
        this.config = config
        monitoring = true
        Log.i(TAG, "Monitoring started with ${config.enabledDetections.size} detection types")
    }

    override suspend fun stopMonitoring() {
        monitoring = false
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
        Log.i(TAG, "Detection confirmed (SOS triggered): $eventId")
    }

    override suspend fun getDetectionHistory(days: Int): List<DetectionEvent> {
        val cutoff = System.currentTimeMillis() - (days * 86_400_000L)
        return history.filter { it.timestamp >= cutoff }.sortedByDescending { it.timestamp }
    }

    override suspend fun updateConfig(config: DetectionConfig) {
        this.config = config
        Log.d(TAG, "Config updated")
    }

    override suspend fun getConfig(): DetectionConfig = config
}
