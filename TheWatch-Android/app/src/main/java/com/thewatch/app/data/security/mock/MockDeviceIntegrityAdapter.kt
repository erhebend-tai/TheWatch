/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         MockDeviceIntegrityAdapter.kt                          │
 * │ Purpose:      Mock (Tier 1) adapter for DeviceIntegrityPort.         │
 * │               Always returns a clean verdict — the device is         │
 * │               "trusted." Used for development and emulator runs      │
 * │               where real integrity checks would always fail.         │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: DeviceIntegrityPort                                    │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   // In AppModule.kt (dev builds):                                   │
 * │   @Provides fun provideDeviceIntegrityPort(                          │
 * │       mock: MockDeviceIntegrityAdapter                               │
 * │   ): DeviceIntegrityPort = mock                                      │
 * │                                                                      │
 * │ For negative-path testing, call setSimulatedCompromised(true).       │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.security.mock

import android.util.Log
import com.thewatch.app.data.security.DeviceIntegrityPort
import com.thewatch.app.data.security.DeviceIntegrityVerdict
import com.thewatch.app.data.security.FindingSeverity
import com.thewatch.app.data.security.IntegrityCheck
import com.thewatch.app.data.security.IntegrityFinding
import com.thewatch.app.data.security.PlayIntegrityLevel
import kotlinx.coroutines.delay
import javax.inject.Inject
import javax.inject.Singleton

@Singleton
class MockDeviceIntegrityAdapter @Inject constructor() : DeviceIntegrityPort {

    companion object {
        private const val TAG = "TheWatch.MockIntegrity"
    }

    @Volatile
    private var simulateCompromised: Boolean = false

    @Volatile
    private var cachedVerdict: DeviceIntegrityVerdict? = null

    /** Toggle to simulate a compromised device for testing. */
    fun setSimulatedCompromised(compromised: Boolean) {
        simulateCompromised = compromised
        cachedVerdict = null // Invalidate cache
        Log.d(TAG, "Simulated compromised set to: $compromised")
    }

    override suspend fun checkIntegrity(): DeviceIntegrityVerdict {
        Log.d(TAG, "checkIntegrity() — mock check (compromised=$simulateCompromised)")
        delay(200) // Simulate brief check time

        val verdict = if (simulateCompromised) {
            DeviceIntegrityVerdict(
                isCompromised = true,
                findings = listOf(
                    IntegrityFinding(
                        check = IntegrityCheck.ROOT_SU_BINARY,
                        detected = true,
                        detail = "Mock: simulated root detection",
                        severity = FindingSeverity.CRITICAL
                    ),
                    IntegrityFinding(
                        check = IntegrityCheck.ROOT_MAGISK,
                        detected = true,
                        detail = "Mock: simulated Magisk detection",
                        severity = FindingSeverity.CRITICAL
                    ),
                    IntegrityFinding(
                        check = IntegrityCheck.EMULATOR_DETECTED,
                        detected = false,
                        detail = "Mock: emulator not detected"
                    )
                ),
                playIntegrityLevel = PlayIntegrityLevel.DOES_NOT_MEET_INTEGRITY,
                riskScore = 0.9f
            )
        } else {
            DeviceIntegrityVerdict(
                isCompromised = false,
                findings = IntegrityCheck.values().map { check ->
                    IntegrityFinding(
                        check = check,
                        detected = false,
                        detail = "Mock: all clear",
                        severity = FindingSeverity.LOW
                    )
                },
                playIntegrityLevel = PlayIntegrityLevel.MEETS_STRONG_INTEGRITY,
                riskScore = 0.0f
            )
        }

        cachedVerdict = verdict
        Log.i(TAG, "Verdict: compromised=${verdict.isCompromised}, risk=${verdict.riskScore}")
        return verdict
    }

    override suspend fun requestIntegrityToken(): String? {
        Log.d(TAG, "requestIntegrityToken() — mock returns fake token")
        delay(100)
        return "mock-integrity-token-${System.currentTimeMillis()}"
    }

    override fun getCachedVerdict(): DeviceIntegrityVerdict? = cachedVerdict
}
