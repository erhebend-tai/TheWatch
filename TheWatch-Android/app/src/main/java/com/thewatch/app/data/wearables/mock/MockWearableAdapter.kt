/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         MockWearableAdapter.kt                                 │
 * │ Purpose:      Mock (Tier 1) adapter for WearablePort. Simulates     │
 * │               paired wearable devices and health data sync.          │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: WearablePort, WearableDevice model                     │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   @Provides fun provideWearablePort(                                 │
 * │       mock: MockWearableAdapter                                      │
 * │   ): WearablePort = mock                                             │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.wearables.mock

import android.util.Log
import com.thewatch.app.data.health.HealthDataType
import com.thewatch.app.data.health.HealthReading
import com.thewatch.app.data.health.HealthSummary
import com.thewatch.app.data.model.WearableDevice
import com.thewatch.app.data.wearables.WearableConnectionState
import com.thewatch.app.data.wearables.WearablePort
import com.thewatch.app.data.wearables.WearableSOSEvent
import com.thewatch.app.data.wearables.WearableSOSTrigger
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.flow
import javax.inject.Inject
import javax.inject.Singleton
import kotlin.random.Random

@Singleton
class MockWearableAdapter @Inject constructor() : WearablePort {

    companion object {
        private const val TAG = "TheWatch.MockWearable"
    }

    private val pairedDevices = mutableListOf(
        WearableDevice("wear-001", "user-1", "Pixel Watch 3", "Google", true),
        WearableDevice("wear-002", "user-1", "Galaxy Watch 6", "Samsung", false)
    )

    private val sosFlow = MutableSharedFlow<WearableSOSEvent>(extraBufferCapacity = 10)

    /** Simulate an SOS from a wearable for testing. */
    suspend fun simulateSOS(trigger: WearableSOSTrigger = WearableSOSTrigger.BUTTON_PRESS) {
        val event = WearableSOSEvent(
            deviceId = "wear-001",
            userId = "user-1",
            triggerType = trigger,
            latitude = 32.7767,
            longitude = -96.7970,
            heartRate = 145.0
        )
        sosFlow.emit(event)
        Log.i(TAG, "Simulated wearable SOS: $trigger")
    }

    override suspend fun getPairedDevices(): List<WearableDevice> = pairedDevices.toList()

    override fun scanForDevices(): Flow<WearableDevice> = flow {
        delay(1500)
        emit(WearableDevice("wear-new-001", "", "Fitbit Sense 3", "Fitbit", false))
        delay(800)
        emit(WearableDevice("wear-new-002", "", "Garmin Venu 4", "Garmin", false))
    }

    override suspend fun pairDevice(deviceId: String): Boolean {
        delay(2000)
        pairedDevices.add(WearableDevice(deviceId, "user-1", "New Device", "Unknown", true))
        Log.i(TAG, "Paired device: $deviceId")
        return true
    }

    override suspend fun unpairDevice(deviceId: String): Boolean {
        pairedDevices.removeAll { it.id == deviceId }
        Log.i(TAG, "Unpaired device: $deviceId")
        return true
    }

    override suspend fun getConnectionState(deviceId: String): WearableConnectionState {
        val device = pairedDevices.find { it.id == deviceId }
        return if (device?.isActive == true) WearableConnectionState.CONNECTED
        else WearableConnectionState.DISCONNECTED
    }

    override fun observeConnectionState(deviceId: String): Flow<WearableConnectionState> = flow {
        emit(getConnectionState(deviceId))
    }

    override suspend fun syncHealthData(deviceId: String): HealthSummary {
        delay(1500)
        return HealthSummary(
            latestHeartRate = HealthReading(Random.nextDouble(60.0, 90.0), "bpm", source = "Mock Wearable", type = HealthDataType.HEART_RATE),
            latestBloodOxygen = HealthReading(Random.nextDouble(95.0, 99.0), "%", source = "Mock Wearable", type = HealthDataType.BLOOD_OXYGEN),
            stepsToday = Random.nextInt(3000, 10000),
            distanceTodayMeters = Random.nextDouble(2000.0, 7000.0),
            caloriesToday = Random.nextDouble(1000.0, 2200.0),
            sleepLastNightMinutes = Random.nextInt(300, 520),
            restingHeartRate = Random.nextDouble(55.0, 70.0)
        )
    }

    override fun observeSOSFromWearable(): Flow<WearableSOSEvent> = sosFlow

    override suspend fun sendHapticAlert(deviceId: String, pattern: String): Boolean {
        Log.i(TAG, "Sent haptic alert to $deviceId: pattern=$pattern")
        return true
    }

    override suspend fun sendNotification(deviceId: String, title: String, body: String): Boolean {
        Log.i(TAG, "Sent notification to $deviceId: $title - $body")
        return true
    }

    override suspend fun getBatteryLevel(deviceId: String): Int {
        return Random.nextInt(20, 95)
    }
}
