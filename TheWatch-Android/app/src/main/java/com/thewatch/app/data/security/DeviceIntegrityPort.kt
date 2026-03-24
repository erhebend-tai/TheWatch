/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         DeviceIntegrityPort.kt                                 │
 * │ Purpose:      Hexagonal port interface for device integrity checks.  │
 * │               Detects rooted/jailbroken devices, bootloader unlock,  │
 * │               running in emulator, Magisk Hide, Xposed Framework,    │
 * │               and other tampering indicators. Critical for a safety  │
 * │               app — compromised devices cannot be trusted for SOS.   │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: Google Play Integrity API (native tier)                │
 * │                                                                      │
 * │ Adapter tiers:                                                       │
 * │   - Mock:   Always returns "device is clean". Dev/emulator use.      │
 * │   - Native: Local heuristic checks (su binary, test-keys, etc.)     │
 * │             + Google Play Integrity API attestation.                 │
 * │   - Live:   Native + server-side verdict verification + risk score. │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   val port: DeviceIntegrityPort = hiltGet()                          │
 * │   val verdict = port.checkIntegrity()                                │
 * │   if (verdict.isCompromised) {                                       │
 * │       showSecurityWarning(verdict.findings)                          │
 * │   }                                                                  │
 * │                                                                      │
 * │ NOTE: Root detection is an arms race. Magisk Hide can bypass many    │
 * │ checks. The Play Integrity API is the most reliable signal but       │
 * │ requires Google Play Services and network connectivity. Always       │
 * │ combine multiple signals for a composite verdict.                    │
 * │                                                                      │
 * │ Related: SafetyNet Attestation (deprecated), Play Integrity API,    │
 * │ OWASP MASVS-RESILIENCE-1 through RESILIENCE-4.                     │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.security

/**
 * Port interface for device integrity verification — the domain contract.
 *
 * Checks whether the device is in a trusted state (not rooted, not emulated,
 * bootloader locked, no tampering frameworks installed). A safety-critical
 * app like TheWatch must warn users on compromised devices.
 */
interface DeviceIntegrityPort {

    /**
     * Perform a comprehensive device integrity check.
     *
     * Implementations should check (as applicable):
     * - Root/su binary presence
     * - SELinux permissive mode
     * - Test-keys build signature
     * - Magisk / Xposed / EdXposed / LSPosed framework indicators
     * - Bootloader unlock status
     * - Running in emulator (QEMU, Genymotion, BlueStacks)
     * - Debugger attached
     * - App signature tampering
     * - Google Play Integrity API token (native/live tiers)
     *
     * @return [DeviceIntegrityVerdict] with composite findings.
     */
    suspend fun checkIntegrity(): DeviceIntegrityVerdict

    /**
     * Quick check: is the device likely compromised?
     * Convenience wrapper around [checkIntegrity].
     */
    suspend fun isDeviceCompromised(): Boolean = checkIntegrity().isCompromised

    /**
     * Request a Play Integrity API token for server-side verification.
     * Returns null if Play Services unavailable or network error.
     *
     * @return Base64-encoded integrity token, or null on failure.
     */
    suspend fun requestIntegrityToken(): String?

    /**
     * Get the last cached integrity verdict without performing a new check.
     * Returns null if no check has been performed yet.
     */
    fun getCachedVerdict(): DeviceIntegrityVerdict?
}

/**
 * Comprehensive device integrity verdict with individual findings.
 */
data class DeviceIntegrityVerdict(
    /** Composite flag: true if ANY integrity check failed. */
    val isCompromised: Boolean,

    /** Individual findings from each check. */
    val findings: List<IntegrityFinding>,

    /** Play Integrity API device recognition level, if available. */
    val playIntegrityLevel: PlayIntegrityLevel? = null,

    /** Timestamp of this verdict (epoch millis). */
    val checkedAt: Long = System.currentTimeMillis(),

    /** Overall risk score 0.0 (clean) to 1.0 (definitely compromised). */
    val riskScore: Float = if (isCompromised) 1.0f else 0.0f
)

/**
 * Individual integrity finding from a specific check.
 */
data class IntegrityFinding(
    /** The type of check that produced this finding. */
    val check: IntegrityCheck,
    /** Whether this check detected a problem. */
    val detected: Boolean,
    /** Human-readable detail about the finding. */
    val detail: String? = null,
    /** Severity: how concerning this finding is. */
    val severity: FindingSeverity = FindingSeverity.MEDIUM
)

/**
 * Types of integrity checks performed.
 */
enum class IntegrityCheck {
    ROOT_SU_BINARY,
    ROOT_SUPERUSER_APK,
    ROOT_MAGISK,
    ROOT_XPOSED,
    ROOT_BUSYBOX,
    SELINUX_PERMISSIVE,
    TEST_KEYS,
    BOOTLOADER_UNLOCKED,
    EMULATOR_DETECTED,
    DEBUGGER_ATTACHED,
    APP_SIGNATURE_TAMPERED,
    PLAY_INTEGRITY_FAILED,
    DEVELOPER_OPTIONS_ENABLED,
    USB_DEBUGGING_ENABLED,
    MOCK_LOCATION_ENABLED
}

/**
 * Severity of an integrity finding.
 */
enum class FindingSeverity {
    /** Informational — not necessarily a threat (e.g., developer options). */
    LOW,
    /** Moderate concern — may indicate tampering. */
    MEDIUM,
    /** High concern — strong indicator of compromise. */
    HIGH,
    /** Critical — device should not be trusted for safety features. */
    CRITICAL
}

/**
 * Google Play Integrity API device recognition levels.
 */
enum class PlayIntegrityLevel {
    /** Device passes all integrity checks (genuine, non-rooted). */
    MEETS_DEVICE_INTEGRITY,
    /** Device passes basic integrity but may lack strong integrity. */
    MEETS_BASIC_INTEGRITY,
    /** Device passes strong integrity (hardware-backed attestation). */
    MEETS_STRONG_INTEGRITY,
    /** Device failed integrity checks entirely. */
    DOES_NOT_MEET_INTEGRITY,
    /** Play Integrity API unavailable (no Google Play Services). */
    UNAVAILABLE
}
