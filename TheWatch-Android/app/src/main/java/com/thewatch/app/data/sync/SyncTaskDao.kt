/**
 * +----------------------------------------------------------------------+
 * | WRITE-AHEAD LOG                                                       |
 * +----------------------------------------------------------------------+
 * | File:         SyncTaskDao.kt                                          |
 * | Purpose:      Room DAO for SyncTaskEntity. Provides priority-ordered  |
 * |               queue access, status transitions, retry management,     |
 * |               and cleanup for the generalized sync engine.            |
 * | Created:      2026-03-24                                              |
 * | Author:       Claude                                                  |
 * | Dependencies: Room, SyncTaskEntity                                    |
 * |                                                                       |
 * | Usage example:                                                        |
 * |   val tasks = syncTaskDao.getPendingByPriority(maxRetries = 5)        |
 * |   for (task in tasks) {                                               |
 * |       syncTaskDao.markInProgress(task.id)                             |
 * |       when (val r = dispatcher.dispatch(task)) {                      |
 * |           is Success -> syncTaskDao.markCompleted(task.id)            |
 * |           is Failure -> syncTaskDao.markFailed(task.id, r.msg, ...)   |
 * |       }                                                               |
 * |   }                                                                   |
 * |                                                                       |
 * | NOTE: All status transition queries use atomic UPDATE statements.     |
 * | The IN_PROGRESS state prevents duplicate processing if two workers    |
 * | overlap (shouldn't happen with unique work, but defense in depth).    |
 * +----------------------------------------------------------------------+
 */
package com.thewatch.app.data.sync

import androidx.room.Dao
import androidx.room.Delete
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query
import androidx.room.Update
import kotlinx.coroutines.flow.Flow

@Dao
interface SyncTaskDao {

    // ── Insert / Update / Delete ─────────────────────────────────────

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insert(task: SyncTaskEntity)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertAll(tasks: List<SyncTaskEntity>)

    @Update
    suspend fun update(task: SyncTaskEntity)

    @Delete
    suspend fun delete(task: SyncTaskEntity)

    // ── Queue Reads ──────────────────────────────────────────────────

    /**
     * Fetch all QUEUED and retryable FAILED tasks, ordered by priority (ASC)
     * then createdAt (ASC). This ensures SOS tasks (priority=0) process first.
     */
    @Query("""
        SELECT * FROM sync_tasks
        WHERE (status = 'QUEUED' OR status = 'FAILED')
          AND retryCount < :maxRetries
        ORDER BY priority ASC, createdAt ASC
    """)
    suspend fun getPendingByPriority(maxRetries: Int = 5): List<SyncTaskEntity>

    /**
     * Fetch pending tasks for a specific entity type.
     * Useful for targeted sync (e.g., "flush all pending SOS events now").
     */
    @Query("""
        SELECT * FROM sync_tasks
        WHERE (status = 'QUEUED' OR status = 'FAILED')
          AND entityType = :entityType
          AND retryCount < :maxRetries
        ORDER BY priority ASC, createdAt ASC
    """)
    suspend fun getPendingByEntityType(
        entityType: SyncEntityType,
        maxRetries: Int = 5
    ): List<SyncTaskEntity>

    /**
     * Get tasks currently being processed. Used to detect stuck tasks
     * after a crash (worker killed mid-push).
     */
    @Query("SELECT * FROM sync_tasks WHERE status = 'IN_PROGRESS'")
    suspend fun getInProgress(): List<SyncTaskEntity>

    /**
     * Get tasks that exhausted retries (dead-letter queue).
     * Useful for diagnostics screen and manual retry.
     */
    @Query("""
        SELECT * FROM sync_tasks
        WHERE status = 'DEAD_LETTER'
        ORDER BY createdAt DESC
    """)
    suspend fun getDeadLetterTasks(): List<SyncTaskEntity>

