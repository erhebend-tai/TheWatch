package com.thewatch.app.service

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.media.AudioAttributes
import android.media.RingtoneManager
import android.os.Build
import androidx.core.app.NotificationCompat
import com.google.firebase.messaging.FirebaseMessagingService
import com.google.firebase.messaging.RemoteMessage
import dagger.hilt.android.AndroidEntryPoint
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.launch
import javax.inject.Inject

/**
 * TheWatch FCM Notification Service.
 *
 * Handles inbound push notifications from the backend and presents them
 * as actionable notifications with Accept/Decline buttons.
 *
 * Notification categories (matching the C# NotificationCategory enum):
 *   - SOS_DISPATCH      → "Someone needs help" with Accept / Decline actions
 *   - SOS_UPDATE         → Status update for an active response
 *   - SOS_CANCELLED      → Response was cancelled
 *   - SOS_RESOLVED       → Response was resolved
 *   - ESCALATION_ALERT   → Escalation triggered with Accept / Decline / Call 911
 *   - CHECK_IN_REQUEST   → Check-in with I'm OK / Need Help actions
 *   - EVACUATION_NOTICE  → Evacuation with Acknowledge / Need Assistance actions
 *
 * Deep links: thewatch://response/{requestId}
 */
@AndroidEntryPoint
class NotificationService : FirebaseMessagingService() {

    @Inject
    lateinit var notificationResponseHandler: NotificationResponseHandler

    private val serviceScope = CoroutineScope(Dispatchers.IO + SupervisorJob())

    companion object {
        // Notification channels — separate channels for different priority levels
        const val CHANNEL_CRITICAL = "thewatch_critical"
        const val CHANNEL_HIGH = "thewatch_high"
        const val CHANNEL_NORMAL = "thewatch_normal"

        // Notification action intents
        const val ACTION_ACCEPT = "com.thewatch.app.notification.ACCEPT"
        const val ACTION_DECLINE = "com.thewatch.app.notification.DECLINE"
        const val ACTION_IM_OK = "com.thewatch.app.notification.IM_OK"
        const val ACTION_NEED_HELP = "com.thewatch.app.notification.NEED_HELP"
        const val ACTION_CALL_911 = "com.thewatch.app.notification.CALL_911"
        const val ACTION_ACKNOWLEDGE = "com.thewatch.app.notification.ACKNOWLEDGE"

        // Intent extras
        const val EXTRA_REQUEST_ID = "request_id"
        const val EXTRA_NOTIFICATION_ID = "notification_id"
        const val EXTRA_CATEGORY = "category"
        const val EXTRA_LATITUDE = "latitude"
        const val EXTRA_LONGITUDE = "longitude"

        // Notification IDs — use requestId hashcode for unique-per-request grouping
        private const val NOTIFICATION_GROUP = "thewatch_sos_group"

        /**
         * Create all notification channels. Call once from Application.onCreate().
         */
        fun createNotificationChannels(context: Context) {
            if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) return

            val manager = context.getSystemService(NotificationManager::class.java) ?: return
            val alarmSound = RingtoneManager.getDefaultUri(RingtoneManager.TYPE_ALARM)

            // Critical — bypasses DND, plays alarm, vibrates
            val critical = NotificationChannel(
                CHANNEL_CRITICAL,
                "Life-Safety Alerts",
                NotificationManager.IMPORTANCE_HIGH
            ).apply {
                description = "Critical emergency alerts that bypass Do Not Disturb"
                enableVibration(true)
                vibrationPattern = longArrayOf(0, 500, 200, 500, 200, 500)
                setSound(
                    alarmSound,
                    AudioAttributes.Builder()
                        .setUsage(AudioAttributes.USAGE_ALARM)
                        .setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION)
                        .build()
                )
                setBypassDnd(true)
                lockscreenVisibility = NotificationCompat.VISIBILITY_PUBLIC
            }

