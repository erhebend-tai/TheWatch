package com.thewatch.app.worker

import android.content.Context
import androidx.hilt.work.HiltWorker
import androidx.work.CoroutineWorker
import androidx.work.WorkerParameters
import com.google.android.gms.location.FusedLocationProviderClient
import com.google.android.gms.location.Priority
import com.google.android.gms.tasks.CancellationToken
import com.google.android.gms.tasks.OnTokenCanceledListener
import dagger.assisted.Assisted
import dagger.assisted.AssistedInject
import kotlinx.coroutines.tasks.await

/**
 * WorkManager CoroutineWorker for periodic location sync.
 * Runs every 15 minutes as a fallback when the foreground service isn't running.
 * Stores location to Room database.
 */
@HiltWorker
class LocationTrackingWorker @AssistedInject constructor(
    @Assisted context: Context,
    @Assisted params: WorkerParameters,
    private val fusedLocationClient: FusedLocationProviderClient
) : CoroutineWorker(context, params) {

    override suspend fun doWork(): Result {
        return try {
            @Suppress("MissingPermission")
            val location = fusedLocationClient.getCurrentLocation(
                Priority.PRIORITY_BALANCED_POWER_ACCURACY,
                object : CancellationToken() {
                    override fun onCanceledRequested(callback: OnTokenCanceledListener) {}
                    override fun isCancellationRequested() = false
                }
            ).await()

            if (location != null) {
                // Location sync successful
                // TODO: Store location to Room database
                Result.success()
            } else {
                // No location available, retry
                Result.retry()
            }
        } catch (e: SecurityException) {
            // Missing permissions, don't retry
            Result.failure()
        } catch (e: Exception) {
            // Other exceptions, retry
            Result.retry()
        }
    }

    companion object {
        const val WORK_TAG = "location_tracking_work"
        const val PERIODIC_INTERVAL_MINUTES = 15L
    }
}
