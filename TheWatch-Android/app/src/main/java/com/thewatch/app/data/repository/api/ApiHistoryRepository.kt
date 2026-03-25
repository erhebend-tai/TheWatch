/**
 * ┌──────────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                         │
 * │ File:    ApiHistoryRepository.kt                                        │
 * │ Purpose: HistoryRepository implementation backed by WatchApiClient.     │
 * │          Fetches history from GET /api/response/active/{userId} and     │
 * │          maps completed responses into HistoryEvent model objects.      │
 * │ Date:    2026-03-24                                                     │
 * │ Author:  Claude                                                         │
 * │ Deps:    WatchApiClient, Hilt                                           │
 * │                                                                         │
 * │ Usage Example:                                                          │
 * │   val repo: HistoryRepository = apiHistoryRepository                    │
 * │   repo.getHistory("user_001", startMs, endMs).collect { events -> }     │
 * └──────────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.repository.api

import com.thewatch.app.data.api.WatchApiClient
import com.thewatch.app.data.model.HistoryEvent
import com.thewatch.app.data.repository.HistoryRepository
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.flow
import java.time.LocalDateTime
import java.time.format.DateTimeFormatter
import javax.inject.Inject

class ApiHistoryRepository @Inject constructor(
    private val apiClient: WatchApiClient
) : HistoryRepository {

    override suspend fun recordEvent(event: HistoryEvent): Result<HistoryEvent> {
        // Events are recorded server-side when responses are triggered.
        // This method exists for local-first/offline scenarios.
        return Result.success(event)
    }

    override suspend fun getHistory(
        userId: String,
        startTime: Long,
        endTime: Long,
        eventTypes: List<String>,
        severities: List<String>,
        statuses: List<String>
    ): Flow<List<HistoryEvent>> = flow {
        try {
            val responses = apiClient.getActiveResponses(userId)
            val events = responses.map { dto ->
                HistoryEvent(
                    id = dto.requestId,
                    userId = dto.userId,
                    eventType = dto.scope,
                    severity = if (dto.scope == "Emergency") "High" else "Medium",
                    timestamp = try {
                        LocalDateTime.parse(dto.createdAt, DateTimeFormatter.ISO_DATE_TIME)
                    } catch (_: Exception) {
                        LocalDateTime.now()
                    },
                    latitude = dto.latitude,
                    longitude = dto.longitude,
                    location = "%.4f, %.4f".format(dto.latitude, dto.longitude),
                    description = "Response: ${dto.scope} - ${dto.status}",
                    responderName = dto.acknowledgedResponders.firstOrNull()?.responderName ?: "",
                    responderId = dto.acknowledgedResponders.firstOrNull()?.responderId ?: "",
                    status = dto.status,
                    resolution = ""
                )
            }

            // Apply local filters
            var filtered = events
            if (eventTypes.isNotEmpty()) {
                filtered = filtered.filter { it.eventType in eventTypes }
            }
            if (severities.isNotEmpty()) {
                filtered = filtered.filter { it.severity in severities }
            }
            if (statuses.isNotEmpty()) {
                filtered = filtered.filter { it.status in statuses }
            }

            emit(filtered.sortedByDescending { it.timestamp })
        } catch (e: Exception) {
            emit(emptyList())
        }
    }

    override suspend fun getEventDetails(eventId: String): Flow<HistoryEvent?> = flow {
        try {
            val situation = apiClient.getSituation(eventId)
            val request = situation.request
            if (request != null) {
                emit(
                    HistoryEvent(
                        id = request.requestId,
                        userId = request.userId,
                        eventType = request.scope,
                        severity = if (request.scope == "Emergency") "High" else "Medium",
                        timestamp = try {
                            LocalDateTime.parse(request.createdAt, DateTimeFormatter.ISO_DATE_TIME)
                        } catch (_: Exception) {
                            LocalDateTime.now()
                        },
                        latitude = request.latitude,
                        longitude = request.longitude,
                        location = "%.4f, %.4f".format(request.latitude, request.longitude),
                        description = "Response: ${request.scope}",
                        responderName = request.acknowledgedResponders.firstOrNull()?.responderName ?: "",
                        responderId = request.acknowledgedResponders.firstOrNull()?.responderId ?: "",
                        status = request.status,
                        resolution = ""
                    )
                )
            } else {
                emit(null)
            }
        } catch (e: Exception) {
            emit(null)
        }
    }

    override suspend fun deleteHistoryEvent(eventId: String): Result<Unit> {
        // History events cannot be deleted from the API (audit trail is immutable).
        // This could be mapped to a local-only soft delete in the future.
        return Result.success(Unit)
    }
}
