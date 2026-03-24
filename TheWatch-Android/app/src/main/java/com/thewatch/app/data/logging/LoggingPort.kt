package com.thewatch.app.data.logging

import kotlinx.coroutines.flow.Flow

/**
 * Port interface for structured logging — the domain contract.
 *
 * Three-tier implementations:
 * - **Mock**: In-memory list + Logcat output. Always-on for development.
 * - **Native**: Room persistence + periodic Firestore sync. Works offline.
 * - **Live**: Direct Firestore writes + Cloud Logging. Production only.
 *
 * All implementations MUST be thread-safe — logging can originate from
 * any coroutine context (Main, IO, Default, or background services).
 */
interface LoggingPort {

    /**
     * Write a structured log entry.
     * Implementations buffer or persist as appropriate for their tier.
     */
    suspend fun write(entry: LogEntry)

    /**
     * Query recent log entries, newest first.
     * @param limit Maximum entries to return
     * @param minLevel Minimum severity filter (inclusive)
     * @param sourceContext Optional filter by source component
     */
    suspend fun query(
        limit: Int = 100,
        minLevel: LogLevel = LogLevel.Verbose,
        sourceContext: String? = null
    ): List<LogEntry>

    /**
     * Query log entries by correlation ID (e.g. all logs for a specific SOS incident).
     */
    suspend fun queryByCorrelation(correlationId: String): List<LogEntry>

    /**
     * Observe log entries in real time. Used by on-device log viewer.
     */
    fun observe(minLevel: LogLevel = LogLevel.Information): Flow<LogEntry>

    /**
     * Flush any buffered entries to persistent storage / remote.
     * Called on app backgrounding and periodic sync.
     */
    suspend fun flush()

    /**
     * Delete entries older than the given epoch millis.
     * Respects retention policy: 7 days local, 90 days Firestore.
     */
    suspend fun prune(olderThanEpochMillis: Long)
}

/**
 * Port for syncing local logs to Firestore.
 * Separated from LoggingPort so mock logging doesn't need sync awareness.
 */
interface LogSyncPort {

    /** Push unsynced local entries to Firestore. Returns count synced. */
    suspend fun syncToFirestore(): Int

    /** Pull recent entries from Firestore (e.g. for cross-device view). */
    suspend fun pullFromFirestore(limit: Int = 200): List<LogEntry>

    /** Check connectivity and Firestore availability. */
    suspend fun isSyncAvailable(): Boolean
}
