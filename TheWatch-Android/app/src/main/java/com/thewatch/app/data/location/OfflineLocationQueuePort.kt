/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         OfflineLocationQueuePort.kt                            │
 * │ Purpose:      Hexagonal port interface for offline location          │
 * │               queuing. Stores location updates in Room when device   │
 * │               is offline. Batch flushes on reconnect via SyncPort.  │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: Room (SyncLogDao), SyncPort                            │
 * │                                                                      │
 * │ Adapter tiers:                                                       │
 * │   - Mock:   In-memory queue. Dev/test.                               │
 * │   - Native: Room-backed queue using SyncLogEntity.                   │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   val queue: OfflineLocationQueuePort = hiltGet()                    │
 * │   // In LocationService when offline:                                │
 * │   queue.enqueue(OfflineLocation(lat, lng, accuracy, timestamp))     │
 * │   // On reconnect:                                                   │
 * │   val flushed = queue.flush()                                        │
 * │   Log.i(TAG, "Flushed $flushed queued locations")                   │
 * │                                                                      │
 * │ NOTE: Location updates are generated every 10-30 seconds by the     │
 * │ foreground LocationService. During extended offline periods          │
 * │ (hours), this can accumulate thousands of entries. The queue         │
 * │ should cap at MAX_QUEUE_SIZE (default 10,000) and drop oldest.      │
 * │ Batch flush uses the SyncPort with action LOCATION_UPDATE.          │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.location

import kotlinx.coroutines.flow.Flow

/**
 * A location reading queued for offline sync.
 */
data class OfflineLocation(
    val latitude: Double,
    val longitude: Double,
    val accuracy: Float = 0f,
    val altitude: Double = 0.0,
    val speed: Float = 0f,
    val bearing: Float = 0f,
    val timestamp: Long = System.currentTimeMillis(),
    val provider: String = "fused",
    /** User ID who generated this location. */
    val userId: String = ""
)

/**
 * Port interface for offline location queuing.
 */
interface OfflineLocationQueuePort {

    /**
     * Enqueue a single location update for later sync.
     *
     * @param location The location data to store.
     */
    suspend fun enqueue(location: OfflineLocation)

    /**
     * Enqueue a batch of location updates.
     */
    suspend fun enqueueBatch(locations: List<OfflineLocation>)

    /**
     * Get the count of queued (pending) location updates.
     */
    suspend fun getQueueSize(): Int

    /**
     * Observe the queue size for UI display.
     */
    fun observeQueueSize(): Flow<Int>

    /**
     * Flush all queued locations to the server via SyncPort.
     *
     * @return Number of locations successfully flushed.
     */
    suspend fun flush(): Int

    /**
     * Get all queued locations (for display or manual export).
     */
    suspend fun getQueuedLocations(): List<OfflineLocation>

    /**
     * Get the most recent queued location.
     */
    suspend fun getLatestQueuedLocation(): OfflineLocation?

    /**
     * Clear the entire queue (e.g., on logout).
     */
    suspend fun clearQueue()

    /**
     * Trim the queue to a maximum size, removing oldest entries.
     *
     * @param maxSize Maximum entries to keep.
     * @return Number of entries removed.
     */
    suspend fun trimQueue(maxSize: Int = 10_000): Int
}
