/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         OfflineSyncWorker.kt                                   │
 * │ Purpose:      WorkManager PeriodicWorkRequest that flushes queued    │
 * │               SyncLog entries every 15 minutes. Implements           │
 * │               exponential backoff per-entry on failure (base 30s,    │
 * │               max 5 retries). Prioritizes SOS alerts above all      │
 * │               other sync operations.                                 │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: WorkManager, Room (SyncLogDao), SyncPort, Hilt        │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   // Schedule in Application.onCreate() or after login:              │
 * │   OfflineSyncWorker.enqueue(applicationContext)                      │
 * │                                                                      │
 * │   // Cancel (e.g., on logout):                                       │
 * │   OfflineSyncWorker.cancel(applicationContext)                       │
 * │                                                                      │
 * │ NOTE: WorkManager guarantees execution even after device reboot      │
 * │ (when combined with BootReceiver re-enqueue). The 15-minute minimum  │
 * │ is enforced by Android for PeriodicWorkRequest. For more urgent      │
 * │ flush (e.g., SOS triggered while offline), use expedited OneTime     │
 * │ work via enqueueExpedited().                                         │
 * │ On Samsung OneUI / Xiaomi MIUI / Huawei EMUI, WorkManager may be   │
 * │ delayed by aggressive battery optimization. Users should be guided  │
 * │ to exempt TheWatch from battery optimization in PermissionsScreen.  │
 * └──────────────────────────────────────────────────────────────────────┘
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
import com.thewatch.app.data.local.SyncLogDao
import com.thewatch.app.data.local.SyncStatus
import dagger.assisted.Assisted
import dagger.assisted.AssistedInject
import java.util.concurrent.TimeUnit
import kotlin.math.min
import kotlin.math.pow

