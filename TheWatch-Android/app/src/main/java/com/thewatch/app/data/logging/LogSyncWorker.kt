package com.thewatch.app.data.logging

import android.content.Context
import android.util.Log
import androidx.hilt.work.HiltWorker
import androidx.work.CoroutineWorker
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import androidx.work.WorkerParameters
import dagger.assisted.Assisted
import dagger.assisted.AssistedInject
import java.time.Duration
import java.util.concurrent.TimeUnit

/**
 * WorkManager periodic worker that syncs local log entries to Firestore.
 *
 * Schedule:
 * - Every 15 minutes (minimum WorkManager interval)
 * - Requires network connectivity
 * - Also prunes entries older than 7 days locally
 *
 * Enqueued from TheWatchApplication.onCreate() or AppModule.
 */
@HiltWorker
class LogSyncWorker @AssistedInject constructor(
    @Assisted context: Context,
    @Assisted params: WorkerParameters,
    private val syncPort: LogSyncPort,
    private val loggingPort: LoggingPort
) : CoroutineWorker(context, params) {

    companion object {
        private const val TAG = "TheWatch"
        const val WORK_NAME = "thewatch_log_sync"
        private val LOCAL_RETENTION = Duration.ofDays(7)

        fun enqueue(context: Context) {
            val request = PeriodicWorkRequestBuilder<LogSyncWorker>(
                15, TimeUnit.MINUTES
            ).build()

            WorkManager.getInstance(context).enqueueUniquePeriodicWork(
                WORK_NAME,
                ExistingPeriodicWorkPolicy.KEEP,
                request
            )
            Log.i(TAG, "[LogSyncWorker] Periodic sync enqueued (15min)")
        }
    }

    override suspend fun doWork(): Result {
        return try {
            // ── Sync to Firestore ────────────────────────────────
            val synced = syncPort.syncToFirestore()
            Log.i(TAG, "[LogSyncWorker] Synced $synced entries")

            // ── Prune old local entries ──────────────────────────
            val cutoff = System.currentTimeMillis() - LOCAL_RETENTION.toMillis()
            loggingPort.prune(cutoff)

            Result.success()
        } catch (e: Exception) {
            Log.e(TAG, "[LogSyncWorker] Failed: ${e.message}", e)
            Result.retry()
        }
    }
}
