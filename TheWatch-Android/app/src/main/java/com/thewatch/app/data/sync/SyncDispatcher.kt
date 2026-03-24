/**
 * +----------------------------------------------------------------------+
 * | WRITE-AHEAD LOG                                                       |
 * +----------------------------------------------------------------------+
 * | File:         SyncDispatcher.kt                                       |
 * | Purpose:      Maps SyncEntityType to the appropriate Firestore        |
 * |               collection and serialization strategy. Handles batch    |
 * |               writes, conflict resolution (last-write-wins with       |
 * |               server timestamp), and circuit breaker pattern.         |
 * | Created:      2026-03-24                                              |
 * | Author:       Claude                                                  |
 * | Dependencies: Firebase Firestore SDK, SyncTaskEntity                  |
 * |                                                                       |
 * | Collection mapping:                                                   |
 * |   SOS_EVENT       -> "sos_events"                                     |
 * |   VOLUNTEER       -> "volunteers"                                     |
 * |   CONTACT         -> "contacts"                                       |
 * |   DEVICE          -> "devices"                                        |
 * |   EVIDENCE        -> "evidence"                                       |
 * |   LOCATION        -> "locations"                                      |
 * |   CHECK_IN        -> "check_ins"                                      |
 * |   SITREP          -> "sitreps"                                        |
 * |   PROFILE         -> "profiles"                                       |
 * |   HEALTH_DATA     -> "health_data"                                    |
 * |   GEOFENCE        -> "geofences"                                      |
 * |   BLE_RELAY       -> "ble_relays"                                     |
 * |   LOG_ENTRY       -> "logs"                                           |
 * |   GUARDIAN_CONSENT -> "guardian_consents"                              |
 * |   ESCALATION      -> "escalations"                                    |
 * |                                                                       |
 * | Usage example:                                                        |
 * |   val result = syncDispatcher.dispatch(task)                          |
 * |   when (result) {                                                     |
 * |       is SyncDispatchResult.Success -> // task.entityId written       |
 * |       is SyncDispatchResult.RetryableFailure -> // retry later        |
 * |       is SyncDispatchResult.PermanentFailure -> // dead letter        |
 * |   }                                                                   |
 * |                                                                       |
 * | NOTE: This is the Mock (Tier 1) implementation. The Native (Tier 2)   |
 * | version injects FirebaseFirestore and performs real writes.            |
 * | Circuit breaker: after 3 consecutive failures, the circuit opens      |
 * | for 60 seconds. During this time, all dispatches return               |
 * | RetryableFailure immediately without hitting the network.             |
 * +----------------------------------------------------------------------+
 */
package com.thewatch.app.data.sync

import android.util.Log
import kotlinx.coroutines.delay
import java.util.concurrent.atomic.AtomicInteger
import java.util.concurrent.atomic.AtomicLong
import javax.inject.Inject
import javax.inject.Singleton

/**
 * Result of dispatching a sync task to the backend.
 */
sealed class SyncDispatchResult {
    data class Success(val serverId: String? = null) : SyncDispatchResult()
    data class RetryableFailure(val message: String, val cause: Throwable? = null) : SyncDispatchResult()
    data class PermanentFailure(val message: String, val cause: Throwable? = null) : SyncDispatchResult()
}

/**
 * Port interface for dispatching sync tasks. Allows Mock/Native/Live tier switching.
 */
interface SyncDispatchPort {
    suspend fun dispatch(task: SyncTaskEntity): SyncDispatchResult
    suspend fun dispatchBatch(tasks: List<SyncTaskEntity>): Map<String, SyncDispatchResult>
    suspend fun isBackendReachable(): Boolean
    suspend fun isCircuitOpen(): Boolean
}

/**
 * Mock (Tier 1) dispatcher. Simulates Firestore writes with configurable delay.
 * For Tier 2 (Native), replace with FirestoreSyncDispatcher that uses:
 *   - FirebaseFirestore.getInstance().collection(collectionName).document(entityId).set(data)
 *   - FieldValue.serverTimestamp() for conflict resolution
 *   - WriteBatch for batch operations
 */
@Singleton
class SyncDispatcher @Inject constructor() : SyncDispatchPort {

    companion object {
        private const val TAG = "TheWatch.SyncDispatch"
        private const val SIMULATED_DELAY_MS = 150L

        // Circuit breaker config
        private const val CIRCUIT_FAILURE_THRESHOLD = 3
        private const val CIRCUIT_OPEN_DURATION_MS = 60_000L

        /**
         * Entity type to Firestore collection name mapping.
         */
        val COLLECTION_MAP: Map<SyncEntityType, String> = mapOf(
            SyncEntityType.SOS_EVENT to "sos_events",
            SyncEntityType.VOLUNTEER to "volunteers",
            SyncEntityType.CONTACT to "contacts",
            SyncEntityType.DEVICE to "devices",
            SyncEntityType.EVIDENCE to "evidence",
            SyncEntityType.LOCATION to "locations",
            SyncEntityType.CHECK_IN to "check_ins",
            SyncEntityType.SITREP to "sitreps",
            SyncEntityType.PROFILE to "profiles",
            SyncEntityType.HEALTH_DATA to "health_data",
            SyncEntityType.GEOFENCE to "geofences",
            SyncEntityType.BLE_RELAY to "ble_relays",
            SyncEntityType.LOG_ENTRY to "logs",
            SyncEntityType.GUARDIAN_CONSENT to "guardian_consents",
            SyncEntityType.ESCALATION to "escalations"
        )
    }

