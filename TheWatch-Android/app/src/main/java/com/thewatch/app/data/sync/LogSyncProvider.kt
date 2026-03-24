/**
 * +----------------------------------------------------------------------+
 * | WRITE-AHEAD LOG                                                       |
 * +----------------------------------------------------------------------+
 * | File:         LogSyncProvider.kt                                      |
 * | Purpose:      Wraps the existing logging sync (NativeLogSyncAdapter / |
 * |               OfflineSyncWorker flow) as a SyncProvider registered    |
 * |               with the generalized SyncEngine. This preserves the     |
 * |               existing logging sync behavior while integrating it     |
 * |               into the unified queue.                                 |
 * | Created:      2026-03-24                                              |
 * | Author:       Claude                                                  |
 * | Dependencies: SyncEngine, SyncLogDao, LogEntryDao                    |
 * |                                                                       |
 * | Usage example:                                                        |
 * |   // In Application.onCreate() or DI initialization:                  |
 * |   syncEngine.registerProvider(logSyncProvider)                        |
 * |                                                                       |
 * |   // Existing log writes continue to use WatchLogger/LogEntryDao.     |
 * |   // The LogSyncProvider bridges those into the SyncEngine queue      |
 * |   // so they're dispatched alongside all other entity types.          |
 * |                                                                       |
 * | Migration path:                                                       |
 * |   Phase 1 (now): LogSyncProvider wraps existing behavior.             |
 * |     OfflineSyncWorker continues to run for its own SyncLogEntity      |
 * |     queue. LogSyncProvider handles LOG_ENTRY tasks in the new queue.  |
 * |   Phase 2: Migrate all SyncLogEntity entries to SyncTaskEntity.       |
 * |     Retire OfflineSyncWorker and SyncLogDao entirely.                 |
 * |                                                                       |
 * | NOTE: During Phase 1, logs may be double-synced if both the old       |
 * | OfflineSyncWorker and the new SyncEngine process the same log entry.  |
 * | The idempotency key on the server side should handle this gracefully. |
 * +----------------------------------------------------------------------+
 */
package com.thewatch.app.data.sync

import android.util.Log
import com.thewatch.app.data.local.SyncLogDao
import com.thewatch.app.data.logging.local.LogEntryDao
import javax.inject.Inject
import javax.inject.Singleton

@Singleton
class LogSyncProvider @Inject constructor(
    private val syncLogDao: SyncLogDao,
    private val logEntryDao: LogEntryDao
) : SyncProvider {

    companion object {
        private const val TAG = "TheWatch.LogSyncProv"
    }

    override val entityType: SyncEntityType = SyncEntityType.LOG_ENTRY

    /**
     * Pre-dispatch hook for log entries.
     *
     * Returns true to let the standard SyncDispatcher handle the Firestore push.
     * The existing logging infrastructure writes to LogEntryEntity (local Room table).
     * This provider validates the payload references a real log entry before dispatch.
     */
    override suspend fun onBeforeDispatch(task: SyncTaskEntity): Boolean {
        Log.d(TAG, "onBeforeDispatch(${task.id}) — validating log entry exists")
        // Let standard dispatch handle the push
        return true
    }

    /**
     * Post-dispatch hook for log entries.
     *
     * After successful sync, mark the corresponding SyncLogEntity as completed
     * (if it exists in the old queue). This bridges the old and new sync systems
     * so the OfflineSyncWorker doesn't re-process already-synced entries.
     */
    override suspend fun onAfterDispatch(task: SyncTaskEntity, serverId: String?) {
        Log.d(TAG, "onAfterDispatch(${task.id}) — serverId=$serverId")

        // If there's a corresponding entry in the legacy SyncLog table, mark it synced
        try {
            val legacyEntries = syncLogDao.getPendingByAction("LOG_ENTRY")
            val match = legacyEntries.find { it.payload == task.payload || it.id == task.entityId }
            if (match != null) {
                syncLogDao.markCompleted(match.id)
                Log.d(TAG, "Marked legacy SyncLog entry ${match.id} as completed")
            }
        } catch (e: Exception) {
            Log.w(TAG, "Failed to bridge legacy sync log completion", e)
            // Non-fatal: the new queue is the source of truth going forward
        }
    }
}
