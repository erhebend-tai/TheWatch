package com.thewatch.app.data.repository

import android.content.Context
import android.content.Intent
import android.location.Location
import com.google.android.gms.location.FusedLocationProviderClient
import com.google.android.gms.location.Priority
import com.google.android.gms.tasks.CancellationToken
import com.google.android.gms.tasks.OnTokenCanceledListener
import com.thewatch.app.service.LocationService
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.channels.awaitClose
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.callbackFlow
import javax.inject.Inject
import javax.inject.Singleton

/**
 * Repository for managing device location.
 * Wraps FusedLocationProviderClient and provides reactive location updates via StateFlow.
 */
interface LocationRepository {
    val currentLocation: StateFlow<Location?>

    suspend fun startTracking(mode: LocationService.TrackingMode)
    suspend fun stopTracking()
    suspend fun getLastKnownLocation(): Location?
}

@Singleton
class LocationRepositoryImpl @Inject constructor(
    @ApplicationContext private val context: Context,
    private val fusedLocationClient: FusedLocationProviderClient
) : LocationRepository {

    private val _currentLocation = MutableStateFlow<Location?>(null)
    override val currentLocation: StateFlow<Location?> = _currentLocation.asStateFlow()

    override suspend fun startTracking(mode: LocationService.TrackingMode) {
        val intent = Intent(context, LocationService::class.java).apply {
            action = LocationService.ACTION_START_TRACKING
            putExtra(LocationService.EXTRA_MODE, mode)
        }
        context.startService(intent)
    }

    override suspend fun stopTracking() {
        val intent = Intent(context, LocationService::class.java).apply {
            action = LocationService.ACTION_STOP_TRACKING
        }
        context.startService(intent)
    }

    override suspend fun getLastKnownLocation(): Location? {
        return try {
            @Suppress("MissingPermission")
            val location = fusedLocationClient.getCurrentLocation(
                Priority.PRIORITY_HIGH_ACCURACY,
                object : CancellationToken() {
                    override fun onCanceledRequested(callback: OnTokenCanceledListener) {}
                    override fun isCancellationRequested() = false
                }
            ).result
            _currentLocation.value = location
            location
        } catch (e: SecurityException) {
            null
        }
    }
}
