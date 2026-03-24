/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         BLEMeshPort.kt                                         │
 * │ Purpose:      Hexagonal port interface for BLE mesh communication.   │
 * │               Enables device-to-device offline SOS relay using BLE   │
 * │               advertising and scanning. When a user triggers SOS     │
 * │               without internet, nearby TheWatch devices can receive  │
 * │               the beacon and relay it to the server on the           │
 * │               recipient's behalf.                                    │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: Android BLE API (BluetoothLeAdvertiser,                │
 * │               BluetoothLeScanner), BLUETOOTH_ADVERTISE +             │
 * │               BLUETOOTH_SCAN permissions (API 31+)                   │
 * │                                                                      │
 * │ Adapter tiers:                                                       │
 * │   - Mock:   In-memory beacon store. Simulates discovery.             │
 * │   - Native: Real BLE advertising + scanning via Android BLE API.     │
 * │   - Live:   Native + mesh routing protocol (future).                 │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   val ble: BLEMeshPort = hiltGet()                                   │
 * │   // Broadcast SOS                                                   │
 * │   ble.startSOSBroadcast(SOSBeacon(userId, lat, lng, timestamp))     │
 * │   // Scan for nearby SOS beacons                                     │
 * │   ble.scanForBeacons().collect { beacon ->                           │
 * │       handleNearbySOSBeacon(beacon)                                  │
 * │   }                                                                  │
 * │                                                                      │
 * │ NOTE: BLE advertising range is typically 10-30m indoors, up to       │
 * │ 100m outdoors (line of sight). The service UUID                      │
 * │ 0000SAFE-0000-1000-8000-00805F9B34FB is custom for TheWatch.        │
 * │ BLE 5.0 extended advertising (API 26+) allows larger payloads       │
 * │ (up to 254 bytes). Pre-BLE 5.0 devices are limited to 31 bytes     │
 * │ in advertisement data — SOS beacon must fit within this limit.      │
 * │ Android 12+ requires BLUETOOTH_ADVERTISE and BLUETOOTH_SCAN          │
 * │ runtime permissions. Older versions use BLUETOOTH and                │
 * │ ACCESS_FINE_LOCATION.                                                │
 * │ Background BLE scanning is throttled by Android (max 5 scans/30s).  │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.ble

import kotlinx.coroutines.flow.Flow
import java.util.UUID

/**
 * TheWatch custom BLE service UUID for SOS mesh communication.
 */
val THEWATCH_BLE_SERVICE_UUID: UUID = UUID.fromString("0000SAFE-0000-1000-8000-00805F9B34FB")

/**
 * SOS beacon data broadcast over BLE.
 * Must fit within 31 bytes for legacy BLE advertising compatibility.
 */
data class SOSBeacon(
    /** User ID of the person in distress (truncated to 8 chars for BLE payload). */
    val userId: String,
    /** Latitude of the SOS origin. */
    val latitude: Double,
    /** Longitude of the SOS origin. */
    val longitude: Double,
    /** Epoch millis when the SOS was triggered. */
    val timestamp: Long = System.currentTimeMillis(),
    /** Signal strength at discovery (filled by scanner, not broadcaster). */
    val rssi: Int = 0,
    /** Estimated distance in meters (computed from RSSI, approximate). */
    val estimatedDistanceMeters: Double = 0.0,
    /** Whether this beacon has been relayed to the server already. */
    val relayed: Boolean = false
)

/**
 * Discovered TheWatch device in BLE range (not necessarily SOS).
 */
data class NearbyDevice(
    /** BLE MAC address or device identifier. */
    val deviceId: String,
    /** RSSI signal strength. */
    val rssi: Int,
    /** Last seen epoch millis. */
    val lastSeen: Long = System.currentTimeMillis(),
    /** Whether this device is currently broadcasting an SOS beacon. */
    val isBroadcastingSOS: Boolean = false,
    /** Parsed SOS beacon data, if broadcasting SOS. */
    val sosBeacon: SOSBeacon? = null
)

/**
 * Port interface for BLE mesh SOS communication.
 */
interface BLEMeshPort {

    /**
     * Check whether BLE advertising and scanning are available on this device.
     * @return true if BLE is supported, enabled, and permissions are granted.
     */
    suspend fun isBLEAvailable(): Boolean

    /**
     * Start broadcasting an SOS beacon over BLE.
     * The beacon will be advertised continuously until [stopSOSBroadcast] is called.
     *
     * @param beacon The SOS beacon data to broadcast.
     * @return true if advertising started successfully.
     */
    suspend fun startSOSBroadcast(beacon: SOSBeacon): Boolean

    /**
     * Stop broadcasting the SOS beacon.
     */
    suspend fun stopSOSBroadcast()

    /**
     * Whether this device is currently broadcasting an SOS beacon.
     */
    suspend fun isBroadcasting(): Boolean

    /**
     * Start scanning for nearby TheWatch devices and SOS beacons.
     * Returns a Flow that emits discovered beacons.
     *
     * Scanning is throttled to respect Android's 5-scans-per-30s limit.
     * Results are deduplicated by userId within a 30-second window.
     */
    fun scanForBeacons(): Flow<SOSBeacon>

    /**
     * Start scanning for all nearby TheWatch devices (SOS and non-SOS).
     * Used for mesh topology awareness.
     */
    fun scanForDevices(): Flow<NearbyDevice>

    /**
     * Stop all BLE scanning.
     */
    suspend fun stopScanning()

    /**
     * Get the count of TheWatch devices currently visible in BLE range.
     * Useful for UI display showing nearby network size.
     */
    suspend fun getNearbyDeviceCount(): Int

    /**
     * Relay a received SOS beacon to the server on behalf of the originator.
     * Called when this device has internet but received an SOS from a
     * device that does not.
     *
     * @param beacon The SOS beacon received from another device.
     * @return true if relay was successful.
     */
    suspend fun relaySOSBeacon(beacon: SOSBeacon): Boolean

    /**
     * Start passive listening mode. Low-power background scan that only
     * wakes the app when an SOS beacon is detected.
     */
    suspend fun startPassiveListening(): Boolean

    /**
     * Stop passive listening.
     */
    suspend fun stopPassiveListening()
}