    // ── Circuit breaker state ────────────────────────────────────────
    private val consecutiveFailures = AtomicInteger(0)
    private val circuitOpenedAt = AtomicLong(0)

    // ── Test simulation controls ─────────────────────────────────────
    @Volatile
    private var simulateFailure: Boolean = false

    @Volatile
    private var simulateBackendDown: Boolean = false

    fun setSimulateFailure(fail: Boolean) { simulateFailure = fail }
    fun setSimulateBackendDown(down: Boolean) { simulateBackendDown = down }

    /**
     * Resolve the Firestore collection name for an entity type.
     */
    fun resolveCollection(entityType: SyncEntityType): String {
        return COLLECTION_MAP[entityType]
            ?: throw IllegalArgumentException("No collection mapped for $entityType")
    }

    /**
     * Dispatch a single sync task to the backend (mock implementation).
     *
     * Native (Tier 2) implementation would:
     * 1. Parse task.payload as Map<String, Any> via kotlinx.serialization
     * 2. Add "serverTimestamp" field via FieldValue.serverTimestamp()
     * 3. Add "idempotencyKey" field for dedup
     * 4. Route to collection: firestore.collection(resolveCollection(task.entityType))
     * 5. Execute: .document(task.entityId).set(data, SetOptions.merge()) for CREATE/UPDATE
     *    or .document(task.entityId).delete() for DELETE
     * 6. Return Success with document ID, or categorize errors (UNAVAILABLE/DEADLINE_EXCEEDED
     *    as Retryable, PERMISSION_DENIED/NOT_FOUND as Permanent)
     */
    override suspend fun dispatch(task: SyncTaskEntity): SyncDispatchResult {
        // Circuit breaker check
        if (isCircuitOpen()) {
            return SyncDispatchResult.RetryableFailure("Circuit breaker open — backend assumed down")
        }

        val collection = resolveCollection(task.entityType)
        Log.d(TAG, "dispatch(${task.id}) -> $collection/${task.entityId} [${task.action}]")

        delay(SIMULATED_DELAY_MS)

        return if (simulateFailure) {
            val failures = consecutiveFailures.incrementAndGet()
            if (failures >= CIRCUIT_FAILURE_THRESHOLD) {
                circuitOpenedAt.set(System.currentTimeMillis())
                Log.w(TAG, "Circuit breaker OPEN after $failures consecutive failures")
            }
            SyncDispatchResult.RetryableFailure("Mock: simulated dispatch failure")
        } else {
            consecutiveFailures.set(0) // Reset on success
            Log.i(TAG, "dispatch(${task.id}) -> success (mock, collection=$collection)")
            SyncDispatchResult.Success(serverId = "mock-$collection-${task.entityId}")
        }
    }

    /**
     * Dispatch a batch of tasks. Mock implementation calls dispatch() sequentially.
     *
     * Native (Tier 2) would use Firestore WriteBatch:
     *   val batch = firestore.batch()
     *   tasks.forEach { task ->
     *       val ref = firestore.collection(resolveCollection(task.entityType)).document(task.entityId)
     *       when (task.action) {
     *           CREATE, UPDATE -> batch.set(ref, parsePayload(task), SetOptions.merge())
     *           DELETE -> batch.delete(ref)
     *       }
     *   }
     *   batch.commit().await()
     *
     * Firestore batch limit is 500 operations. Split accordingly.
     */
    override suspend fun dispatchBatch(tasks: List<SyncTaskEntity>): Map<String, SyncDispatchResult> {
        return tasks.associate { it.id to dispatch(it) }
    }

    override suspend fun isBackendReachable(): Boolean {
        return !simulateBackendDown && !isCircuitOpen()
    }

    override suspend fun isCircuitOpen(): Boolean {
        val openedAt = circuitOpenedAt.get()
        if (openedAt == 0L) return false

        val elapsed = System.currentTimeMillis() - openedAt
        if (elapsed > CIRCUIT_OPEN_DURATION_MS) {
            // Half-open: reset and allow next attempt
            circuitOpenedAt.set(0)
            consecutiveFailures.set(0)
            Log.i(TAG, "Circuit breaker HALF-OPEN -> CLOSED (cooldown elapsed)")
            return false
        }
        return true
    }

    /**
     * Reset circuit breaker state. Used in testing and after manual connectivity confirmation.
     */
    fun resetCircuitBreaker() {
        consecutiveFailures.set(0)
        circuitOpenedAt.set(0)
        Log.i(TAG, "Circuit breaker manually reset")
    }
}
