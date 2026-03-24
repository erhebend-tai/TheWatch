package com.thewatch.app.data.repository.mock

import com.thewatch.app.data.model.AcknowledgmentResponse
import com.thewatch.app.data.model.NavigationDirections
import com.thewatch.app.data.model.Responder
import com.thewatch.app.data.repository.VolunteerRepository
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.flowOf
import javax.inject.Inject

class MockVolunteerRepository @Inject constructor() : VolunteerRepository {

    private var responderProfile: Responder? = null
    private val responseHistory = mutableListOf<Map<String, Any>>()

    override suspend fun enrollAsResponder(
        userId: String,
        role: String,
        certifications: List<String>,
        hasVehicle: Boolean
    ): Result<Responder> {
        delay(1000)
        val responder = Responder(
            id = userId,
            name = "Alex Rivera",
            type = role,
            distance = 0.0,
            latitude = 40.7128,
            longitude = -74.0060,
            eta = 0,
            certifications = certifications,
            hasVehicle = hasVehicle
        )
        responderProfile = responder
        return Result.success(responder)
    }

    override suspend fun updateAvailability(
        userId: String,
        isAvailable: Boolean,
        responseRadiusMeters: Int
    ): Result<Unit> {
        delay(600)
        responderProfile = responderProfile?.copy()
        return Result.success(Unit)
    }

    override suspend fun updateVehicleStatus(userId: String, hasVehicle: Boolean): Result<Unit> {
        delay(400)
        responderProfile = responderProfile?.copy(hasVehicle = hasVehicle)
        return Result.success(Unit)
    }

    override suspend fun updateSchedule(
        userId: String,
        schedule: Map<String, List<Pair<Int, Int>>>
    ): Result<Unit> {
        delay(700)
        return Result.success(Unit)
    }

    override suspend fun getResponderProfile(userId: String): Flow<Responder?> {
        return flowOf(responderProfile)
    }

    override suspend fun getResponseHistory(userId: String): Flow<List<Map<String, Any>>> {
        delay(500)
        val history = listOf(
            mapOf(
                "id" to "response_001",
                "alertType" to "Medical Emergency",
                "timestamp" to "2026-03-22T14:30:00",
                "location" to "Central Park, Manhattan, NY",
                "outcome" to "Assisted"
            ),
            mapOf(
                "id" to "response_002",
                "alertType" to "Lost Person",
                "timestamp" to "2026-03-20T09:15:00",
                "location" to "Times Square, Manhattan, NY",
                "outcome" to "Resolved"
            ),
            mapOf(
                "id" to "response_003",
                "alertType" to "Welfare Check",
                "timestamp" to "2026-03-18T16:45:00",
                "location" to "Washington Square Park, Manhattan, NY",
                "outcome" to "Completed"
            )
        )
        return flowOf(history)
    }

    override suspend fun acceptResponse(userId: String, alertId: String): Result<AcknowledgmentResponse> {
        delay(600)
        responseHistory.add(
            mapOf(
                "userId" to userId,
                "alertId" to alertId,
                "action" to "accepted",
                "timestamp" to System.currentTimeMillis()
            )
        )
        return Result.success(
            AcknowledgmentResponse(
                ackId = "ack_mock_002",
                requestId = alertId,
                responderId = userId,
                status = "EnRoute",
                estimatedArrival = "00:04:00",
                directions = NavigationDirections(
                    travelMode = "driving",
                    distanceMeters = 950.0,
                    estimatedTravelTimeMinutes = 3.5,
                    googleMapsUrl = "https://www.google.com/maps/dir/?api=1&origin=40.7128,-74.006&destination=40.72,-74.00&travelmode=driving",
                    appleMapsUrl = "https://maps.apple.com/?saddr=40.7128,-74.006&daddr=40.72,-74.00&dirflg=d",
                    wazeUrl = "https://waze.com/ul?ll=40.72,-74.00&navigate=yes"
                )
            )
        )
    }

    override suspend fun declineResponse(userId: String, alertId: String): Result<Unit> {
        delay(500)
        responseHistory.add(
            mapOf(
                "userId" to userId,
                "alertId" to alertId,
                "action" to "declined",
                "timestamp" to System.currentTimeMillis()
            )
        )
        return Result.success(Unit)
    }
}