            // High — standard urgent notifications
            val high = NotificationChannel(
                CHANNEL_HIGH,
                "Emergency Alerts",
                NotificationManager.IMPORTANCE_HIGH
            ).apply {
                description = "Urgent emergency response notifications"
                enableVibration(true)
                vibrationPattern = longArrayOf(0, 300, 200, 300)
            }

            // Normal — informational updates
            val normal = NotificationChannel(
                CHANNEL_NORMAL,
                "Response Updates",
                NotificationManager.IMPORTANCE_DEFAULT
            ).apply {
                description = "Status updates and informational notifications"
            }

            manager.createNotificationChannels(listOf(critical, high, normal))
        }
    }

    override fun onNewToken(token: String) {
        super.onNewToken(token)
        // Send the new token to our backend for device registration
        serviceScope.launch {
            notificationResponseHandler.registerDeviceToken(token)
        }
    }

    override fun onMessageReceived(message: RemoteMessage) {
        super.onMessageReceived(message)

        val data = message.data
        val category = data["category"] ?: "SOS_UPDATE"
        val requestId = data["request_id"] ?: return
        val notificationId = data["notification_id"] ?: requestId
        val priority = data["priority"] ?: "HIGH"
        val title = data["title"] ?: message.notification?.title ?: "TheWatch Alert"
        val body = data["body"] ?: message.notification?.body ?: "Emergency alert received"
        val latitude = data["latitude"]?.toDoubleOrNull() ?: 0.0
        val longitude = data["longitude"]?.toDoubleOrNull() ?: 0.0
        val distance = data["distance_meters"]?.toDoubleOrNull()
        val requestorName = data["requestor_name"]
        val scope = data["scope"]

        when (category) {
            "SOS_DISPATCH" -> showSosDispatchNotification(
                requestId, notificationId, title, body,
                priority, latitude, longitude, distance, requestorName, scope
            )
            "ESCALATION_ALERT" -> showEscalationNotification(
                requestId, notificationId, title, body,
                priority, latitude, longitude
            )
            "CHECK_IN_REQUEST" -> showCheckInNotification(
                requestId, notificationId, title, body,
                latitude, longitude
            )
            "EVACUATION_NOTICE" -> showEvacuationNotification(
                requestId, notificationId, title, body,
                latitude, longitude
            )
            "SOS_CANCELLED" -> showInfoNotification(
                requestId, notificationId, title, body, autoDismiss = true
            )
            "SOS_RESOLVED" -> showInfoNotification(
                requestId, notificationId, title, body, autoDismiss = false
            )
            else -> showInfoNotification(
                requestId, notificationId, title, body, autoDismiss = false
            )
        }
    }

    // ─────────────────────────────────────────────────────────────
    // SOS Dispatch — Accept / Decline
    // ─────────────────────────────────────────────────────────────

    private fun showSosDispatchNotification(
        requestId: String,
        notificationId: String,
        title: String,
        body: String,
        priority: String,
        latitude: Double,
        longitude: Double,
        distance: Double?,
        requestorName: String?,
        scope: String?
    ) {
        val notifId = requestId.hashCode()
        val channel = if (priority == "CRITICAL") CHANNEL_CRITICAL else CHANNEL_HIGH

        val distanceText = distance?.let { d ->
            if (d < 1000) "${d.toInt()}m away" else "${String.format("%.1f", d / 1000)}km away"
        }
        val fullBody = listOfNotNull(body, distanceText).joinToString(" · ")

        // Accept action
        val acceptIntent = createActionIntent(
            ACTION_ACCEPT, requestId, notificationId, latitude, longitude
        )
        val acceptPending = PendingIntent.getBroadcast(
            this, notifId * 10 + 1, acceptIntent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )

        // Decline action
        val declineIntent = createActionIntent(
            ACTION_DECLINE, requestId, notificationId, latitude, longitude
        )
        val declinePending = PendingIntent.getBroadcast(
            this, notifId * 10 + 2, declineIntent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )

        // Tap action — deep link to response detail
        val tapIntent = createDeepLinkIntent(requestId)
        val tapPending = PendingIntent.getActivity(
            this, notifId, tapIntent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )

        val notification = NotificationCompat.Builder(this, channel)
            .setContentTitle(title)
            .setContentText(fullBody)
            .setSmallIcon(android.R.drawable.ic_dialog_alert)
            .setPriority(NotificationCompat.PRIORITY_MAX)
            .setCategory(NotificationCompat.CATEGORY_ALARM)
            .setVisibility(NotificationCompat.VISIBILITY_PUBLIC)
            .setAutoCancel(false)
            .setOngoing(true)  // Can't swipe away — must respond
            .setContentIntent(tapPending)
            .addAction(android.R.drawable.ic_menu_send, "Accept", acceptPending)
            .addAction(android.R.drawable.ic_menu_close_clear_cancel, "Decline", declinePending)
            .setGroup(NOTIFICATION_GROUP)
            .setStyle(NotificationCompat.BigTextStyle().bigText(fullBody))
            .build()

        val manager = getSystemService(NotificationManager::class.java)
        manager?.notify(notifId, notification)
    }

    // ─────────────────────────────────────────────────────────────
    // Escalation — Accept / Decline / Call 911
    // ─────────────────────────────────────────────────────────────

    private fun showEscalationNotification(
        requestId: String,
        notificationId: String,
        title: String,
        body: String,
        priority: String,
        latitude: Double,
        longitude: Double
    ) {
        val notifId = requestId.hashCode() + 1000

        val acceptPending = PendingIntent.getBroadcast(
            this, notifId * 10 + 1,
            createActionIntent(ACTION_ACCEPT, requestId, notificationId, latitude, longitude),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
        val declinePending = PendingIntent.getBroadcast(
            this, notifId * 10 + 2,
            createActionIntent(ACTION_DECLINE, requestId, notificationId, latitude, longitude),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
        val call911Pending = PendingIntent.getBroadcast(
            this, notifId * 10 + 3,
            createActionIntent(ACTION_CALL_911, requestId, notificationId, latitude, longitude),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )

        val notification = NotificationCompat.Builder(this, CHANNEL_CRITICAL)
            .setContentTitle(title)
            .setContentText(body)
            .setSmallIcon(android.R.drawable.ic_dialog_alert)
            .setPriority(NotificationCompat.PRIORITY_MAX)
            .setCategory(NotificationCompat.CATEGORY_ALARM)
            .setVisibility(NotificationCompat.VISIBILITY_PUBLIC)
            .setAutoCancel(false)
            .setOngoing(true)
            .addAction(android.R.drawable.ic_menu_send, "Accept", acceptPending)
            .addAction(android.R.drawable.ic_menu_close_clear_cancel, "Decline", declinePending)
            .addAction(android.R.drawable.ic_menu_call, "Call 911", call911Pending)
            .setGroup(NOTIFICATION_GROUP)
            .build()

        val manager = getSystemService(NotificationManager::class.java)
        manager?.notify(notifId, notification)
    }

    // ─────────────────────────────────────────────────────────────
    // Check-In — I'm OK / Need Help
    // ─────────────────────────────────────────────────────────────

    private fun showCheckInNotification(
        requestId: String,
        notificationId: String,
        title: String,
        body: String,
        latitude: Double,
        longitude: Double
    ) {
        val notifId = requestId.hashCode() + 2000

        val imOkPending = PendingIntent.getBroadcast(
            this, notifId * 10 + 1,
            createActionIntent(ACTION_IM_OK, requestId, notificationId, latitude, longitude),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
        val needHelpPending = PendingIntent.getBroadcast(
            this, notifId * 10 + 2,
            createActionIntent(ACTION_NEED_HELP, requestId, notificationId, latitude, longitude),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )

        val notification = NotificationCompat.Builder(this, CHANNEL_HIGH)
            .setContentTitle(title)
            .setContentText(body)
            .setSmallIcon(android.R.drawable.ic_dialog_info)
            .setPriority(NotificationCompat.PRIORITY_HIGH)
            .setCategory(NotificationCompat.CATEGORY_MESSAGE)
            .setAutoCancel(true)
            .addAction(android.R.drawable.ic_menu_view, "I'm OK", imOkPending)
            .addAction(android.R.drawable.ic_dialog_alert, "Need Help", needHelpPending)
            .setGroup(NOTIFICATION_GROUP)
            .setStyle(NotificationCompat.BigTextStyle().bigText(body))
            .build()

        val manager = getSystemService(NotificationManager::class.java)
        manager?.notify(notifId, notification)
    }

    // ─────────────────────────────────────────────────────────────
    // Evacuation — Acknowledge / Need Assistance
    // ─────────────────────────────────────────────────────────────

    private fun showEvacuationNotification(
        requestId: String,
        notificationId: String,
        title: String,
        body: String,
        latitude: Double,
        longitude: Double
    ) {
        val notifId = requestId.hashCode() + 3000

        val ackPending = PendingIntent.getBroadcast(
            this, notifId * 10 + 1,
            createActionIntent(ACTION_ACKNOWLEDGE, requestId, notificationId, latitude, longitude),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )
        val helpPending = PendingIntent.getBroadcast(
            this, notifId * 10 + 2,
            createActionIntent(ACTION_NEED_HELP, requestId, notificationId, latitude, longitude),
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )

        val notification = NotificationCompat.Builder(this, CHANNEL_CRITICAL)
            .setContentTitle(title)
            .setContentText(body)
            .setSmallIcon(android.R.drawable.ic_dialog_alert)
            .setPriority(NotificationCompat.PRIORITY_MAX)
            .setCategory(NotificationCompat.CATEGORY_ALARM)
            .setVisibility(NotificationCompat.VISIBILITY_PUBLIC)
            .setAutoCancel(false)
            .setOngoing(true)
            .addAction(android.R.drawable.ic_menu_view, "Acknowledged", ackPending)
            .addAction(android.R.drawable.ic_dialog_alert, "Need Assistance", helpPending)
            .setGroup(NOTIFICATION_GROUP)
            .setStyle(NotificationCompat.BigTextStyle().bigText(body))
            .build()

        val manager = getSystemService(NotificationManager::class.java)
        manager?.notify(notifId, notification)
    }

    // ─────────────────────────────────────────────────────────────
    // Info notification (cancelled, resolved, updates)
    // ─────────────────────────────────────────────────────────────

    private fun showInfoNotification(
        requestId: String,
        notificationId: String,
        title: String,
        body: String,
        autoDismiss: Boolean
    ) {
        val notifId = requestId.hashCode() + 4000

        val notification = NotificationCompat.Builder(this, CHANNEL_NORMAL)
            .setContentTitle(title)
            .setContentText(body)
            .setSmallIcon(android.R.drawable.ic_dialog_info)
            .setPriority(NotificationCompat.PRIORITY_DEFAULT)
            .setAutoCancel(true)
            .setGroup(NOTIFICATION_GROUP)
            .build()

        val manager = getSystemService(NotificationManager::class.java)
        manager?.notify(notifId, notification)

        // Auto-dismiss previous ongoing notification for this request
        if (autoDismiss) {
            manager?.cancel(requestId.hashCode())      // Cancel SOS dispatch
            manager?.cancel(requestId.hashCode() + 1000) // Cancel escalation
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────

    private fun createActionIntent(
        action: String,
        requestId: String,
        notificationId: String,
        latitude: Double,
        longitude: Double
    ): Intent = Intent(this, NotificationActionReceiver::class.java).apply {
        this.action = action
        putExtra(EXTRA_REQUEST_ID, requestId)
        putExtra(EXTRA_NOTIFICATION_ID, notificationId)
        putExtra(EXTRA_LATITUDE, latitude)
        putExtra(EXTRA_LONGITUDE, longitude)
    }

    private fun createDeepLinkIntent(requestId: String): Intent {
        return Intent(Intent.ACTION_VIEW).apply {
            data = android.net.Uri.parse("thewatch://response/$requestId")
            flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP
        }
    }

    override fun onDestroy() {
        serviceScope.cancel()
        super.onDestroy()
    }
}
