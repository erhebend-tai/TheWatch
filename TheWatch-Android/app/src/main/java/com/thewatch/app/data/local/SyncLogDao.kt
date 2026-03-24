/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         SyncLogDao.kt                                          │
 * │ Purpose:      Room DAO for SyncLogEntity. Full CRUD plus query       │
 * │               methods for the offline sync queue.                    │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: Room, SyncLogEntity                                    │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   val pending = syncLogDao.getPendingByPriority()                    │
 * │   pending.forEach { entry ->                                         │
 * │       syncLogDao.markInProgress(entry.id)                            │
 * │       try { syncPort.push(entry); syncLogDao.markCompleted(entry.id) │
 * │       } catch (e: Exception) { syncLogDao.markFailed(entry.id, ...) │
 * │       }                                                              │
 * │   }                                                                  │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.local

import androidx.room.Dao
import androidx.room.Delete
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query
import androidx.room.Update
import kotlinx.coroutines.flow.Flow

@Dao
interface SyncLogDao {

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertLog(log: SyncLogEntity)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertLogs(logs: List<SyncLogEntity>)

    @Update
    suspend fun updateLog(log: SyncLogEntity)

    @Delete
    suspend fun deleteLog(log: SyncLogEntity)

    // Pending operations ordered by priority then creation time
    @Query("SELECT * FROM sync_logs WHERE status = 'PENDING' OR status = 'FAILED' ORDER BY priority ASC, createdAt ASC")
    suspend fun getPendingByPriority(): List<SyncLogEntity>

    @Query("SELECT * FROM sync_logs WHERE (status = 'PENDING' OR status = 'FAILED') AND retryCount < :maxRetries ORDER BY priority ASC, createdAt ASC")
    suspend fun getRetryableEntries(maxRetries: Int = 5): List<SyncLogEntity>

    @Query("SELECT * FROM sync_logs WHERE status = 'IN_PROGRESS'")
    suspend fun getInProgressEntries(): List<SyncLogEntity>

    // Legacy queries for backward compat
    @Query("SELECT * FROM sync_logs WHERE synced = 0 ORDER BY timestamp ASC")
    suspend fun getUnsyncedLogs(): List<SyncLogEntity>

    @Query("SELECT * FROM sync_logs WHERE userId = :userId ORDER BY timestamp DESC LIMIT 50")
    suspend fun getLogsByUser(userId: String): List<SyncLogEntity>

    // Status transitions
    @Query("UPDATE sync_logs SET status = 'IN_PROGRESS', lastAttemptAt = :now WHERE id = :id")
    suspend fun markInProgress(id: String, now: Long = System.currentTimeMillis())

    @Query("UPDATE sync_logs SET status = 'COMPLETED', synced = 1, lastAttemptAt = :now WHERE id = :id")
    suspend fun markCompleted(id: String, now: Long = System.currentTimeMillis())

    @Query("UPDATE sync_logs SET status = 'FAILED', lastError = :error, retryCount = :retryCount, lastAttemptAt = :now, syncAttempts = :retryCount WHERE id = :id")
    suspend fun markFailed(id: String, error: String?, retryCount: Int, now: Long = System.currentTimeMillis())

    @Query("UPDATE sync_logs SET status = 'PENDING' WHERE status = 'IN_PROGRESS'")
    suspend fun resetStuckEntries()

    // Counts
    @Query("SELECT COUNT(*) FROM sync_logs WHERE status = 'PENDING' OR status = 'FAILED'")
    fun observePendingCount(): Flow<Int>

    @Query("SELECT COUNT(*) FROM sync_logs WHERE status = 'PENDING' OR status = 'FAILED'")
    suspend fun getPendingCount(): Int

    // Cleanup
    @Query("DELETE FROM sync_logs WHERE synced = 1 AND timestamp < :cutoffTime")
    suspend fun deleteOldSyncedLogs(cutoffTime: Long)

    @Query("DELETE FROM sync_logs WHERE status = 'COMPLETED' AND lastAttemptAt < :cutoffTime")
    suspend fun pruneCompleted(cutoffTime: Long)

    @Query("DELETE FROM sync_logs WHERE status = 'FAILED' AND retryCount >= :maxRetries AND createdAt < :cutoffTime")
    suspend fun pruneExhaustedRetries(maxRetries: Int = 5, cutoffTime: Long)

    @Query("DELETE FROM sync_logs")
    suspend fun clearAllLogs()

    // Query by action type
    @Query("SELECT * FROM sync_logs WHERE action = :action ORDER BY createdAt DESC")
    suspend fun getByAction(action: String): List<SyncLogEntity>

    @Query("SELECT * FROM sync_logs WHERE action = :action AND status = 'PENDING' ORDER BY createdAt ASC")
    suspend fun getPendingByAction(action: String): List<SyncLogEntity>
}
