/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         WearablePort.kt                                        │
 * │ Purpose:      Hexagonal port interface for wearable device           │
 * │               management. Pair/unpair devices, sync health data,     │
 * │               receive SOS triggers from wearable companions.         │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: WearableDevice model, HealthPort                       │
 * │                                                                      │
 * │ Adapter tiers:                                                       │
 * │   - Mock:   Simulated device list. Dev/test.                         │
 * │   - Native: BLE companion device management + Wear OS Data Layer.   │
 * │   - Live:   Native + cloud device registry (future).                 │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   val port: WearablePort = hiltGet()                                 │
 * │   val devices = port.getPairedDevices()                              │
 * │   port.syncHealthData(devices.first().id)                            │
 * │   port.observeSOSFromWearable().collect { sos -> triggerSOS(sos) }  │
 * │                                                                      │
 * │ NOTE: Wear OS uses the Data Layer API for message passing. Non-Wear │
 * │ OS wearables (Fitbit, Garmin) communicate via their proprietary     │
 * │ SDKs or Health Connect. Samsung Galaxy Watch uses Wear OS 4+.      │
 * │ Apple Watch is NOT supported on Android (no cross-platform API).    │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.wearables

import com.thewatch.app.data.health.HealthSummary
import com.thewatch.app.data.model.WearableDevice
import kotlinx.coroutines.flow.Flow

/**
 * Wearable connection state.
 */
enum class WearableConnectionState {
    DISCONNECTED,
    CONNECTING,
    CONNECTED,
    SYNCING,
    ERROR
}

/**
 * SOS event received from a wearable device.
 */
data class WearableSOSEvent(
    val deviceId: String,
    val userId: String,
    val timestamp: Long = System.currentTimeMillis(),
    val triggerType: WearableSOSTrigger = WearableSOSTrigger.BUTTON_PRESS,
    val latitude: Double? = null,
    val longitude: Double? = null,
    val heartRate: Double? = null
)

/**
 * How the SOS was triggered on the wearable.
 */
enum class WearableSOSTrigger {
    /** User pressed a dedicated SOS button or tile. */
    BUTTON_PRESS,
    /** Fall detected by wearable accelerometer. */
    FALL_DETECTED,
    /** Abnormal heart rate detected by wearable sensor. */
    ABNORMAL_HR,
    /** User performed a specific gesture (e.g., triple-tap). */
    GESTURE,
    /** Inactivity timeout — user didn't respond to check-in prompt. */
    INACTIVITY_TIMEOUT
}

/**
 * Port interface for wearable device management.
 */
interface WearablePort {

    /**
     * Get all paired wearable devices.
     */
    suspend fun getPairedDevices(): List<WearableDevice>

    /**
     * Start scanning for available wearable devices to pair.
     */
    fun scanForDevices(): Flow<WearableDevice>

    /**
     * Pair a new wearable device.
     * @param deviceId The BLE address or Wear OS node ID.
     * @return true if pairing succeeded.
     */
    suspend fun pairDevice(deviceId: String): Boolean

    /**
     * Unpair a wearable device and remove its data.
     * @param deviceId The device to unpair.
     * @return true if unpairing succeeded.
     */
    suspend fun unpairDevice(deviceId: String): Boolean

    /**
     * Get the connection state of a specific device.
     */
    suspend fun getConnectionState(deviceId: String): WearableConnectionState

    /**
     * Observe connection state changes for a specific device.
     */
    fun observeConnectionState(deviceId: String): Flow<WearableConnectionState>

    /**
     * Sync health data from a wearable device.
     * Pulls latest readings and stores in HealthPort / Room.
     *
     * @param deviceId The device to sync from.
     * @return HealthSummary containing synced data, or null on failure.
     */
    suspend fun syncHealthData(deviceId: String): HealthSummary?

    /**
     * Observe SOS events from any paired wearable.
     * The app should trigger its SOS flow when an event is received.
     */
    fun observeSOSFromWearable(): Flow<WearableSOSEvent>

    /**
     * Send a vibration/haptic alert to a paired wearable.
     * Used for check-in prompts and SOS confirmations.
     *
     * @param deviceId The device to alert.
     * @param pattern Vibration pattern (e.g., "short", "long", "sos").
     */
    suspend fun sendHapticAlert(deviceId: String, pattern: String = "short"): Boolean

    /**
     * Send a message/notification to display on the wearable.
     */
    suspend fun sendNotification(
        deviceId: String,
        title: String,
        body: String
    ): Boolean

    /**
     * Get battery level of a paired wearable.
     * @return Battery percentage (0-100), or null if unavailable.
     */
    suspend fun getBatteryLevel(deviceId: String): Int?
}
