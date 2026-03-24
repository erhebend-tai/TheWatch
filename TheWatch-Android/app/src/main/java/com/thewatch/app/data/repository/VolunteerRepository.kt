package com.thewatch.app.data.repository

import com.thewatch.app.data.model.AcknowledgmentResponse
import com.thewatch.app.data.model.Responder
import kotlinx.coroutines.flow.Flow

interface VolunteerRepository {
    suspend fun enrollAsResponder(
        userId: String,
        role: String,
        certifications: List<String>,
        hasVehicle: Boolean = false
    ): Result<Responder>
    suspend fun updateAvailability(userId: String, isAvailable: Boolean, responseRadiusMeters: Int): Result<Unit>
    suspend fun updateVehicleStatus(userId: String, hasVehicle: Boolean): Result<Unit>
    suspend fun updateSchedule(userId: String, schedule: Map<String, List<Pair<Int, Int>>>): Result<Unit>
    suspend fun getResponderProfile(userId: String): Flow<Responder?>
    suspend fun getResponseHistory(userId: String): Flow<List<Map<String, Any>>>

    /**
     * Accept an incident response. Returns [AcknowledgmentResponse] containing
     * navigation directions (Google Maps / Waze deep links) to the incident.
     *
     * Example:
     *   val result = repository.acceptResponse(userId, alertId)
     *   result.onSuccess { ack ->
     *       // Launch navigation
     *       val intent = Intent(Intent.ACTION_VIEW, Uri.parse(ack.directions.googleMapsUrl))
     *       startActivity(intent)
     *   }
     */
    suspend fun acceptResponse(userId: String, alertId: String): Result<AcknowledgmentResponse>
    suspend fun declineResponse(userId: String, alertId: String): Result<Unit>
}
