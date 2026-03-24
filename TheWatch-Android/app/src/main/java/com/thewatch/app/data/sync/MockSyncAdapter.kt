/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         MockSyncAdapter.kt                                     │
 * │ Purpose:      Mock (Tier 1) adapter for SyncPort. Simulates          │
 * │               successful server pushes with configurable delay       │
 * │               and optional failure simulation.                       │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: SyncPort, SyncLogEntity                                │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   // In AppModule.kt (dev builds):                                   │
 * │   @Provides fun provideSyncPort(                                     │
 * │       mock: MockSyncAdapter                                          │
 * │   ): SyncPort = mock                                                 │
 * │                                                                      │
 * │ NOTE: For testing failure paths, call setSimulateFailure(true).      │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.sync

import android.util.Log
import com.thewatch.app.data.local.SyncLogEntity
import kotlinx.coroutines.delay
import javax.inject.Inject
import javax.inject.Singleton

@Singleton
class MockSyncAdapter @Inject constructor() : SyncPort {

    companion object {
        private const val TAG = "TheWatch.MockSync"
        private const val SIMULATED_DELAY_MS = 200L
    }

    @Volatile
    private var simulateFailure: Boolean = false

    @Volatile
    private var simulateBackendDown: Boolean = false

    fun setSimulateFailure(fail: Boolean) {
        simulateFailure = fail
    }

    fun setSimulateBackendDown(down: Boolean) {
        simulateBackendDown = down
    }

    override suspend fun push(entry: SyncLogEntity): SyncPushResult {
        Log.d(TAG, "push(${entry.id}, action=${entry.action})")
        delay(SIMULATED_DELAY_MS)

        return if (simulateFailure) {
            Log.w(TAG, "push() -> simulated retryable failure")
            SyncPushResult.RetryableFailure("Mock: simulated server error")
        } else {
            Log.i(TAG, "push() -> success (mock)")
            SyncPushResult.Success(serverId = "mock-server-${entry.id}")
        }
    }

    override suspend fun isBackendReachable(): Boolean {
        return !simulateBackendDown
    }

    override suspend fun isCircuitOpen(): Boolean {
        return simulateBackendDown
    }
}
