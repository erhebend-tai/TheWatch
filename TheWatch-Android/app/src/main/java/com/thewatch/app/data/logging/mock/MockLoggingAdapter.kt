package com.thewatch.app.data.logging.mock

import android.util.Log
import com.thewatch.app.data.logging.LogEntry
import com.thewatch.app.data.logging.LogLevel
import com.thewatch.app.data.logging.LogSyncPort
import com.thewatch.app.data.logging.LoggingPort
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.filter
import java.util.concurrent.ConcurrentLinkedDeque
import javax.inject.Inject
import javax.inject.Singleton

/**
 * Mock logging adapter — Tier 1 (Development).
 *
 * - Stores all entries in an in-memory deque (capped at 5000)
 * - Echoes every entry to Logcat with appropriate level
 * - Emits to a SharedFlow for real-time observation
 * - Never touches disk or network
 * - First-class, permanent — not a "test stub"
 *
 * This is the default adapter wired in AppModule during development.
 */
@Singleton
class MockLoggingAdapter @Inject constructor() : LoggingPort {

    private val entries = ConcurrentLinkedDeque<LogEntry>()
    private val liveFlow = MutableSharedFlow<LogEntry>(extraBufferCapacity = 256)

    companion object {
        private const val TAG = "TheWatch"
        private const val MAX_ENTRIES = 5000
    }

    override suspend fun write(entry: LogEntry) {
        // ── Persist in-memory ────────────────────────────────────
        entries.addFirst(entry)
        while (entries.size > MAX_ENTRIES) entries.removeLast()

        // ── Echo to Logcat ───────────────────────────────────────
        val msg = "[${entry.sourceContext}] ${entry.renderedMessage()}"
        when (entry.level) {
            LogLevel.Verbose     -> Log.v(TAG, msg)
            LogLevel.Debug       -> Log.d(TAG, msg)
            LogLevel.Information -> Log.i(TAG, msg)
            LogLevel.Warning     -> Log.w(TAG, msg)
            LogLevel.Error       -> Log.e(TAG, msg, null)
            LogLevel.Fatal       -> Log.wtf(TAG, msg)
        }
        entry.exception?.let { Log.e(TAG, "  Exception: $it") }
        entry.correlationId?.let { Log.d(TAG, "  CorrelationId: $it") }

        // ── Emit to observers ────────────────────────────────────
        liveFlow.emit(entry)
    }

    override suspend fun query(
        limit: Int,
        minLevel: LogLevel,
        sourceContext: String?
    ): List<LogEntry> {
        return entries.asSequence()
            .filter { it.level >= minLevel }
            .let { seq ->
                if (sourceContext != null) seq.filter { it.sourceContext == sourceContext }
                else seq
            }
            .take(limit)
            .toList()
    }

    override suspend fun queryByCorrelation(correlationId: String): List<LogEntry> {
        return entries.filter { it.correlationId == correlationId }
    }

    override fun observe(minLevel: LogLevel): Flow<LogEntry> {
        return liveFlow.filter { it.level >= minLevel }
    }

    override suspend fun flush() {
        // No-op for mock — everything is already in memory
        Log.d(TAG, "[MockLogging] Flush called — ${entries.size} entries in buffer")
    }

    override suspend fun prune(olderThanEpochMillis: Long) {
        val before = entries.size
        entries.removeIf { it.timestamp.toEpochMilli() < olderThanEpochMillis }
        Log.d(TAG, "[MockLogging] Pruned ${before - entries.size} entries")
    }

    /** Expose all entries for testing / debug UI. */
    fun allEntries(): List<LogEntry> = entries.toList()

    /** Clear all entries (useful for test reset). */
    fun clear() = entries.clear()
}

/**
 * Mock sync adapter — Tier 1 no-op.
 * Logs sync attempts to Logcat without touching Firestore.
 */
@Singleton
class MockLogSyncAdapter @Inject constructor() : LogSyncPort {

    override suspend fun syncToFirestore(): Int {
        Log.i("TheWatch", "[MockLogSync] syncToFirestore called — mock returns 0")
        return 0
    }

    override suspend fun pullFromFirestore(limit: Int): List<LogEntry> {
        Log.i("TheWatch", "[MockLogSync] pullFromFirestore($limit) — mock returns empty")
        return emptyList()
    }

    override suspend fun isSyncAvailable(): Boolean {
        Log.d("TheWatch", "[MockLogSync] isSyncAvailable — mock returns false")
        return false
    }
}
