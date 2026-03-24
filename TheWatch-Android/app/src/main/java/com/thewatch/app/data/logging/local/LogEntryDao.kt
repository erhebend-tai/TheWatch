package com.thewatch.app.data.logging.local

import androidx.room.Dao
import androidx.room.Insert
import androidx.room.OnConflictStrategy
import androidx.room.Query

/**
 * Room DAO for structured log entries.
 * Supports the native logging adapter's local persistence layer.
 */
@Dao
interface LogEntryDao {

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insert(entry: LogEntryEntity)

    @Insert(onConflict = OnConflictStrategy.REPLACE)
    suspend fun insertAll(entries: List<LogEntryEntity>)

    @Query("""
        SELECT * FROM log_entries
        WHERE level >= :minLevel
        ORDER BY timestamp DESC
        LIMIT :limit
    """)
    suspend fun getRecent(limit: Int, minLevel: Int): List<LogEntryEntity>

    @Query("""
        SELECT * FROM log_entries
        WHERE level >= :minLevel AND source_context = :sourceContext
        ORDER BY timestamp DESC
        LIMIT :limit
    """)
    suspend fun getRecentBySource(limit: Int, minLevel: Int, sourceContext: String): List<LogEntryEntity>

    @Query("SELECT * FROM log_entries WHERE correlation_id = :correlationId ORDER BY timestamp ASC")
    suspend fun getByCorrelation(correlationId: String): List<LogEntryEntity>

    @Query("SELECT * FROM log_entries WHERE synced = 0 ORDER BY timestamp ASC LIMIT :batchSize")
    suspend fun getUnsynced(batchSize: Int = 500): List<LogEntryEntity>

    @Query("UPDATE log_entries SET synced = 1 WHERE id IN (:ids)")
    suspend fun markSynced(ids: List<String>)

    @Query("DELETE FROM log_entries WHERE timestamp < :cutoffEpochMs")
    suspend fun deleteOlderThan(cutoffEpochMs: Long): Int

    @Query("SELECT COUNT(*) FROM log_entries")
    suspend fun count(): Int

    @Query("SELECT COUNT(*) FROM log_entries WHERE synced = 0")
    suspend fun unsyncedCount(): Int

    @Query("DELETE FROM log_entries")
    suspend fun deleteAll()
}
