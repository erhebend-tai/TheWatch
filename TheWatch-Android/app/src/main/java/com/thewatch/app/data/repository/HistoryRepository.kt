package com.thewatch.app.data.repository

import com.thewatch.app.data.model.HistoryEvent
import kotlinx.coroutines.flow.Flow

interface HistoryRepository {
    suspend fun recordEvent(event: HistoryEvent): Result<HistoryEvent>
    suspend fun getHistory(
        userId: String,
        startTime: Long,
        endTime: Long,
        eventTypes: List<String> = emptyList(),
        severities: List<String> = emptyList(),
        statuses: List<String> = emptyList()
    ): Flow<List<HistoryEvent>>
    suspend fun getEventDetails(eventId: String): Flow<HistoryEvent?>
    suspend fun deleteHistoryEvent(eventId: String): Result<Unit>
}
