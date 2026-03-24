package com.thewatch.app.data.mock

import com.thewatch.app.data.model.AcknowledgmentResponse
import com.thewatch.app.data.model.NavigationDirections
import com.thewatch.app.data.model.Responder
import com.thewatch.app.data.repository.VolunteerRepository
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.flow
import javax.inject.Inject

class MockVolunteerRepository @Inject constructor() : VolunteerRepository {

    override suspend fun enrollAsResponder(
        userId: String,
        role: String,
        certifications: List<String>,
        hasVehicle: Boolean
    ): Result<Responder> {
        delay(1000)
        val responder = Responder(
            id = "responder_new_$userId",
            name = "Responder Name",
            type = role,
            latitude = 37.7749,
            longitude = -122.4194,
            eta = 5,
            certifications = certifications,
            hasVehicle = hasVehicle
        )
        return Result.success(responder)
    }

    override suspend fun updateAvailability(
        userId: String,
        isAvailable: Boolean,
        responseRadiusMeters: Int
    ): Result<Unit> {
        delay(800)
        return Result.success(Unit)
    }

    override suspend fun updateVehicleStatus(userId: String, hasVehicle: Boolean): Result<Unit> {
        delay(400)
        return Result.success(Unit)
    }

    override suspend fun updateSchedule(
        userId: String,
        schedule: Map<String, List<Pair<Int, Int>>>
    ): Result<Unit> {
        delay(800)
        return Result.success(Unit)
    }

    override suspend fun getResponderProfile(userId: String): Flow<Responder?> = flow {
        delay(500)
        emit(
            Responder(
                id = "responder_001",
                name = "Michael Chen",
                type = "EMT",
                latitude = 37.7749,
                longitude = -122.4194,
                eta = 3,
                certifications = listOf("EMT-Basic", "CPR"),
                hasVehicle = true
            )
        )
    }

    override suspend fun getResponseHistory(userId: String): Flow<List<Map<String, Any>>> = flow {
        delay(800)
        emit(
            listOf(
                mapOf(
                    "alertId" to "alert_001",
                    "timestamp" to System.currentTimeMillis() - 86400000,
                    "severity" to "CRITICAL",
                    "status" to "COMPLETED",
                    "responseTime" to 3,
                    "address" to "123 Main St, San Francisco, CA"
                ),
                mapOf(
                    "alertId" to "alert_002",
                    "timestamp" to System.currentTimeMillis() - 172800000,
                    "severity" to "HIGH",
                    "status" to "COMPLETED",
                    "responseTime" to 5,
                    "address" to "456 Oak Ave, San Francisco, CA"
                ),
                mapOf(
                    "alertId" to "alert_003",
                    "timestamp" to System.currentTimeMillis() - 259200000,
                    "severity" to "MEDIUM",
                    "status" to "DECLINED",
                    "responseTime" to null,
                    "address" to "789 Pine Rd, San Francisco, CA"
                )
            )
        )
    }

    override suspend fun acceptResponse(userId: String, alertId: String): Result<AcknowledgmentResponse> {
        delay(600)
        return Result.success(
            AcknowledgmentResponse(
                ackId = "ack_mock_001",
                requestId = alertId,
                responderId = userId,
                status = "EnRoute",
                estimatedArrival = "00:05:00",
                directions = NavigationDirections(
                    travelMode = "driving",
                    distanceMeters = 1200.0,
                    estimatedTravelTimeMinutes = 4.5,
                    googleMapsUrl = "https://www.google.com/maps/dir/?api=1&origin=37.7749,-122.4194&destination=37.78,-122.41&travelmode=driving",
                    appleMapsUrl = "https://maps.apple.com/?saddr=37.7749,-122.4194&daddr=37.78,-122.41&dirflg=d",
                    wazeUrl = "https://waze.com/ul?ll=37.78,-122.41&navigate=yes"
                )
            )
        )
    }

    override suspend fun declineResponse(userId: String, alertId: String): Result<Unit> {
        delay(600)
        return Result.success(Unit)
    }
}
