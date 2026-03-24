package com.thewatch.app.data.logging.native

import android.util.Log
import com.thewatch.app.data.logging.LogEntry
import com.thewatch.app.data.logging.LogLevel
import com.thewatch.app.data.logging.LoggingPort
import com.thewatch.app.data.logging.local.LogEntryDao
import com.thewatch.app.data.logging.local.LogEntryEntity
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.filter
import javax.inject.Inject
import javax.inject.Singleton

/**
 * Native logging adapter — Tier 2 (On-Device + Offline).
 *
 * - Persists every log entry to Room (local SQLite)
 * - Echoes Warning+ to Logcat for crash diagnostics
 * - Emits to a SharedFlow for real-time observation
 * - Works fully offline — no network dependency
 * - Paired with [NativeLogSyncAdapter] for Firestore sync
 *
 * Room handles all threading via suspend functions on the IO dispatcher.
 */
@Singleton
class NativeLoggingAdapter @Inject constructor(
    private val dao: LogEntryDao
) : LoggingPort {

    private val liveFlow = MutableSharedFlow<LogEntry>(extraBufferCapacity = 256)

    companion object {
        private const val TAG = "TheWatch"
    }

    override suspend fun write(entry: LogEntry) {
        // Persist to Room
        dao.insert(LogEntryEntity.fromDomain(entry))

        // Echo Warning+ to Logcat for crash report correlation
        if (entry.level >= LogLevel.Warning) {
            val msg = "[${entry.sourceContext}] ${entry.renderedMessage()}"
            when (entry.level) {
                LogLevel.Warning -> Log.w(TAG, msg)
                LogLevel.Error   -> Log.e(TAG, msg)
                LogLevel.Fatal   -> Log.wtf(TAG, msg)
                else -> { /* unreachable */ }
            }
        }

        // Emit to observers
        liveFlow.emit(entry)
    }

    override suspend fun query(
        limit: Int,
        minLevel: LogLevel,
        sourceContext: String?
    ): List<LogEntry> {
        val entities = if (sourceContext != null) {
            dao.getRecentBySource(limit, minLevel.ordinal, sourceContext)
        } else {
            dao.getRecent(limit, minLevel.ordinal)
        }
        return entities.map { it.toDomain() }
    }

    override suspend fun queryByCorrelation(correlationId: String): List<LogEntry> {
        return dao.getByCorrelation(correlationId).map { it.toDomain() }
    }

    override fun observe(minLevel: LogLevel): Flow<LogEntry> {
        return liveFlow.filter { it.level >= minLevel }
    }

    override suspend fun flush() {
        // Room auto-persists, but log the buffer state for diagnostics
        val total = dao.count()
        val unsynced = dao.unsyncedCount()
        Log.d(TAG, "[NativeLogging] Flush — $total total, $unsynced unsynced")
    }

    override suspend fun prune(olderThanEpochMillis: Long) {
        val deleted = dao.deleteOlderThan(olderThanEpochMillis)
        Log.d(TAG, "[NativeLogging] Pruned $deleted entries older than $olderThanEpochMillis")
    }
}
