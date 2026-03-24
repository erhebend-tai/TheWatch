package com.thewatch.app.data.mock

import com.thewatch.app.data.model.HistoryEvent
import com.thewatch.app.data.repository.HistoryRepository
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.flow
import javax.inject.Inject

class MockHistoryRepository @Inject constructor() : HistoryRepository {

    private val mockHistoryEvents = listOf(
        HistoryEvent(
            id = "event_001",
            userId = "user_001",
            type = "ALERT_ACTIVATED",
            severity = "CRITICAL",
            status = "RESOLVED",
            timestamp = System.currentTimeMillis() - 86400000,
            latitude = 37.7749,
            longitude = -122.4194,
            triggerSource = "USER",
            confidenceScore = 1.0f,
            description = "Fall detected at home",
            escalationCount = 1
        ),
        HistoryEvent(
            id = "event_002",
            userId = "user_001",
            type = "CONTACT_NOTIFIED",
            severity = "HIGH",
            status = "COMPLETED",
            timestamp = System.currentTimeMillis() - 86400000 + 60000,
            latitude = 37.7749,
            longitude = -122.4194,
            triggerSource = "SYSTEM",
            confidenceScore = null,
            description = "Emergency contact Maria Rivera notified",
            escalationCount = 0
        ),
        HistoryEvent(
            id = "event_003",
            userId = "user_001",
            type = "RESPONDER_ASSIGNED",
            severity = "CRITICAL",
            status = "COMPLETED",
            timestamp = System.currentTimeMillis() - 86400000 + 120000,
            latitude = 37.7749,
            longitude = -122.4194,
            triggerSource = "SYSTEM",
            confidenceScore = null,
            description = "EMT Michael Chen assigned to response",
            escalationCount = 1
        ),
        HistoryEvent(
            id = "event_004",
            userId = "user_001",
            type = "ALERT_ACTIVATED",
            severity = "HIGH",
            status = "RESOLVED",
            timestamp = System.currentTimeMillis() - 172800000,
            latitude = 37.7750,
            longitude = -122.4195,
            triggerSource = "IMPLICIT_DETECTION",
            confidenceScore = 0.87f,
            description = "Potential fall detected by wearable",
            escalationCount = 0
        ),
        HistoryEvent(
            id = "event_005",
            userId = "user_001",
            type = "ALERT_ACTIVATED",
            severity = "MEDIUM",
            status = "CANCELLED",
            timestamp = System.currentTimeMillis() - 259200000,
            latitude = 37.7751,
            longitude = -122.4196,
            triggerSource = "USER",
            confidenceScore = 1.0f,
            description = "False alarm - user cancelled alert",
            escalationCount = 0
        )
    )

    override suspend fun recordEvent(event: HistoryEvent): Result<HistoryEvent> {
        delay(600)
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
        delay(800)
        var filtered = mockHistoryEvents.filter { it.timestamp in startTime..endTime }

        if (eventTypes.isNotEmpty()) {
            filtered = filtered.filter { it.type in eventTypes }
        }
        if (severities.isNotEmpty()) {
            filtered = filtered.filter { it.severity in severities }
        }
        if (statuses.isNotEmpty()) {
            filtered = filtered.filter { it.status in statuses }
        }

        emit(filtered)
    }

    override suspend fun getEventDetails(eventId: String): Flow<HistoryEvent?> = flow {
        delay(500)
        emit(mockHistoryEvents.find { it.id == eventId })
    }

    override suspend fun deleteHistoryEvent(eventId: String): Result<Unit> {
        delay(600)
        return Result.success(Unit)
    }
}
