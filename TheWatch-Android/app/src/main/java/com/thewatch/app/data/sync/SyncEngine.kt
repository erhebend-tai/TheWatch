/**
 * +----------------------------------------------------------------------+
 * | WRITE-AHEAD LOG                                                       |
 * +----------------------------------------------------------------------+
 * | File:         SyncEngine.kt                                           |
 * | Purpose:      Singleton coordinator for all offline-first sync        |
 * |               operations across TheWatch. Manages a priority queue    |
 * |               of SyncTask items persisted in Room, dispatches them    |
 * |               through SyncDispatcher when connectivity is available,  |
 * |               and handles task coalescing for duplicate entity updates.|
 * | Created:      2026-03-24                                              |
 * | Author:       Claude                                                  |
 * | Dependencies: SyncTaskDao, SyncDispatcher, ConnectivityMonitor,       |
 * |               WatchLogger                                             |
 * |                                                                       |
 * | Usage example:                                                        |
 * |   // Enqueue from anywhere in the app:                                |
 * |   syncEngine.enqueue(                                                 |
 * |       entityType = SyncEntityType.SOS_EVENT,                          |
 * |       entityId = "sos-abc",                                           |
 * |       action = SyncTaskAction.CREATE,                                 |
 * |       payload = sosJson,                                              |
 * |       priority = SyncPriority.CRITICAL                                |
 * |   )                                                                   |
 * |                                                                       |
 * |   // The engine will:                                                 |
 * |   // 1. Persist to Room immediately (survives process death)          |
 * |   // 2. If online, trigger immediate flush via SyncWorker             |
 * |   // 3. If offline, ConnectivityMonitor triggers flush on reconnect   |
 * |                                                                       |
 * | Coalescing: If an UPDATE for the same entityType+entityId is already  |
 * | QUEUED, the engine replaces the payload (last-write-wins locally)     |
 * | rather than creating a duplicate task.                                |
 * |                                                                       |
 * | NOTE: The engine does NOT hold tasks in memory. Room is the single    |
 * | source of truth. This ensures no data loss on process death.          |
 * | Consider adding metrics emission (enqueue rate, flush latency,        |
 * | dead-letter rate) for operational dashboards.                         |
 * +----------------------------------------------------------------------+
 */
package com.thewatch.app.data.sync

import android.content.Context
import android.util.Log
import com.thewatch.app.data.logging.WatchLogger
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.launch
import java.util.UUID
import java.util.concurrent.TimeUnit
import javax.inject.Inject
import javax.inject.Singleton
import kotlin.math.min
import kotlin.math.pow

/**
 * Callback interface for sync providers that want to register with the engine.
 * The existing logging sync (OfflineSyncWorker/NativeLogSyncAdapter) registers
 * through this interface so it participates in the generalized engine without
 * breaking its existing behavior.
 */
interface SyncProvider {
    /** The entity type this provider handles. */
    val entityType: SyncEntityType

    /**
     * Called by the engine when a task for this provider's entity type is ready
     * to be dispatched. Providers may perform custom serialization, validation,
     * or side effects before the standard Firestore push.
     *
     * Return true to let the engine proceed with standard dispatch.
     * Return false if the provider handled the push itself.
     */
    suspend fun onBeforeDispatch(task: SyncTaskEntity): Boolean

    /**
     * Called after successful dispatch. Providers can update local state
     * (e.g., mark a log entry as synced in the logging subsystem's own table).
     */
    suspend fun onAfterDispatch(task: SyncTaskEntity, serverId: String?)
}