@HiltWorker
class OfflineSyncWorker @AssistedInject constructor(
    @Assisted appContext: Context,
    @Assisted workerParams: WorkerParameters,
    private val syncLogDao: SyncLogDao,
    private val syncPort: SyncPort
) : CoroutineWorker(appContext, workerParams) {

    companion object {
        private const val TAG = "TheWatch.OfflineSync"
        private const val UNIQUE_PERIODIC_WORK = "offline_sync_periodic"
        private const val UNIQUE_EXPEDITED_WORK = "offline_sync_expedited"
        private const val MAX_RETRIES = 5
        private const val BASE_BACKOFF_SECONDS = 30L
        private const val MAX_BACKOFF_SECONDS = 900L // 15 minutes
        private const val PERIODIC_INTERVAL_MINUTES = 15L
        private const val BATCH_SIZE = 50

        /**
         * Enqueue periodic sync worker. Runs every 15 minutes when network is available.
         * Safe to call multiple times — uses KEEP policy to avoid duplicates.
         */
        fun enqueue(context: Context) {
            val constraints = Constraints.Builder()
                .setRequiredNetworkType(NetworkType.CONNECTED)
                .build()

            val request = PeriodicWorkRequestBuilder<OfflineSyncWorker>(
                PERIODIC_INTERVAL_MINUTES, TimeUnit.MINUTES
            )
                .setConstraints(constraints)
                .setBackoffCriteria(
                    BackoffPolicy.EXPONENTIAL,
                    BASE_BACKOFF_SECONDS, TimeUnit.SECONDS
                )
                .addTag("offline_sync")
                .build()

            WorkManager.getInstance(context).enqueueUniquePeriodicWork(
                UNIQUE_PERIODIC_WORK,
                ExistingPeriodicWorkPolicy.KEEP,
                request
            )
            Log.i(TAG, "Periodic sync worker enqueued (${PERIODIC_INTERVAL_MINUTES}min)")
        }

        /**
         * Enqueue an expedited one-time sync. Used when SOS is triggered offline
         * and connectivity is restored, or when the user manually requests sync.
         */
        fun enqueueExpedited(context: Context) {
            val constraints = Constraints.Builder()
                .setRequiredNetworkType(NetworkType.CONNECTED)
                .build()

            val request = OneTimeWorkRequestBuilder<OfflineSyncWorker>()
                .setConstraints(constraints)
                .addTag("offline_sync_expedited")
                .build()

            WorkManager.getInstance(context).enqueueUniqueWork(
                UNIQUE_EXPEDITED_WORK,
                ExistingWorkPolicy.REPLACE,
                request
            )
            Log.i(TAG, "Expedited sync worker enqueued")
        }

        /**
         * Cancel all scheduled sync work (e.g., on user logout).
         */
        fun cancel(context: Context) {
            WorkManager.getInstance(context).cancelUniqueWork(UNIQUE_PERIODIC_WORK)
            WorkManager.getInstance(context).cancelUniqueWork(UNIQUE_EXPEDITED_WORK)
            Log.i(TAG, "Sync workers cancelled")
        }
    }

    override suspend fun doWork(): Result {
        Log.i(TAG, "doWork() started — run attempt ${runAttemptCount}")

        try {
            // 1. Recover any entries stuck in IN_PROGRESS from a previous crash
            syncLogDao.resetStuckEntries()

            // 2. Check backend reachability before processing
            if (!syncPort.isBackendReachable()) {
                Log.w(TAG, "Backend not reachable — retrying later")
                return Result.retry()
            }

            // 3. Fetch retryable entries, ordered by priority (SOS first)
            val entries = syncLogDao.getRetryableEntries(MAX_RETRIES)
            if (entries.isEmpty()) {
                Log.i(TAG, "No pending entries to sync")
                return Result.success()
            }

            Log.i(TAG, "Processing ${entries.size} pending entries")

            var successCount = 0
            var failCount = 0

            // 4. Process in batches to avoid holding DB transactions too long
            entries.chunked(BATCH_SIZE).forEach { batch ->
                for (entry in batch) {
                    // Check if entry should be deferred based on per-entry backoff
                    if (shouldDeferEntry(entry)) {
                        Log.d(TAG, "Deferring entry ${entry.id} (retry ${entry.retryCount})")
                        continue
                    }

                    syncLogDao.markInProgress(entry.id)

                    when (val result = syncPort.push(entry)) {
                        is SyncPushResult.Success -> {
                            syncLogDao.markCompleted(entry.id)
                            successCount++
                            Log.d(TAG, "Synced entry ${entry.id} -> ${result.serverId}")
                        }

                        is SyncPushResult.RetryableFailure -> {
                            val newRetryCount = entry.retryCount + 1
                            syncLogDao.markFailed(entry.id, result.message, newRetryCount)
                            failCount++
                            Log.w(TAG, "Retryable failure for ${entry.id}: ${result.message}")

                            // If circuit is now open, stop processing this batch
                            if (syncPort.isCircuitOpen()) {
                                Log.w(TAG, "Circuit breaker open — stopping batch")
                                return Result.retry()
                            }
                        }

                        is SyncPushResult.PermanentFailure -> {
                            // Mark as max retries so it won't be picked up again
                            syncLogDao.markFailed(entry.id, result.message, MAX_RETRIES)
                            failCount++
                            Log.e(TAG, "Permanent failure for ${entry.id}: ${result.message}")
                        }
                    }
                }
            }

            // 5. Prune completed entries older than 24 hours
            val cutoff = System.currentTimeMillis() - TimeUnit.HOURS.toMillis(24)
            syncLogDao.pruneCompleted(cutoff)

            // 6. Prune exhausted retries older than 7 days
            val retryCutoff = System.currentTimeMillis() - TimeUnit.DAYS.toMillis(7)
            syncLogDao.pruneExhaustedRetries(MAX_RETRIES, retryCutoff)

            Log.i(TAG, "doWork() complete — synced=$successCount, failed=$failCount")

            return if (failCount > 0 && successCount == 0) {
                Result.retry()
            } else {
                Result.success()
            }

        } catch (e: Exception) {
            Log.e(TAG, "doWork() exception", e)
            return if (runAttemptCount < 3) Result.retry() else Result.failure()
        }
    }

    /**
     * Calculate whether an entry should be deferred based on exponential backoff.
     * Backoff = min(BASE_BACKOFF * 2^retryCount, MAX_BACKOFF).
     */
    private fun shouldDeferEntry(entry: SyncLogEntity): Boolean {
        if (entry.retryCount == 0 || entry.lastAttemptAt == null) return false

        val backoffSeconds = min(
            BASE_BACKOFF_SECONDS * 2.0.pow(entry.retryCount.toDouble()).toLong(),
            MAX_BACKOFF_SECONDS
        )
        val nextAllowedTime = entry.lastAttemptAt + (backoffSeconds * 1000)
        return System.currentTimeMillis() < nextAllowedTime
    }
}
