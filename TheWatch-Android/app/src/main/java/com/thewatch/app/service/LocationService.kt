package com.thewatch.app.service

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Intent
import android.location.Location
import android.os.Build
import android.os.IBinder
import androidx.core.app.NotificationCompat
import androidx.core.app.ServiceCompat
import com.google.android.gms.location.FusedLocationProviderClient
import com.google.android.gms.location.LocationRequest
import com.google.android.gms.location.LocationServices
import com.google.android.gms.location.Priority
import com.google.android.gms.tasks.CancellationToken
import com.google.android.gms.tasks.OnTokenCanceledListener
import dagger.hilt.android.AndroidEntryPoint
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import javax.inject.Inject

/**
 * Foreground service for continuous location tracking.
 * Supports two modes: NORMAL (30-sec interval) and EMERGENCY (1-sec, best accuracy).
 * Location updates are broadcast via [currentLocation] StateFlow.
 */
@AndroidEntryPoint
class LocationService : Service() {

    @Inject
    lateinit var fusedLocationClient: FusedLocationProviderClient

    private val serviceScope = CoroutineScope(Dispatchers.Default + SupervisorJob())

    private val _currentLocation = MutableStateFlow<Location?>(null)
    val currentLocation: StateFlow<Location?> = _currentLocation.asStateFlow()

    private var currentMode: TrackingMode = TrackingMode.PASSIVE

    override fun onCreate() {
        super.onCreate()
        createNotificationChannel()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        val action = intent?.action
        val mode = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            intent?.getSerializableExtra(EXTRA_MODE, TrackingMode::class.java) ?: TrackingMode.NORMAL
        } else {
            @Suppress("DEPRECATION")
            intent?.getSerializableExtra(EXTRA_MODE) as? TrackingMode ?: TrackingMode.NORMAL
        }

        when (action) {
            ACTION_START_TRACKING -> {
                startTracking(mode)
            }
            ACTION_STOP_TRACKING -> {
                stopTracking()
            }
        }

        // Start foreground service with persistent notification
        val notification = createNotification()
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) {
            ServiceCompat.startForeground(
                this,
                NOTIFICATION_ID,
                notification,
                android.content.pm.ServiceInfo.FOREGROUND_SERVICE_TYPE_LOCATION
            )
        } else {
            startForeground(NOTIFICATION_ID, notification)
        }

        return START_STICKY
    }

    override fun onBind(intent: Intent?): IBinder? = null

    private fun startTracking(mode: TrackingMode) {
        currentMode = mode
        serviceScope.launch {
            val locationRequest = when (mode) {
                TrackingMode.NORMAL -> LocationRequest.Builder(Priority.PRIORITY_BALANCED_POWER_ACCURACY, 30_000L)
                    .setMinUpdateDistanceMeters(100f)
                    .build()

                TrackingMode.EMERGENCY -> LocationRequest.Builder(Priority.PRIORITY_HIGH_ACCURACY, 1_000L)
                    .setMinUpdateDistanceMeters(0f)
                    .build()

                TrackingMode.PASSIVE -> LocationRequest.Builder(Priority.PRIORITY_LOW_POWER, 60_000L)
                    .setMinUpdateDistanceMeters(500f)
                    .build()
            }

            try {
                @Suppress("MissingPermission")
                fusedLocationClient.requestLocationUpdates(
                    locationRequest,
                    { location -> _currentLocation.value = location },
                    null
                )
            } catch (e: SecurityException) {
                // Handle missing permissions gracefully
            }
        }
    }

    private fun stopTracking() {
        serviceScope.launch {
            try {
                fusedLocationClient.removeLocationUpdates { }
            } catch (e: Exception) {
                // Location updates already removed or cancelled
            }
        }
    }

    private fun createNotification() = NotificationCompat.Builder(this, CHANNEL_ID)
        .setContentTitle("TheWatch - Location Tracking Active")
        .setContentText("Tracking your location for emergency response")
        .setSmallIcon(android.R.drawable.ic_dialog_map)
        .setOngoing(true)
        .setPriority(NotificationCompat.PRIORITY_LOW)
        .build()

    private fun createNotificationChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val channel = NotificationChannel(
                CHANNEL_ID,
                "Location Tracking",
                NotificationManager.IMPORTANCE_LOW
            ).apply {
                description = "Notifications for TheWatch location tracking service"
                enableVibration(false)
            }
            val manager = getSystemService(NotificationManager::class.java)
            manager?.createNotificationChannel(channel)
        }
    }

    override fun onDestroy() {
        serviceScope.cancel()
        stopTracking()
        super.onDestroy()
    }

    enum class TrackingMode {
        NORMAL,      // 30-sec interval, balanced power/accuracy
        EMERGENCY,   // 1-sec interval, best accuracy for SOS
        PASSIVE      // 60-sec interval, low power
    }

    companion object {
        const val NOTIFICATION_ID = 2001
        const val CHANNEL_ID = "location_service_channel"

        const val ACTION_START_TRACKING = "com.thewatch.app.action.START_TRACKING"
        const val ACTION_STOP_TRACKING = "com.thewatch.app.action.STOP_TRACKING"
        const val EXTRA_MODE = "mode"
    }
}
