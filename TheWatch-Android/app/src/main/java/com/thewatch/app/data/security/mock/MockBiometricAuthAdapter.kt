/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         MockBiometricAuthAdapter.kt                            │
 * │ Purpose:      Mock (Tier 1) adapter for BiometricAuthPort.           │
 * │               Always succeeds after a simulated delay. Used for      │
 * │               development, UI testing, and emulator runs where real  │
 * │               biometric hardware is unavailable.                     │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: BiometricAuthPort                                      │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   // In AppModule.kt (dev builds):                                   │
 * │   @Provides fun provideBiometricPort(                                │
 * │       mock: MockBiometricAuthAdapter                                 │
 * │   ): BiometricAuthPort = mock                                        │
 * │                                                                      │
 * │ Behavior:                                                            │
 * │   - checkAvailability() -> Available (configurable via setAvail.)    │
 * │   - authenticate() -> Success after 800ms delay                      │
 * │   - invalidateSession() -> clears internal flag                      │
 * │                                                                      │
 * │ NOTE: For negative-path testing, call setSimulatedFailure(true)      │
 * │ before authenticate() — it will return Failed instead of Success.    │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.security.mock

import android.util.Log
import androidx.fragment.app.FragmentActivity
import com.thewatch.app.data.security.BiometricAuthPort
import com.thewatch.app.data.security.BiometricAvailability
import com.thewatch.app.data.security.BiometricResult
import kotlinx.coroutines.delay
import javax.inject.Inject
import javax.inject.Singleton

@Singleton
class MockBiometricAuthAdapter @Inject constructor() : BiometricAuthPort {

    companion object {
        private const val TAG = "TheWatch.MockBiometric"
        private const val SIMULATED_DELAY_MS = 800L
    }

    /** Toggle to simulate hardware availability. Default: Available. */
    @Volatile
    private var simulatedAvailability: BiometricAvailability = BiometricAvailability.Available

    /** Toggle to simulate authentication failure. Default: false (success). */
    @Volatile
    private var simulateFailure: Boolean = false

    /** Whether a mock "session" is active. */
    @Volatile
    private var hasActiveSession: Boolean = false

    // ── Test configuration helpers ─────────────────────────────────
    fun setSimulatedAvailability(availability: BiometricAvailability) {
        simulatedAvailability = availability
        Log.d(TAG, "Simulated availability set to: $availability")
    }

    fun setSimulatedFailure(fail: Boolean) {
        simulateFailure = fail
        Log.d(TAG, "Simulated failure set to: $fail")
    }

    // ── Port implementation ────────────────────────────────────────

    override suspend fun checkAvailability(): BiometricAvailability {
        Log.d(TAG, "checkAvailability() -> $simulatedAvailability")
        return simulatedAvailability
    }

    override suspend fun authenticate(
        activity: FragmentActivity,
        title: String,
        subtitle: String?,
        negativeButtonText: String
    ): BiometricResult {
        Log.i(TAG, "authenticate() called — simulating ${SIMULATED_DELAY_MS}ms delay")
        delay(SIMULATED_DELAY_MS)

        return if (simulateFailure) {
            Log.w(TAG, "authenticate() -> simulated failure")
            BiometricResult.Failed(errorCode = -1, message = "Mock: simulated biometric failure")
        } else {
            hasActiveSession = true
            Log.i(TAG, "authenticate() -> success (mock)")
            BiometricResult.Success()
        }
    }

    override suspend fun invalidateSession() {
        hasActiveSession = false
        Log.d(TAG, "invalidateSession() — mock session cleared")
    }
}
