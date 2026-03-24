package com.thewatch.app.service

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Service
import android.content.Intent
import android.os.Build
import android.os.IBinder
import androidx.core.app.NotificationCompat
import androidx.core.app.ServiceCompat
import dagger.hilt.android.AndroidEntryPoint
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch
import javax.inject.Inject

@AndroidEntryPoint
class SOSService : Service() {

    @Inject
    lateinit var locationService: LocationService

    private val serviceScope = CoroutineScope(Dispatchers.Default + SupervisorJob())

    companion object {
        const val NOTIFICATION_ID = 1
        const val CHANNEL_ID = "sos_service_channel"
    }

    override fun onCreate() {
        super.onCreate()
        createNotificationChannel()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
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

        when (intent?.action) {
            ACTION_START_SOS -> {
                handleStartSOS()
            }
            ACTION_STOP_SOS -> {
                handleStopSOS()
            }
            ACTION_CANCEL_SOS -> {
                handleCancelSOS()
            }
        }
        return START_STICKY
    }

    override fun onBind(intent: Intent?): IBinder? {
        return null
    }

    private fun handleStartSOS() {
        serviceScope.launch {
            // Start location tracking in EMERGENCY mode (1-sec interval, best accuracy)
            val emergencyIntent = Intent(this@SOSService, LocationService::class.java).apply {
                action = LocationService.ACTION_START_TRACKING
                putExtra(LocationService.EXTRA_MODE, LocationService.TrackingMode.EMERGENCY)
            }
            startService(emergencyIntent)
        }

        // Activate SOS alert
        // - Location tracking started in EMERGENCY mode
        // - Initialize responder notification system
        // - Begin haptic feedback countdown
        // - Lock screen if needed
    }

    private fun handleStopSOS() {
        serviceScope.launch {
            // Stop location tracking
            val stopIntent = Intent(this@SOSService, LocationService::class.java).apply {
                action = LocationService.ACTION_STOP_TRACKING
            }
            startService(stopIntent)
        }

        // Stop SOS alert
        // - Location tracking stopped
        // - Clear haptic feedback
        // - Cancel responder notifications
        // - Unlock screen if needed
    }

    private fun handleCancelSOS() {
        serviceScope.launch {
            // Stop location tracking
            val stopIntent = Intent(this@SOSService, LocationService::class.java).apply {
                action = LocationService.ACTION_STOP_TRACKING
            }
            startService(stopIntent)
        }

        // Cancel active SOS alert
        // - Location tracking stopped
        // - Log cancellation event
        // - Stop foreground service
    }

    private fun createNotification() = NotificationCompat.Builder(this, CHANNEL_ID)
        .setContentTitle("TheWatch - Emergency Alert Active")
        .setContentText("SOS Alert is active. Emergency responders have been notified.")
        .setSmallIcon(android.R.drawable.ic_dialog_info)
        .setOngoing(true)
        .setPriority(NotificationCompat.PRIORITY_HIGH)
        .build()

    private fun createNotificationChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val channel = NotificationChannel(
                CHANNEL_ID,
                "SOS Service",
                NotificationManager.IMPORTANCE_HIGH
            ).apply {
                description = "Notifications for TheWatch SOS emergency alerts"
                enableVibration(true)
            }
            val manager = getSystemService(NotificationManager::class.java)
            manager?.createNotificationChannel(channel)
        }
    }

    override fun onDestroy() {
        serviceScope.cancel()
        super.onDestroy()
    }

    companion object {
        const val ACTION_START_SOS = "com.thewatch.app.action.START_SOS"
        const val ACTION_STOP_SOS = "com.thewatch.app.action.STOP_SOS"
        const val ACTION_CANCEL_SOS = "com.thewatch.app.action.CANCEL_SOS"
    }
}
