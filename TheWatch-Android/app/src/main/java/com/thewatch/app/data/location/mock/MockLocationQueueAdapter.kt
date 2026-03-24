/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         MockLocationQueueAdapter.kt                            │
 * │ Purpose:      Mock (Tier 1) adapter for OfflineLocationQueuePort.    │
 * │               In-memory queue for development and testing.           │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: OfflineLocationQueuePort                               │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   @Provides fun provideLocationQueue(                                │
 * │       mock: MockLocationQueueAdapter                                 │
 * │   ): OfflineLocationQueuePort = mock                                 │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.location.mock

import android.util.Log
import com.thewatch.app.data.location.OfflineLocation
import com.thewatch.app.data.location.OfflineLocationQueuePort
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import javax.inject.Inject
import javax.inject.Singleton

@Singleton
class MockLocationQueueAdapter @Inject constructor() : OfflineLocationQueuePort {

    companion object {
        private const val TAG = "TheWatch.MockLocQueue"
    }

    private val queue = mutableListOf<OfflineLocation>()
    private val queueSizeFlow = MutableStateFlow(0)

    override suspend fun enqueue(location: OfflineLocation) {
        queue.add(location)
        queueSizeFlow.value = queue.size
        Log.d(TAG, "Enqueued location: ${location.latitude},${location.longitude} (queue=${queue.size})")
    }

    override suspend fun enqueueBatch(locations: List<OfflineLocation>) {
        queue.addAll(locations)
        queueSizeFlow.value = queue.size
    }

    override suspend fun getQueueSize(): Int = queue.size

    override fun observeQueueSize(): Flow<Int> = queueSizeFlow

    override suspend fun flush(): Int {
        val count = queue.size
        queue.clear()
        queueSizeFlow.value = 0
        Log.i(TAG, "Flushed $count mock locations")
        return count
    }

    override suspend fun getQueuedLocations(): List<OfflineLocation> = queue.toList()

    override suspend fun getLatestQueuedLocation(): OfflineLocation? = queue.maxByOrNull { it.timestamp }

    override suspend fun clearQueue() {
        queue.clear()
        queueSizeFlow.value = 0
    }

    override suspend fun trimQueue(maxSize: Int): Int {
        if (queue.size <= maxSize) return 0
        val removed = queue.size - maxSize
        val sorted = queue.sortedBy { it.timestamp }
        queue.clear()
        queue.addAll(sorted.takeLast(maxSize))
        queueSizeFlow.value = queue.size
        return removed
    }
}