@Singleton
class SyncEngine @Inject constructor(
    @ApplicationContext private val context: Context,
    private val syncTaskDao: SyncTaskDao,
    private val syncDispatcher: SyncDispatcher,
    private val connectivityMonitor: ConnectivityMonitor,
    private val logger: WatchLogger
) {
    companion object {
        private const val TAG = "TheWatch.SyncEngine"
        private const val MAX_RETRIES_DEFAULT = 5
        private const val BASE_BACKOFF_SECONDS = 30L
        private const val MAX_BACKOFF_SECONDS = 900L // 15 min
        private const val BATCH_SIZE = 50
        private const val PRUNE_COMPLETED_HOURS = 24L
        private const val PRUNE_DEAD_LETTER_DAYS = 7L
    }

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    /** Registered sync providers, keyed by entity type. */
    private val providers = mutableMapOf<SyncEntityType, SyncProvider>()

    /**
     * Register a sync provider for a specific entity type.
     * This allows existing subsystems (logging, evidence, etc.) to plug
     * into the generalized engine with custom pre/post-dispatch hooks.
     */
    fun registerProvider(provider: SyncProvider) {
        providers[provider.entityType] = provider
        Log.i(TAG, "Registered sync provider for ${provider.entityType}")
    }

    /**
     * Enqueue a sync task. Persists immediately to Room.
     * If online and priority is CRITICAL or HIGH, triggers an expedited flush.
     *
     * @param entityType What kind of data
     * @param entityId Unique ID of the entity
     * @param action Create/Update/Delete
     * @param payload JSON-serialized data
     * @param priority See [SyncPriority]
     * @param userId Owner of this data
     * @param idempotencyKey Custom dedup key, defaults to task ID
     * @return The generated task ID
     */
    suspend fun enqueue(
        entityType: SyncEntityType,
        entityId: String,
        action: SyncTaskAction,
        payload: String,
        priority: Int = SyncPriority.NORMAL,
        userId: String = "",
        idempotencyKey: String? = null
    ): String {
        // Coalesce: if an UPDATE for the same entity is already queued, replace payload
        if (action == SyncTaskAction.UPDATE) {
            val existing = syncTaskDao.findPendingForEntity(entityType, entityId)
            if (existing != null && existing.action == SyncTaskAction.UPDATE) {
                val updated = existing.copy(
                    payload = payload,
                    priority = minOf(existing.priority, priority), // Escalate priority
                    createdAt = System.currentTimeMillis()
                )
                syncTaskDao.update(updated)
                Log.d(TAG, "Coalesced UPDATE for $entityType/$entityId (task=${existing.id})")
                triggerFlushIfNeeded(priority)
                return existing.id
            }
        }

        val taskId = UUID.randomUUID().toString()
        val task = SyncTaskEntity(
            id = taskId,
            entityType = entityType,
            entityId = entityId,
            action = action,
            payload = payload,
            priority = priority,
            userId = userId,
            idempotencyKey = idempotencyKey ?: taskId
        )

        syncTaskDao.insert(task)
        Log.i(TAG, "Enqueued $action for $entityType/$entityId (priority=$priority, id=$taskId)")

        triggerFlushIfNeeded(priority)
        return taskId
    }

    /**
     * Convenience: enqueue a batch of tasks atomically.
     */
    suspend fun enqueueBatch(tasks: List<SyncTaskEntity>) {
        syncTaskDao.insertAll(tasks)
        Log.i(TAG, "Enqueued batch of ${tasks.size} tasks")
        val highestPriority = tasks.minOfOrNull { it.priority } ?: SyncPriority.NORMAL
        triggerFlushIfNeeded(highestPriority)
    }

    /**
     * Main flush cycle. Called by SyncWorker on schedule and on connectivity restored.
     * Processes all pending tasks in priority order, dispatching through SyncDispatcher.
     *
     * @return Pair of (successCount, failCount)
     */
    suspend fun flush(): Pair<Int, Int> {
        Log.i(TAG, "flush() started")

        // 1. Recover stuck tasks from a previous crash
        syncTaskDao.resetStuckTasks()

        // 2. Check backend reachability
        if (!syncDispatcher.isBackendReachable()) {
            Log.w(TAG, "Backend not reachable — skipping flush")
            return Pair(0, 0)
        }

        // 3. Fetch all pending tasks ordered by priority
        val tasks = syncTaskDao.getPendingByPriority(MAX_RETRIES_DEFAULT)
        if (tasks.isEmpty()) {
            Log.i(TAG, "No pending tasks")
            return Pair(0, 0)
        }

        Log.i(TAG, "Processing ${tasks.size} pending tasks")

        var successCount = 0
        var failCount = 0

        // 4. Process in batches
        for (batch in tasks.chunked(BATCH_SIZE)) {
            for (task in batch) {
                // Per-task exponential backoff
                if (shouldDefer(task)) {
                    Log.d(TAG, "Deferring task ${task.id} (retry ${task.retryCount})")
                    continue
                }

                syncTaskDao.markInProgress(task.id)

                // Let registered provider run pre-dispatch hook
                val provider = providers[task.entityType]
                val shouldContinueDispatch = try {
                    provider?.onBeforeDispatch(task) ?: true
                } catch (e: Exception) {
                    Log.w(TAG, "Provider pre-dispatch failed for ${task.id}", e)
                    true // Continue with standard dispatch on provider error
                }

                if (!shouldContinueDispatch) {
                    // Provider handled the push itself
                    syncTaskDao.markCompleted(task.id)
                    successCount++
                    continue
                }

                // Dispatch through the generalized dispatcher
                when (val result = syncDispatcher.dispatch(task)) {
                    is SyncDispatchResult.Success -> {
                        syncTaskDao.markCompleted(task.id)
                        successCount++

                        // Notify provider of success
                        try {
                            provider?.onAfterDispatch(task, result.serverId)
                        } catch (e: Exception) {
                            Log.w(TAG, "Provider post-dispatch failed for ${task.id}", e)
                        }

                        Log.d(TAG, "Synced ${task.entityType}/${task.entityId} -> ${result.serverId}")
                    }

                    is SyncDispatchResult.RetryableFailure -> {
                        val newRetryCount = task.retryCount + 1
                        if (newRetryCount >= task.maxRetries) {
                            syncTaskDao.markDeadLetter(task.id)
                            Log.e(TAG, "Task ${task.id} moved to dead-letter after $newRetryCount retries: ${result.message}")
                        } else {
                            syncTaskDao.markFailed(task.id, result.message, newRetryCount)
                            Log.w(TAG, "Retryable failure for ${task.id}: ${result.message}")
                        }
                        failCount++

                        // If dispatcher signals circuit open, stop batch
                        if (syncDispatcher.isCircuitOpen()) {
                            Log.w(TAG, "Circuit breaker open — stopping flush")
                            return Pair(successCount, failCount)
                        }
                    }

                    is SyncDispatchResult.PermanentFailure -> {
                        syncTaskDao.markDeadLetter(task.id)
                        failCount++
                        Log.e(TAG, "Permanent failure for ${task.id}: ${result.message}")
                    }
                }
            }
        }

        // 5. Prune old completed and dead-letter tasks
        val completedCutoff = System.currentTimeMillis() - TimeUnit.HOURS.toMillis(PRUNE_COMPLETED_HOURS)
        syncTaskDao.pruneCompleted(completedCutoff)

        val deadLetterCutoff = System.currentTimeMillis() - TimeUnit.DAYS.toMillis(PRUNE_DEAD_LETTER_DAYS)
        syncTaskDao.pruneDeadLetter(deadLetterCutoff)

        Log.i(TAG, "flush() complete — synced=$successCount, failed=$failCount")
        return Pair(successCount, failCount)
    }

    /**
     * Flush only tasks of a specific entity type.
     * Used for targeted sync (e.g., "sync all pending SOS events NOW").
     */
    suspend fun flushEntityType(entityType: SyncEntityType): Pair<Int, Int> {
        syncTaskDao.resetStuckTasks()

        val tasks = syncTaskDao.getPendingByEntityType(entityType, MAX_RETRIES_DEFAULT)
        if (tasks.isEmpty()) return Pair(0, 0)

        var success = 0
        var fail = 0

        for (task in tasks) {
            syncTaskDao.markInProgress(task.id)
            when (val result = syncDispatcher.dispatch(task)) {
                is SyncDispatchResult.Success -> {
                    syncTaskDao.markCompleted(task.id)
                    success++
                    providers[task.entityType]?.onAfterDispatch(task, result.serverId)
                }
                is SyncDispatchResult.RetryableFailure -> {
                    val newRetry = task.retryCount + 1
                    if (newRetry >= task.maxRetries) {
                        syncTaskDao.markDeadLetter(task.id)
                    } else {
                        syncTaskDao.markFailed(task.id, result.message, newRetry)
                    }
                    fail++
                }
                is SyncDispatchResult.PermanentFailure -> {
                    syncTaskDao.markDeadLetter(task.id)
                    fail++
                }
            }
        }

        return Pair(success, fail)
    }

    /**
     * Re-queue a dead-letter task for manual retry.
     */
    suspend fun retryDeadLetter(taskId: String) {
        syncTaskDao.requeue(taskId)
        Log.i(TAG, "Re-queued dead-letter task $taskId")
        triggerFlushIfNeeded(SyncPriority.HIGH)
    }

    /**
     * Observe the number of pending tasks (for UI badge).
     */
    fun observePendingCount(): Flow<Int> = syncTaskDao.observePendingCount()

    /**
     * Clear all pending sync tasks (used on logout).
     */
    suspend fun clearAll() {
        syncTaskDao.clearAll()
        Log.i(TAG, "All sync tasks cleared")
    }

    // ── Private Helpers ──────────────────────────────────────────────

    /**
     * Trigger an expedited WorkManager sync if the task is high priority
     * and we're currently online.
     */
    private fun triggerFlushIfNeeded(priority: Int) {
        if (priority <= SyncPriority.HIGH && connectivityMonitor.isOnline()) {
            SyncWorker.enqueueExpedited(context)
        }
    }

    /**
     * Per-task exponential backoff: min(BASE * 2^retryCount, MAX).
     */
    private fun shouldDefer(task: SyncTaskEntity): Boolean {
        if (task.retryCount == 0 || task.lastAttemptAt == null) return false

        val backoffSeconds = min(
            BASE_BACKOFF_SECONDS * 2.0.pow(task.retryCount.toDouble()).toLong(),
            MAX_BACKOFF_SECONDS
        )
        val nextAllowedTime = task.lastAttemptAt + (backoffSeconds * 1000)
        return System.currentTimeMillis() < nextAllowedTime
    }
}
