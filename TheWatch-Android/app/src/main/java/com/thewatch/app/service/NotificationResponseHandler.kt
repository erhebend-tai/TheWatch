package com.thewatch.app.service

import android.util.Log
import com.thewatch.app.data.repository.UserRepository
import javax.inject.Inject
import javax.inject.Singleton

/**
 * Handles outbound responses to notifications — the user's yes/no answer
 * sent back to the TheWatch backend API.
 *
 * Injected into NotificationActionReceiver and NotificationService.
 * Also handles FCM device token registration.
 *
 * All methods are suspend functions designed to be called from coroutine scopes.
 * In the mock implementation, responses are logged. The live implementation
 * will POST to the Dashboard API's ResponseController endpoints.
 */
@Singleton
class NotificationResponseHandler @Inject constructor(
    private val userRepository: UserRepository,
    private val locationCoordinator: LocationCoordinator
) {
    companion object {
        private const val TAG = "NotifResponseHandler"
    }

    /**
     * Accept an SOS dispatch — "I'm on my way."
     * - Sends acknowledgment to POST /api/response/{requestId}/ack
     * - Escalates location tracking to emergency mode (1-sec updates)
     * - Starts broadcasting location to the response group via SignalR
     */
    suspend fun sendAccept(
        requestId: String,
        notificationId: String,
        incidentLatitude: Double,
        incidentLongitude: Double
    ) {
        Log.i(TAG, "Sending ACCEPT for $requestId")

        // Escalate location to emergency mode so our position is tracked
        locationCoordinator.escalateToEmergency()

        val user = userRepository.getCurrentUser()
        val location = locationCoordinator.getLastKnownLocation()

        // Calculate distance to incident
        val distanceMeters = if (location != null) {
            calculateDistance(
                location.latitude, location.longitude,
                incidentLatitude, incidentLongitude
            )
        } else 0.0

        // TODO: Replace with actual Retrofit API call in live implementation
        // POST /api/response/{requestId}/ack
        // Body: { responderId, responderName, latitude, longitude, distanceMeters, estimatedArrivalMinutes }
        Log.w(TAG, "[MOCK] POST /api/response/$requestId/ack — " +
            "responderId=${user?.id}, distance=${distanceMeters}m")
    }

    /**
     * Decline an SOS dispatch — "I can't help right now."
     */
    suspend fun sendDecline(requestId: String, notificationId: String) {
        Log.i(TAG, "Sending DECLINE for $requestId")

        // TODO: POST to backend to record the decline
        Log.w(TAG, "[MOCK] Decline recorded for $requestId")
    }

    /**
     * Respond "I'm OK" to a check-in request.
     */
    suspend fun sendImOk(requestId: String, notificationId: String) {
        Log.i(TAG, "Sending IM_OK for $requestId")

        // TODO: POST to backend
        Log.w(TAG, "[MOCK] I'm OK recorded for $requestId")
    }

    /**
     * Respond "Need Help" — this is a secondary SOS trigger.
     * The person receiving a check-in actually needs help themselves.
     */
    suspend fun sendNeedHelp(
        requestId: String,
        notificationId: String,
        latitude: Double,
        longitude: Double
    ) {
        Log.w(TAG, "NEED HELP received for $requestId — triggering secondary SOS")

        // Escalate our own location
        locationCoordinator.escalateToEmergency()

        // TODO: POST /api/response/trigger with scope=Neighborhood
        // This creates a NEW response request for the person who needs help
        Log.w(TAG, "[MOCK] Secondary SOS triggered from check-in response")
    }

    /**
     * Call 911 action — logs the escalation. Dialer launch is handled by the receiver.
     */
    suspend fun sendCall911(requestId: String, notificationId: String) {
        Log.w(TAG, "CALL 911 for $requestId — escalation logged")

        // TODO: POST to backend to record 911 escalation event
        Log.w(TAG, "[MOCK] 911 escalation recorded for $requestId")
    }

    /**
     * Acknowledge an evacuation notice — "I see it, I'm evacuating."
     */
    suspend fun sendAcknowledge(requestId: String, notificationId: String) {
        Log.i(TAG, "Sending ACKNOWLEDGE for $requestId")

        // TODO: POST to backend
        Log.w(TAG, "[MOCK] Acknowledgment recorded for $requestId")
    }

    /**
     * Register FCM device token with the backend.
     * Called when FirebaseMessagingService.onNewToken() fires.
     */
    suspend fun registerDeviceToken(token: String) {
        val user = userRepository.getCurrentUser()
        Log.i(TAG, "Registering FCM token for user ${user?.id}: ${token.take(20)}...")

        // TODO: POST /api/response/device/register
        // Body: { userId, deviceToken, platform: "ANDROID", deviceName }
        Log.w(TAG, "[MOCK] Device token registered")
    }

    /**
     * Haversine distance calculation (meters).
     */
    private fun calculateDistance(
        lat1: Double, lon1: Double,
        lat2: Double, lon2: Double
    ): Double {
        val r = 6371000.0 // Earth radius in meters
        val dLat = Math.toRadians(lat2 - lat1)
        val dLon = Math.toRadians(lon2 - lon1)
        val a = Math.sin(dLat / 2) * Math.sin(dLat / 2) +
                Math.cos(Math.toRadians(lat1)) * Math.cos(Math.toRadians(lat2)) *
                Math.sin(dLon / 2) * Math.sin(dLon / 2)
        val c = 2 * Math.atan2(Math.sqrt(a), Math.sqrt(1 - a))
        return r * c
    }
}
