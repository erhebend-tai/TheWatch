/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         RoomLocationQueueAdapter.kt                            │
 * │ Purpose:      Room-backed adapter for OfflineLocationQueuePort.      │
 * │               Stores location updates as SyncLogEntity entries       │
 * │               with action=LOCATION_UPDATE. Flushes via SyncPort.    │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: SyncLogDao, SyncPort, kotlinx.serialization           │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   @Provides fun provideLocationQueue(                                │
 * │       adapter: RoomLocationQueueAdapter                              │
 * │   ): OfflineLocationQueuePort = adapter                              │
 * │                                                                      │
 * │ NOTE: Locations are serialized to JSON and stored in                  │
 * │ SyncLogEntity.payload. This reuses the existing sync infrastructure │
 * │ rather than creating a separate Room table.                          │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.location.native

import android.util.Log
import com.thewatch.app.data.local.SyncAction
import com.thewatch.app.data.local.SyncLogDao
import com.thewatch.app.data.local.SyncLogEntity
import com.thewatch.app.data.local.SyncStatus
import com.thewatch.app.data.location.OfflineLocation
import com.thewatch.app.data.location.OfflineLocationQueuePort
import com.thewatch.app.data.sync.SyncPort
import com.thewatch.app.data.sync.SyncPushResult
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map
import kotlinx.serialization.Serializable
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import java.util.UUID
import javax.inject.Inject
import javax.inject.Singleton

@Serializable
private data class LocationPayload(
    val latitude: Double,
    val longitude: Double,
    val accuracy: Float,
    val altitude: Double,
    val speed: Float,
    val bearing: Float,
    val timestamp: Long,
    val provider: String,
    val userId: String
)

@Singleton
class RoomLocationQueueAdapter @Inject constructor(
    private val syncLogDao: SyncLogDao,
    private val syncPort: SyncPort
) : OfflineLocationQueuePort {

    companion object {
        private const val TAG = "TheWatch.LocationQueue"
        private const val MAX_QUEUE_SIZE = 10_000
        private val json = Json { ignoreUnknownKeys = true }
    }

    override suspend fun enqueue(location: OfflineLocation) {
        val payload = json.encodeToString(LocationPayload(
            latitude = location.latitude,
            longitude = location.longitude,
            accuracy = location.accuracy,
            altitude = location.altitude,
            speed = location.speed,
            bearing = location.bearing,
            timestamp = location.timestamp,
            provider = location.provider,
            userId = location.userId
        ))

        val entry = SyncLogEntity(
            id = UUID.randomUUID().toString(),
            userId = location.userId,
            action = SyncAction.LOCATION_UPDATE.name,
            payload = payload,
            status = SyncStatus.PENDING.name,
            priority = 3, // Medium priority
            createdAt = location.timestamp,
            timestamp = location.timestamp,
            eventType = SyncAction.LOCATION_UPDATE.name
        )

        syncLogDao.insertLog(entry)

        // Auto-trim if over limit
        val size = getQueueSize()
        if (size > MAX_QUEUE_SIZE) {
            trimQueue(MAX_QUEUE_SIZE)
        }
    }

    override suspend fun enqueueBatch(locations: List<OfflineLocation>) {
        locations.forEach { enqueue(it) }
    }

    override suspend fun getQueueSize(): Int {
        return syncLogDao.getPendingByAction(SyncAction.LOCATION_UPDATE.name).size
    }

    override fun observeQueueSize(): Flow<Int> {
        return syncLogDao.observePendingCount().map { totalPending ->
            // Approximate — in production, would filter by action in the query
            syncLogDao.getPendingByAction(SyncAction.LOCATION_UPDATE.name).size
        }
    }

    override suspend fun flush(): Int {
        val pending = syncLogDao.getPendingByAction(SyncAction.LOCATION_UPDATE.name)
        if (pending.isEmpty()) return 0

        Log.i(TAG, "Flushing ${pending.size} queued locations")
        var flushed = 0

        // Batch in groups of 100 for efficiency
        pending.chunked(100).forEach { batch ->
            for (entry in batch) {
                syncLogDao.markInProgress(entry.id)
                when (val result = syncPort.push(entry)) {
                    is SyncPushResult.Success -> {
                        syncLogDao.markCompleted(entry.id)
                        flushed++
                    }
                    is SyncPushResult.RetryableFailure -> {
                        syncLogDao.markFailed(entry.id, result.message, entry.retryCount + 1)
                    }
                    is SyncPushResult.PermanentFailure -> {
                        syncLogDao.markFailed(entry.id, result.message, 5)
                    }
                }
            }
        }

        Log.i(TAG, "Flushed $flushed / ${pending.size} locations")
        return flushed
    }

    override suspend fun getQueuedLocations(): List<OfflineLocation> {
        return syncLogDao.getPendingByAction(SyncAction.LOCATION_UPDATE.name).mapNotNull { entry ->
            try {
                val payload = json.decodeFromString<LocationPayload>(entry.payload)
                OfflineLocation(
                    latitude = payload.latitude,
                    longitude = payload.longitude,
                    accuracy = payload.accuracy,
                    altitude = payload.altitude,
                    speed = payload.speed,
                    bearing = payload.bearing,
                    timestamp = payload.timestamp,
                    provider = payload.provider,
                    userId = payload.userId
                )
            } catch (e: Exception) {
                Log.e(TAG, "Failed to deserialize location payload: ${entry.id}", e)
                null
            }
        }
    }

    override suspend fun getLatestQueuedLocation(): OfflineLocation? {
        return getQueuedLocations().maxByOrNull { it.timestamp }
    }

    override suspend fun clearQueue() {
        val entries = syncLogDao.getByAction(SyncAction.LOCATION_UPDATE.name)
        entries.forEach { syncLogDao.deleteLog(it) }
        Log.i(TAG, "Location queue cleared (${entries.size} entries)")
    }

    override suspend fun trimQueue(maxSize: Int): Int {
        val entries = syncLogDao.getPendingByAction(SyncAction.LOCATION_UPDATE.name)
        if (entries.size <= maxSize) return 0

        val toRemove = entries.sortedBy { it.createdAt }.take(entries.size - maxSize)
        toRemove.forEach { syncLogDao.deleteLog(it) }
        Log.i(TAG, "Trimmed ${toRemove.size} oldest location entries")
        return toRemove.size
    }
}
