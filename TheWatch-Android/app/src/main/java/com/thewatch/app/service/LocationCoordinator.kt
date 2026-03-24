// LocationCoordinator — app-level coordinator for location tracking.
// Manages LocationService lifecycle at application scope, independent of any screen.
// Handles mode transitions (Normal → Emergency → Normal) triggered by SOS pipeline.

package com.thewatch.app.service

import android.content.Context
import android.content.Intent
import android.location.Location
import android.util.Log
import com.thewatch.app.data.repository.LocationRepository
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import javax.inject.Inject
import javax.inject.Singleton

/**
 * Application-scoped coordinator for location tracking.
 *
 * This wraps LocationService control into a clean API that any SOS trigger
 * (phrase detection, tap detection, manual button) can call to escalate
 * tracking mode without knowing about Android service internals.
 */
@Singleton
class LocationCoordinator @Inject constructor(
    @ApplicationContext private val context: Context,
    private val locationRepository: LocationRepository
) {
    companion object {
        private const val TAG = "LocationCoordinator"
    }

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)

    private val _currentMode = MutableStateFlow(LocationService.TrackingMode.PASSIVE)
    val currentMode: StateFlow<LocationService.TrackingMode> = _currentMode.asStateFlow()

    private val _isTracking = MutableStateFlow(false)
    val isTracking: StateFlow<Boolean> = _isTracking.asStateFlow()

    /** Current location from the repository/service. */
    val currentLocation: StateFlow<Location?> = locationRepository.currentLocation

    /**
     * Start location tracking in the specified mode.
     * Call this at app startup (NORMAL) or on SOS (EMERGENCY).
     */
    fun startTracking(mode: LocationService.TrackingMode = LocationService.TrackingMode.NORMAL) {
        _currentMode.value = mode
        _isTracking.value = true

        val intent = Intent(context, LocationService::class.java).apply {
            action = LocationService.ACTION_START_TRACKING
            putExtra(LocationService.EXTRA_MODE, mode)
        }
        context.startForegroundService(intent)
        Log.i(TAG, "Started tracking in ${mode.name} mode")
    }

    /**
     * Switch to emergency mode — called by any SOS trigger.
     */
    fun escalateToEmergency() {
        if (_currentMode.value != LocationService.TrackingMode.EMERGENCY) {
            startTracking(LocationService.TrackingMode.EMERGENCY)
            Log.i(TAG, "Escalated to EMERGENCY mode")
        }
    }

    /**
     * Return to normal tracking — called when SOS is cancelled.
     */
    fun deescalateToNormal() {
        if (_currentMode.value == LocationService.TrackingMode.EMERGENCY) {
            startTracking(LocationService.TrackingMode.NORMAL)
            Log.i(TAG, "De-escalated to NORMAL mode")
        }
    }

    /**
     * Stop all tracking.
     */
    fun stopTracking() {
        _isTracking.value = false
        _currentMode.value = LocationService.TrackingMode.PASSIVE

        val intent = Intent(context, LocationService::class.java).apply {
            action = LocationService.ACTION_STOP_TRACKING
        }
        context.startService(intent)
        Log.i(TAG, "Stopped tracking")
    }
}
