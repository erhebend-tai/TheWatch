/**
 * ┌──────────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                         │
 * │ File:    ApiAlertRepository.kt                                          │
 * │ Purpose: AlertRepository implementation backed by WatchApiClient.       │
 * │          Calls POST /api/response/trigger, GET /api/response/active,    │
 * │          and POST /api/response/{id}/cancel via the HTTP API.           │
 * │ Date:    2026-03-24                                                     │
 * │ Author:  Claude                                                         │
 * │ Deps:    WatchApiClient, WatchHubConnection, Hilt                       │
 * │                                                                         │
 * │ Usage Example:                                                          │
 * │   // Injected via Hilt as AlertRepository                               │
 * │   val repo: AlertRepository = apiAlertRepository                        │
 * │   val result = repo.activateAlert(alert)                                │
 * │   repo.getNearbyResponders(lat, lng, 2000.0).collect { responders -> }  │
 * └──────────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.repository.api

import com.thewatch.app.data.api.WatchApiClient
import com.thewatch.app.data.model.Alert
import com.thewatch.app.data.model.CommunityAlert
import com.thewatch.app.data.model.Responder
import com.thewatch.app.data.repository.AlertRepository
import com.thewatch.app.data.signalr.WatchHubConnection
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.flow
import javax.inject.Inject

class ApiAlertRepository @Inject constructor(
    private val apiClient: WatchApiClient,
    private val hubConnection: WatchHubConnection
) : AlertRepository {

    private var currentRequestId: String? = null

    override suspend fun activateAlert(alert: Alert): Result<Alert> {
        return try {
            val response = apiClient.triggerResponse(
                userId = alert.userId,
                scope = "CheckIn",
                latitude = alert.latitude,
                longitude = alert.longitude,
                description = alert.description,
                triggerSource = "MANUAL_BUTTON"
            )
            currentRequestId = response.requestId

            // Join the SignalR response group for real-time updates
            hubConnection.joinResponseGroup(response.requestId)

            val updatedAlert = Alert(
                id = response.requestId,
                userId = alert.userId,
                latitude = alert.latitude,
                longitude = alert.longitude,
                description = alert.description,
                severity = response.scope,
                status = response.status
            )
            Result.success(updatedAlert)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    override suspend fun cancelAlert(alertId: String): Result<Unit> {
        return try {
            apiClient.cancelResponse(alertId, "User cancelled")

            // Leave the response group
            hubConnection.leaveResponseGroup(alertId)
            currentRequestId = null

            Result.success(Unit)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    override suspend fun getActiveAlert(userId: String): Flow<Alert?> = flow {
        try {
            val responses = apiClient.getActiveResponses(userId)
            if (responses.isNotEmpty()) {
                val active = responses.first()
                emit(
                    Alert(
                        id = active.requestId,
                        userId = active.userId ?: userId,
                        latitude = active.latitude,
                        longitude = active.longitude,
                        description = "",
                        severity = active.scope ?: "CheckIn",
                        status = active.status ?: "ACTIVE",
                        respondersCount = active.acknowledgedResponders.size
                    )
                )
            } else {
                emit(null)
            }
        } catch (e: Exception) {
            emit(null)
        }
    }

    override suspend fun getNearbyResponders(
        latitude: Double,
        longitude: Double,
        radiusMeters: Double
    ): Flow<List<Responder>> = flow {
        val requestId = currentRequestId
        if (requestId != null) {
            try {
                val situation = apiClient.getSituation(requestId)
                val responders = situation.responders.map { ack ->
                    Responder(
                        id = ack.responderId,
                        name = ack.responderName,
                        type = ack.responderRole,
                        distance = ack.distanceMeters,
                        latitude = ack.latitude,
                        longitude = ack.longitude,
                        eta = ack.estimatedArrival?.let {
                            try { it.split(":")[1].toInt() } catch (_: Exception) { 0 }
                        } ?: 0,
                        hasVehicle = ack.hasVehicle
                    )
                }
                emit(responders)

                // Poll for updates every 5 seconds (SignalR provides real-time,
                // but this is a fallback for when the hub is in mock mode)
                while (true) {
                    delay(5000)
                    try {
                        val updated = apiClient.getSituation(requestId)
                        val updatedResponders = updated.responders.map { ack ->
                            Responder(
                                id = ack.responderId,
                                name = ack.responderName,
                                type = ack.responderRole,
                                distance = ack.distanceMeters,
                                latitude = ack.latitude,
                                longitude = ack.longitude,
                                eta = ack.estimatedArrival?.let {
                                    try { it.split(":")[1].toInt() } catch (_: Exception) { 0 }
                                } ?: 0,
                                hasVehicle = ack.hasVehicle
                            )
                        }
                        emit(updatedResponders)
                    } catch (_: Exception) {
                        break
                    }
                }
            } catch (e: Exception) {
                emit(emptyList())
            }
        } else {
            emit(emptyList())
        }
    }

    override suspend fun getNearbyAlerts(
        latitude: Double,
        longitude: Double,
        radiusMeters: Double
    ): Flow<List<CommunityAlert>> = flow {
        // Community alerts are not yet a separate backend endpoint;
        // this will be wired when GET /api/alerts/nearby is added.
        emit(emptyList())
    }

    override suspend fun reportResponderMissing(alertId: String, responderId: String): Result<Unit> {
        // Will map to a future endpoint; for now delegate to message channel
        return try {
            apiClient.sendResponderMessage(
                requestId = alertId,
                senderId = "system",
                content = "Responder $responderId reported as missing",
                messageType = "StatusUpdate"
            )
            Result.success(Unit)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    override suspend fun confirmAlertResolution(alertId: String): Result<Unit> {
        return try {
            apiClient.resolveResponse(alertId, resolvedBy = "user")
            hubConnection.leaveResponseGroup(alertId)
            currentRequestId = null
            Result.success(Unit)
        } catch (e: Exception) {
            Result.failure(e)
        }
    }
}
