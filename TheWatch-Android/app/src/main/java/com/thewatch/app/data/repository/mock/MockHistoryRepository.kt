package com.thewatch.app.data.repository.mock

import com.thewatch.app.data.model.HistoryEvent
import com.thewatch.app.data.repository.HistoryRepository
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.flowOf
import java.time.LocalDateTime
import javax.inject.Inject

class MockHistoryRepository @Inject constructor() : HistoryRepository {

    private val historyEvents = mutableListOf<HistoryEvent>()

    init {
        val now = LocalDateTime.now()
        historyEvents.addAll(
            listOf(
                HistoryEvent(
                    id = "event_001",
                    userId = "user_001",
                    eventType = "SOS_ALERT",
                    severity = "High",
                    timestamp = now.minusHours(2),
                    latitude = 40.7128,
                    longitude = -74.0060,
                    location = "Central Park, Manhattan, NY",
                    description = "Emergency medical assistance requested",
                    responderName = "John Martinez",
                    responderId = "responder_001",
                    status = "Resolved",
                    resolution = "Transported to nearest hospital"
                ),
                HistoryEvent(
                    id = "event_002",
                    userId = "user_001",
                    eventType = "CHECK_IN",
                    severity = "Low",
                    timestamp = now.minusHours(4),
                    latitude = 40.7089,
                    longitude = -74.0012,
                    location = "Times Square, Manhattan, NY",
                    description = "Safety check-in completed",
                    responderName = "System",
                    responderId = "system",
                    status = "Completed",
                    resolution = "User confirmed safe"
                ),
                HistoryEvent(
                    id = "event_003",
                    userId = "user_001",
                    eventType = "PANIC_BUTTON",
                    severity = "High",
                    timestamp = now.minusHours(8),
                    latitude = 40.7506,
                    longitude = -73.9972,
                    location = "Washington Square Park, Manhattan, NY",
                    description = "Panic button activated",
                    responderName = "Lisa Chen",
                    responderId = "responder_002",
                    status = "Resolved",
                    resolution = "False alarm, user confirmed safety"
                ),
                HistoryEvent(
                    id = "event_004",
                    userId = "user_001",
                    eventType = "SOS_ALERT",
                    severity = "Medium",
                    timestamp = now.minusHours(12),
                    latitude = 40.6892,
                    longitude = -74.0445,
                    location = "Brooklyn Bridge, Brooklyn, NY",
                    description = "Assistance needed - lost or disoriented",
                    responderName = "David Thompson",
                    responderId = "responder_003",
                    status = "Resolved",
                    resolution = "User guided to safety"
                ),
                HistoryEvent(
                    id = "event_005",
                    userId = "user_001",
                    eventType = "CHECK_IN",
                    severity = "Low",
                    timestamp = now.minusHours(16),
                    latitude = 40.7614,
                    longitude = -73.9776,
                    location = "Grand Central Terminal, Manhattan, NY",
                    description = "Routine safety check-in",
                    responderName = "System",
                    responderId = "system",
                    status = "Completed",
                    resolution = "User confirmed safe"
                )
            )
        )
    }

    override suspend fun recordEvent(event: HistoryEvent): Result<HistoryEvent> {
        delay(600)
        historyEvents.add(event)
        return Result.success(event)
    }

    override suspend fun getHistory(
        userId: String,
        startTime: Long,
        endTime: Long,
        eventTypes: List<String>,
        severities: List<String>,
        statuses: List<String>
    ): Flow<List<HistoryEvent>> {
        delay(500)
        var filtered = historyEvents.filter { it.userId == userId }

        if (eventTypes.isNotEmpty()) {
            filtered = filtered.filter { it.eventType in eventTypes }
        }
        if (severities.isNotEmpty()) {
            filtered = filtered.filter { it.severity in severities }
        }
        if (statuses.isNotEmpty()) {
            filtered = filtered.filter { it.status in statuses }
        }

        return flowOf(filtered.sortedByDescending { it.timestamp })
    }

    override suspend fun getEventDetails(eventId: String): Flow<HistoryEvent?> {
        delay(300)
        return flowOf(historyEvents.find { it.id == eventId })
    }

    override suspend fun deleteHistoryEvent(eventId: String): Result<Unit> {
        delay(500)
        historyEvents.removeIf { it.id == eventId }
        return Result.success(Unit)
    }
}