    /**
     * Lookup a specific entity's pending sync task. Used for dedup —
     * if an UPDATE for the same entityType+entityId is already queued,
     * the engine can coalesce payloads instead of creating a new task.
     */
    @Query("""
        SELECT * FROM sync_tasks
        WHERE entityType = :entityType
          AND entityId = :entityId
          AND (status = 'QUEUED' OR status = 'FAILED')
        LIMIT 1
    """)
    suspend fun findPendingForEntity(
        entityType: SyncEntityType,
        entityId: String
    ): SyncTaskEntity?

    // ── Status Transitions ───────────────────────────────────────────

    @Query("""
        UPDATE sync_tasks
        SET status = 'IN_PROGRESS', lastAttemptAt = :now
        WHERE id = :id
    """)
    suspend fun markInProgress(id: String, now: Long = System.currentTimeMillis())

    @Query("""
        UPDATE sync_tasks
        SET status = 'COMPLETED', lastAttemptAt = :now
        WHERE id = :id
    """)
    suspend fun markCompleted(id: String, now: Long = System.currentTimeMillis())

    @Query("""
        UPDATE sync_tasks
        SET status = 'FAILED',
            lastError = :error,
            retryCount = :retryCount,
            lastAttemptAt = :now
        WHERE id = :id
    """)
    suspend fun markFailed(
        id: String,
        error: String?,
        retryCount: Int,
        now: Long = System.currentTimeMillis()
    )

    /**
     * Move exhausted-retry tasks to DEAD_LETTER status.
     * Called by the engine after a task exceeds maxRetries.
     */
    @Query("""
        UPDATE sync_tasks
        SET status = 'DEAD_LETTER', lastAttemptAt = :now
        WHERE id = :id
    """)
    suspend fun markDeadLetter(id: String, now: Long = System.currentTimeMillis())

    /**
     * Recover stuck IN_PROGRESS tasks back to QUEUED after a crash.
     * Called at the start of each sync cycle.
     */
    @Query("UPDATE sync_tasks SET status = 'QUEUED' WHERE status = 'IN_PROGRESS'")
    suspend fun resetStuckTasks()

    /**
     * Re-queue a dead-letter task for manual retry.
     */
    @Query("""
        UPDATE sync_tasks
        SET status = 'QUEUED', retryCount = 0, lastError = NULL
        WHERE id = :id AND status = 'DEAD_LETTER'
    """)
    suspend fun requeue(id: String)

    // ── Counts & Observables ─────────────────────────────────────────

    @Query("""
        SELECT COUNT(*) FROM sync_tasks
        WHERE status = 'QUEUED' OR status = 'FAILED'
    """)
    fun observePendingCount(): Flow<Int>

    @Query("""
        SELECT COUNT(*) FROM sync_tasks
        WHERE status = 'QUEUED' OR status = 'FAILED'
    """)
    suspend fun getPendingCount(): Int

    @Query("SELECT COUNT(*) FROM sync_tasks WHERE status = 'DEAD_LETTER'")
    suspend fun getDeadLetterCount(): Int

    @Query("""
        SELECT COUNT(*) FROM sync_tasks
        WHERE entityType = :entityType
          AND (status = 'QUEUED' OR status = 'FAILED')
    """)
    suspend fun getPendingCountByType(entityType: SyncEntityType): Int

    // ── Cleanup ──────────────────────────────────────────────────────

    /**
     * Prune completed tasks older than the given cutoff.
     * Default recommendation: 24 hours after completion.
     */
    @Query("""
        DELETE FROM sync_tasks
        WHERE status = 'COMPLETED'
          AND lastAttemptAt < :cutoffTime
    """)
    suspend fun pruneCompleted(cutoffTime: Long)

    /**
     * Prune dead-letter tasks older than the given cutoff.
     * Default recommendation: 7 days.
     */
    @Query("""
        DELETE FROM sync_tasks
        WHERE status = 'DEAD_LETTER'
          AND createdAt < :cutoffTime
    """)
    suspend fun pruneDeadLetter(cutoffTime: Long)

    /**
     * Nuclear option: clear the entire queue. Used on logout/account deletion.
     */
    @Query("DELETE FROM sync_tasks")
    suspend fun clearAll()
}
