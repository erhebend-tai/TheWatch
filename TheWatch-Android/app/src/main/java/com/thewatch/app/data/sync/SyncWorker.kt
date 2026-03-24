/**
 * +----------------------------------------------------------------------+
 * | WRITE-AHEAD LOG                                                       |
 * +----------------------------------------------------------------------+
 * | File:         SyncWorker.kt                                           |
 * | Purpose:      General-purpose WorkManager CoroutineWorker that        |
 * |               delegates to SyncEngine.flush(). Replaces the single-   |
 * |               purpose OfflineSyncWorker with a generalized worker     |
 * |               that processes ALL entity types through the engine.     |
 * | Created:      2026-03-24                                              |
 * | Author:       Claude                                                  |
 * | Dependencies: WorkManager, Hilt, SyncEngine                          |
 * |                                                                       |
 * | Usage example:                                                        |
 * |   // Schedule periodic sync in Application.onCreate():                |
 * |   SyncWorker.enqueue(applicationContext)                              |
 * |                                                                       |
 * |   // Trigger immediate sync (e.g., SOS enqueued while offline):       |
 * |   SyncWorker.enqueueExpedited(applicationContext)                     |
 * |                                                                       |
 * |   // Cancel on logout:                                                |
 * |   SyncWorker.cancel(applicationContext)                               |
 * |                                                                       |
 * | Migration from OfflineSyncWorker:                                     |
 * | The old OfflineSyncWorker operated directly on SyncLogDao + SyncPort. |
 * | This worker delegates to SyncEngine which manages the new             |
 * | SyncTaskEntity queue. The old OfflineSyncWorker is preserved but      |
 * | should be phased out — its functionality is wrapped via the           |
 * | LogSyncProvider registered with the engine.                           |
 * |                                                                       |
 * | NOTE: WorkManager 15-minute minimum for PeriodicWorkRequest is        |
 * | enforced by Android. The expedited one-time work path is for          |
 * | immediate sync needs. On OEM-skinned devices (Samsung OneUI,          |
 * | Xiaomi MIUI, Huawei EMUI), WorkManager may be delayed by aggressive   |
 * | battery optimization — guide users to exempt TheWatch.                |
 * +----------------------------------------------------------------------+
 */
package com.thewatch.app.data.sync

import android.content.Context
import android.util.Log
import androidx.hilt.work.HiltWorker
import androidx.work.BackoffPolicy
import androidx.work.Constraints
import androidx.work.CoroutineWorker
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.ExistingWorkPolicy
import androidx.work.NetworkType
import androidx.work.OneTimeWorkRequestBuilder
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import androidx.work.WorkerParameters
import dagger.assisted.Assisted
import dagger.assisted.AssistedInject
import java.util.concurrent.TimeUnit

@HiltWorker
class SyncWorker @AssistedInject constructor(
    @Assisted appContext: Context,
    @Assisted workerParams: WorkerParameters,
    private val syncEngine: SyncEngine
) : CoroutineWorker(appContext, workerParams) {

    companion object {
        private const val TAG = "TheWatch.SyncWorker"
        private const val UNIQUE_PERIODIC_WORK = "sync_engine_periodic"
        private const val UNIQUE_EXPEDITED_WORK = "sync_engine_expedited"
        private const val PERIODIC_INTERVAL_MINUTES = 15L
        private const val BASE_BACKOFF_SECONDS = 30L

        /**
         * Enqueue periodic sync. Runs every 15 minutes when network is available.
         * Safe to call multiple times — KEEP policy prevents duplicates.
         */
        fun enqueue(context: Context) {
            val constraints = Constraints.Builder()
                .setRequiredNetworkType(NetworkType.CONNECTED)
                .build()

            val request = PeriodicWorkRequestBuilder<SyncWorker>(
                PERIODIC_INTERVAL_MINUTES, TimeUnit.MINUTES
            )
                .setConstraints(constraints)
                .setBackoffCriteria(
                    BackoffPolicy.EXPONENTIAL,
                    BASE_BACKOFF_SECONDS, TimeUnit.SECONDS
                )
                .addTag("sync_engine")
                .build()

            WorkManager.getInstance(context).enqueueUniquePeriodicWork(
                UNIQUE_PERIODIC_WORK,
                ExistingPeriodicWorkPolicy.KEEP,
                request
            )
            Log.i(TAG, "Periodic SyncWorker enqueued (${PERIODIC_INTERVAL_MINUTES}min)")
        }

        /**
         * Enqueue an expedited one-time sync. Used when:
         * - SOS is triggered offline and connectivity returns
         * - ConnectivityMonitor detects reconnection
         * - User manually requests sync
         * - High-priority task is enqueued while online
         */
        fun enqueueExpedited(context: Context) {
            val constraints = Constraints.Builder()
                .setRequiredNetworkType(NetworkType.CONNECTED)
                .build()

            val request = OneTimeWorkRequestBuilder<SyncWorker>()
                .setConstraints(constraints)
                .addTag("sync_engine_expedited")
                .build()

            WorkManager.getInstance(context).enqueueUniqueWork(
                UNIQUE_EXPEDITED_WORK,
                ExistingWorkPolicy.REPLACE,
                request
            )
            Log.i(TAG, "Expedited SyncWorker enqueued")
        }

        /**
         * Cancel all scheduled sync work (e.g., on user logout).
         */
        fun cancel(context: Context) {
            WorkManager.getInstance(context).cancelUniqueWork(UNIQUE_PERIODIC_WORK)
            WorkManager.getInstance(context).cancelUniqueWork(UNIQUE_EXPEDITED_WORK)
            Log.i(TAG, "SyncWorker cancelled")
        }
    }

    override suspend fun doWork(): Result {
        Log.i(TAG, "doWork() started — run attempt $runAttemptCount")

        return try {
            val (successCount, failCount) = syncEngine.flush()

            Log.i(TAG, "doWork() complete — synced=$successCount, failed=$failCount")

            if (failCount > 0 && successCount == 0) {
                // All tasks failed — retry the worker
                Result.retry()
            } else {
                Result.success()
            }
        } catch (e: Exception) {
            Log.e(TAG, "doWork() exception", e)
            if (runAttemptCount < 3) Result.retry() else Result.failure()
        }
    }
}
