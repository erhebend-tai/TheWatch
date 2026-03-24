/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         SyncPort.kt                                            │
 * │ Purpose:      Hexagonal port interface for pushing queued offline    │
 * │               operations to the server. Abstracts the transport      │
 * │               layer (REST, gRPC, Firestore) so the sync worker      │
 * │               depends only on this contract.                         │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: SyncLogEntity                                          │
 * │                                                                      │
 * │ Adapter tiers:                                                       │
 * │   - Mock:   Logs to console, always succeeds. Dev/test.              │
 * │   - Native: Pushes to Firestore via Firebase SDK.                    │
 * │   - Live:   REST API with retry + circuit breaker (future).          │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   val syncPort: SyncPort = hiltGet()                                 │
 * │   val result = syncPort.push(syncLogEntry)                           │
 * │   if (result.isSuccess) dao.markCompleted(entry.id)                  │
 * │                                                                      │
 * │ NOTE: Implementations MUST be idempotent — the same entry may be     │
 * │ pushed multiple times if the worker crashes between push and mark.   │
 * │ Server endpoints should use the entry ID for deduplication.          │
 * │ Consider implementing circuit-breaker pattern for native adapter     │
 * │ to avoid hammering a down server during extended outages.            │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.sync

import com.thewatch.app.data.local.SyncLogEntity

/**
 * Result of a sync push operation.
 */
sealed class SyncPushResult {
    /** Entry successfully pushed to server. */
    data class Success(val serverId: String? = null) : SyncPushResult()

    /** Push failed with a retryable error (network timeout, 5xx, etc.). */
    data class RetryableFailure(val message: String, val cause: Throwable? = null) : SyncPushResult()

    /** Push failed with a permanent error (4xx, validation, etc.). No retry. */
    data class PermanentFailure(val message: String, val cause: Throwable? = null) : SyncPushResult()
}

/**
 * Port interface for pushing queued sync operations to the backend.
 *
 * Three-tier implementations:
 * - **Mock**: Always succeeds after simulated delay. Development/testing.
 * - **Native**: Pushes to Firestore or REST API using real credentials.
 * - **Live**: Native + server-side acknowledgment + circuit breaker (future).
 */
interface SyncPort {

    /**
     * Push a single queued operation to the server.
     *
     * @param entry The sync log entry containing action type and JSON payload.
     * @return [SyncPushResult] indicating success or categorized failure.
     */
    suspend fun push(entry: SyncLogEntity): SyncPushResult

    /**
     * Push a batch of entries. Default implementation calls [push] sequentially.
     * Adapters may override for batch-optimized API calls.
     *
     * @param entries List of sync log entries to push.
     * @return Map of entry ID to its push result.
     */
    suspend fun pushBatch(entries: List<SyncLogEntity>): Map<String, SyncPushResult> {
        return entries.associate { it.id to push(it) }
    }

    /**
     * Check whether the sync backend is reachable.
     * Used by the worker to skip flush cycles when the server is known to be down.
     */
    suspend fun isBackendReachable(): Boolean

    /**
     * Report the current circuit breaker state (if implemented).
     * @return true if the circuit is open (backend considered down).
     */
    suspend fun isCircuitOpen(): Boolean = false
}
