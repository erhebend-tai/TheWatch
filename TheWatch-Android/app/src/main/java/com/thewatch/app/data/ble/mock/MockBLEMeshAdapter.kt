/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         MockBLEMeshAdapter.kt                                  │
 * │ Purpose:      Mock (Tier 1) adapter for BLEMeshPort. Simulates      │
 * │               BLE device discovery and SOS broadcasting with         │
 * │               configurable fake devices. For dev/emulator/testing.   │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: BLEMeshPort                                            │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   @Provides fun provideBLEPort(                                      │
 * │       mock: MockBLEMeshAdapter                                       │
 * │   ): BLEMeshPort = mock                                              │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.ble.mock

import android.util.Log
import com.thewatch.app.data.ble.BLEMeshPort
import com.thewatch.app.data.ble.NearbyDevice
import com.thewatch.app.data.ble.SOSBeacon
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.flow
import javax.inject.Inject
import javax.inject.Singleton

@Singleton
class MockBLEMeshAdapter @Inject constructor() : BLEMeshPort {

    companion object {
        private const val TAG = "TheWatch.MockBLE"
    }

    @Volatile
    private var broadcasting = false

    @Volatile
    private var passiveListening = false

    private val fakeNearbyDevices = mutableListOf(
        NearbyDevice("AA:BB:CC:DD:EE:01", rssi = -45, isBroadcastingSOS = false),
        NearbyDevice("AA:BB:CC:DD:EE:02", rssi = -62, isBroadcastingSOS = false),
        NearbyDevice("AA:BB:CC:DD:EE:03", rssi = -78, isBroadcastingSOS = true,
            sosBeacon = SOSBeacon("mock-user", 32.7767, -96.7970, rssi = -78, estimatedDistanceMeters = 15.0)
        )
    )

    val relayedBeacons: MutableList<SOSBeacon> = mutableListOf()

    override suspend fun isBLEAvailable(): Boolean {
        Log.d(TAG, "isBLEAvailable() -> true (mock)")
        return true
    }

    override suspend fun startSOSBroadcast(beacon: SOSBeacon): Boolean {
        broadcasting = true
        Log.i(TAG, "Started SOS broadcast: userId=${beacon.userId}, lat=${beacon.latitude}, lng=${beacon.longitude}")
        return true
    }

    override suspend fun stopSOSBroadcast() {
        broadcasting = false
        Log.i(TAG, "Stopped SOS broadcast")
    }

    override suspend fun isBroadcasting(): Boolean = broadcasting

    override fun scanForBeacons(): Flow<SOSBeacon> = flow {
        Log.d(TAG, "Scanning for SOS beacons (mock)")
        delay(1000)
        fakeNearbyDevices.filter { it.isBroadcastingSOS }.forEach { device ->
            device.sosBeacon?.let { emit(it) }
        }
    }

    override fun scanForDevices(): Flow<NearbyDevice> = flow {
        Log.d(TAG, "Scanning for devices (mock)")
        delay(500)
        fakeNearbyDevices.forEach { emit(it) }
    }

    override suspend fun stopScanning() {
        Log.d(TAG, "Stopped scanning (mock)")
    }

    override suspend fun getNearbyDeviceCount(): Int = fakeNearbyDevices.size

    override suspend fun relaySOSBeacon(beacon: SOSBeacon): Boolean {
        relayedBeacons.add(beacon)
        Log.i(TAG, "Relayed SOS beacon for userId=${beacon.userId}")
        return true
    }

    override suspend fun startPassiveListening(): Boolean {
        passiveListening = true
        Log.d(TAG, "Started passive listening (mock)")
        return true
    }

    override suspend fun stopPassiveListening() {
        passiveListening = false
        Log.d(TAG, "Stopped passive listening (mock)")
    }
}
