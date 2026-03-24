/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         BiometricAuthPort.kt                                   │
 * │ Purpose:      Hexagonal port interface for biometric authentication. │
 * │               Defines the domain contract for fingerprint/face auth  │
 * │               that all adapter tiers (Mock, Native, Live) implement. │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: FragmentActivity (for BiometricPrompt host)            │
 * │                                                                      │
 * │ Adapter tiers:                                                       │
 * │   - Mock:   Always succeeds after simulated delay. Dev/test.         │
 * │   - Native: Uses androidx.biometric.BiometricPrompt on-device.       │
 * │   - Live:   Native + server-side token exchange (future).            │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   val port: BiometricAuthPort = hiltGet()                            │
 * │   if (port.isAvailable()) {                                          │
 * │       val result = port.authenticate(activity)                       │
 * │       if (result.isSuccess) navigateHome()                           │
 * │   }                                                                  │
 * │                                                                      │
 * │ NOTE: BiometricPrompt requires a FragmentActivity host. Compose      │
 * │ screens obtain it via LocalContext.current as FragmentActivity.       │
 * │ Some OEMs (Samsung Knox, Huawei EMUI) expose additional biometric    │
 * │ types — the native adapter should handle BIOMETRIC_STRONG only.      │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.security

import androidx.fragment.app.FragmentActivity

/**
 * Port interface for biometric authentication — the domain contract.
 *
 * Three-tier implementations:
 * - **Mock**: Simulated success/failure for development and UI testing.
 * - **Native**: Real on-device biometric via androidx.biometric.BiometricPrompt.
 * - **Live**: Native biometric + server-side session token exchange (future).
 *
 * All implementations MUST be thread-safe and safe to call from any coroutine context.
 */
interface BiometricAuthPort {

    /**
     * Check whether the device supports strong biometric authentication
     * (fingerprint, face, iris) AND the user has enrolled at least one biometric.
     *
     * @return [BiometricAvailability] indicating hardware + enrollment status.
     */
    suspend fun checkAvailability(): BiometricAvailability

    /**
     * Convenience: true when [checkAvailability] returns [BiometricAvailability.Available].
     */
    suspend fun isAvailable(): Boolean = checkAvailability() == BiometricAvailability.Available

    /**
     * Prompt the user for biometric authentication.
     *
     * @param activity The hosting FragmentActivity (required by BiometricPrompt).
     * @param title    Dialog title shown to the user. Default "Authenticate".
     * @param subtitle Optional subtitle. Default null.
     * @param negativeButtonText Text for the cancel/fallback button. Default "Use Password".
     * @return [BiometricResult] indicating success, failure, or cancellation.
     */
    suspend fun authenticate(
        activity: FragmentActivity,
        title: String = "Authenticate",
        subtitle: String? = null,
        negativeButtonText: String = "Use Password"
    ): BiometricResult

    /**
     * Invalidate any cached biometric session tokens.
     * Called on logout or session expiry.
     */
    suspend fun invalidateSession()
}

/**
 * Hardware + enrollment status for biometric authentication.
 */
enum class BiometricAvailability {
    /** Device has biometric hardware AND user has enrolled at least one biometric. */
    Available,
    /** Device has hardware but no biometrics enrolled (prompt user to enroll). */
    NoneEnrolled,
    /** Device lacks biometric hardware entirely. */
    NoHardware,
    /** Biometric hardware is temporarily unavailable (e.g., too many attempts). */
    HardwareUnavailable,
    /** Security vulnerability detected — biometric should not be trusted. */
    SecurityUpdateRequired
}

/**
 * Result of a biometric authentication attempt.
 */
sealed class BiometricResult {
    /** Authentication succeeded. [cryptoObject] available if crypto-bound. */
    data class Success(val cryptoObject: Any? = null) : BiometricResult()
    /** Authentication failed (wrong finger, face not recognized, etc.). */
    data class Failed(val errorCode: Int, val message: String) : BiometricResult()
    /** User explicitly cancelled the biometric prompt. */
    object Cancelled : BiometricResult()
    /** User chose the negative button (e.g., "Use Password"). */
    object NegativeButtonPressed : BiometricResult()
    /** Too many failed attempts — device lockout active. */
    data class Lockout(val isPermanent: Boolean) : BiometricResult()
}
