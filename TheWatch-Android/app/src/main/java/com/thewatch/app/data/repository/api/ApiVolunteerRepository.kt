/**
 * ┌──────────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                         │
 * │ File:    ApiVolunteerRepository.kt                                      │
 * │ Purpose: VolunteerRepository implementation backed by WatchApiClient.   │
 * │          Maps GET/PUT /api/response/participation and                   │
 * │          POST /api/response/{id}/ack to the volunteer interface.        │
 * │ Date:    2026-03-24                                                     │
 * │ Author:  Claude                                                         │
 * │ Deps:    WatchApiClient, Hilt                                           │
 * │                                                                         │
 * │ Usage Example:                                                          │
 * │   val repo: VolunteerRepository = apiVolunteerRepository                │
 * │   repo.enrollAsResponder("user_001", "EMT", listOf("CPR"), true)        │
 * │   repo.acceptResponse("user_001", "request_001")                        │
 * └──────────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.repository.api

import com.thewatch.app.data.api.WatchApiClient
import com.thewatch.app.data.api.ParticipationPreferencesDto
import com.thewatch.app.data.model.AcknowledgmentResponse
import com.thewatch.app.data.model.NavigationDirections
import com.thewatch.app.data.model.Responder
import com.thewatch.app.data.repository.VolunteerRepository
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.flow
import javax.inject.Inject

class ApiVolunteerRepository @Inject constructor(
    private val apiClient: WatchApiClient
) : VolunteerRepository {

    override suspend fun enrollAsResponder(
        userId: String,
        role: String,
        certifications: List<String>,
        hasVehicle: Boolean
    ): Result<Responder> {
        return try {
            val prefs = ParticipationPreferencesDto(
                userId = userId,
                isAvailable = true,
                optInScopes = listOf("CheckIn", "Emergency", "CommunityWatch"),
                certifications = certifications,
                hasVehicle = hasVehicle,
                maxRadiusMeters = 5000.0
            )
            apiClient.updateParticipation(prefs)

            Result.success(
                Responder(
                    id = userId,
                    name = "",
                    type = role,
                    certifications = certifications,
                    hasVehicle = hasVehicle
                )
            )
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    override suspend fun updateAvailability(
        userId: String,
        isAvailable: Boolean,
        responseRadiusMeters: Int
    ): Result<Unit> {
        return try {
            apiClient.setAvailability(userId, isAvailable)

            // Also update the participation prefs with the new radius
            try {
                val existing = apiClient.getParticipation(userId)
                apiClient.updateParticipation(
                    existing.copy(
                        isAvailable = isAvailable,
                        maxRadiusMeters = responseRadiusMeters.toDouble()
                    )
                )
            } catch (_: Exception) {
                // If participation prefs don't exist yet, just set availability
            }

            Result.success(Unit)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    override suspend fun updateVehicleStatus(userId: String, hasVehicle: Boolean): Result<Unit> {
        return try {
            val existing = apiClient.getParticipation(userId)
            apiClient.updateParticipation(existing.copy(hasVehicle = hasVehicle))
            Result.success(Unit)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    override suspend fun updateSchedule(
        userId: String,
        schedule: Map<String, List<Pair<Int, Int>>>
    ): Result<Unit> {
        return try {
            val existing = apiClient.getParticipation(userId)
            val scheduleStrings = schedule.mapValues { (_, slots) ->
                slots.map { (start, end) -> "${start}:00-${end}:00" }
            }
            apiClient.updateParticipation(existing.copy(weeklySchedule = scheduleStrings))
            Result.success(Unit)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    override suspend fun getResponderProfile(userId: String): Flow<Responder?> = flow {
        try {
            val prefs = apiClient.getParticipation(userId)
            emit(
                Responder(
                    id = userId,
                    name = "",
                    type = "Volunteer",
                    certifications = prefs.certifications,
                    hasVehicle = prefs.hasVehicle
                )
            )
        } catch (_: Exception) {
            emit(null)
        }
    }

    override suspend fun getResponseHistory(userId: String): Flow<List<Map<String, Any>>> = flow {
        try {
            val responses = apiClient.getActiveResponses(userId)
            val history = responses.map { dto ->
                mapOf<String, Any>(
                    "id" to dto.requestId,
                    "alertType" to dto.scope,
                    "timestamp" to dto.createdAt,
                    "location" to "%.4f, %.4f".format(dto.latitude, dto.longitude),
                    "outcome" to dto.status
                )
            }
            emit(history)
        } catch (_: Exception) {
            emit(emptyList())
        }
    }

    override suspend fun acceptResponse(userId: String, alertId: String): Result<AcknowledgmentResponse> {
        return try {
            val result = apiClient.acknowledgeResponse(
                requestId = alertId,
                responderId = userId,
                responderName = null,
                responderRole = "VOLUNTEER",
                hasVehicle = true
            )

            // Extract directions from the result map
            @Suppress("UNCHECKED_CAST")
            val directionsMap = result["Directions"] as? Map<String, Any> ?: emptyMap()

            val ack = AcknowledgmentResponse(
                ackId = result["AckId"]?.toString() ?: "",
                requestId = result["RequestId"]?.toString() ?: alertId,
                responderId = result["ResponderId"]?.toString() ?: userId,
                status = result["Status"]?.toString() ?: "EnRoute",
                estimatedArrival = result["EstimatedArrival"]?.toString(),
                directions = NavigationDirections(
                    travelMode = directionsMap["TravelMode"]?.toString() ?: "driving",
                    distanceMeters = (directionsMap["DistanceMeters"] as? Number)?.toDouble() ?: 0.0,
                    estimatedTravelTimeMinutes = (directionsMap["EstimatedTravelTime"] as? Number)?.toDouble(),
                    googleMapsUrl = directionsMap["GoogleMapsUrl"]?.toString() ?: "",
                    appleMapsUrl = directionsMap["AppleMapsUrl"]?.toString() ?: "",
                    wazeUrl = directionsMap["WazeUrl"]?.toString() ?: ""
                )
            )
            Result.success(ack)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    override suspend fun declineResponse(userId: String, alertId: String): Result<Unit> {
        // There is no explicit decline endpoint; the responder simply doesn't acknowledge.
        // We can send a status message instead.
        return try {
            apiClient.sendResponderMessage(
                requestId = alertId,
                senderId = userId,
                content = "Responder declined",
                messageType = "StatusUpdate"
            )
            Result.success(Unit)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }
}
