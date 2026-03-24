package com.thewatch.app.service

import android.app.NotificationManager
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.util.Log
import dagger.hilt.android.AndroidEntryPoint
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import javax.inject.Inject

/**
 * BroadcastReceiver for notification action button taps.
 *
 * When a user taps Accept, Decline, I'm OK, Need Help, Call 911, or Acknowledge
 * on a notification, this receiver processes the action and sends the response
 * back to the TheWatch backend.
 *
 * Flow:
 *   User taps "Accept" → NotificationActionReceiver.onReceive()
 *     → Dismiss notification
 *     → NotificationResponseHandler.sendResponse(ACCEPT, requestId)
 *       → POST /api/response/{requestId}/ack (for Accept)
 *       → POST /api/response/{requestId}/cancel (for Decline)
 *     → If Accept: start LocationCoordinator in emergency mode
 */
@AndroidEntryPoint
class NotificationActionReceiver : BroadcastReceiver() {

    @Inject
    lateinit var responseHandler: NotificationResponseHandler

    companion object {
        private const val TAG = "NotifActionReceiver"
    }

    override fun onReceive(context: Context, intent: Intent) {
        val action = intent.action ?: return
        val requestId = intent.getStringExtra(NotificationService.EXTRA_REQUEST_ID) ?: return
        val notificationId = intent.getStringExtra(NotificationService.EXTRA_NOTIFICATION_ID) ?: requestId
        val latitude = intent.getDoubleExtra(NotificationService.EXTRA_LATITUDE, 0.0)
        val longitude = intent.getDoubleExtra(NotificationService.EXTRA_LONGITUDE, 0.0)

        Log.i(TAG, "Action received: $action for request $requestId")

        // Dismiss the notification
        val notifManager = context.getSystemService(NotificationManager::class.java)
        dismissRelatedNotifications(notifManager, requestId)

        // Process the action
        val pendingResult = goAsync()
        CoroutineScope(Dispatchers.IO).launch {
            try {
                when (action) {
                    NotificationService.ACTION_ACCEPT -> {
                        Log.i(TAG, "ACCEPT for $requestId — sending acknowledgment")
                        responseHandler.sendAccept(requestId, notificationId, latitude, longitude)
                    }
                    NotificationService.ACTION_DECLINE -> {
                        Log.i(TAG, "DECLINE for $requestId")
                        responseHandler.sendDecline(requestId, notificationId)
                    }
                    NotificationService.ACTION_IM_OK -> {
                        Log.i(TAG, "I'M OK for $requestId")
                        responseHandler.sendImOk(requestId, notificationId)
                    }
                    NotificationService.ACTION_NEED_HELP -> {
                        Log.i(TAG, "NEED HELP for $requestId — triggering secondary SOS")
                        responseHandler.sendNeedHelp(requestId, notificationId, latitude, longitude)
                    }
                    NotificationService.ACTION_CALL_911 -> {
                        Log.i(TAG, "CALL 911 for $requestId")
                        responseHandler.sendCall911(requestId, notificationId)
                        // Launch dialer with 911
                        val dialIntent = Intent(Intent.ACTION_DIAL).apply {
                            data = android.net.Uri.parse("tel:911")
                            flags = Intent.FLAG_ACTIVITY_NEW_TASK
                        }
                        context.startActivity(dialIntent)
                    }
                    NotificationService.ACTION_ACKNOWLEDGE -> {
                        Log.i(TAG, "ACKNOWLEDGE for $requestId")
                        responseHandler.sendAcknowledge(requestId, notificationId)
                    }
                }
            } catch (e: Exception) {
                Log.e(TAG, "Failed to process action $action for $requestId", e)
            } finally {
                pendingResult.finish()
            }
        }
    }

    private fun dismissRelatedNotifications(manager: NotificationManager?, requestId: String) {
        val baseId = requestId.hashCode()
        manager?.cancel(baseId)           // SOS dispatch
        manager?.cancel(baseId + 1000)    // Escalation
        manager?.cancel(baseId + 2000)    // Check-in
        manager?.cancel(baseId + 3000)    // Evacuation
    }
}
