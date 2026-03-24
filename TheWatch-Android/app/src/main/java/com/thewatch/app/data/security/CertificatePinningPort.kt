/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         CertificatePinningPort.kt                              │
 * │ Purpose:      Hexagonal port interface for TLS certificate pinning.  │
 * │               Defines the domain contract for creating pin-verified  │
 * │               OkHttpClients that reject MITM / rogue CA attacks.     │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: OkHttp 4.x                                             │
 * │                                                                      │
 * │ Adapter tiers:                                                       │
 * │   - Mock:   No-op pinning (allows all certs). Dev/test only.         │
 * │   - Native: OkHttp CertificatePinner with SHA-256 pins for          │
 * │             api.thewatch.app and related domains.                    │
 * │   - Live:   Native + dynamic pin rotation via remote config.         │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   val port: CertificatePinningPort = hiltGet()                       │
 * │   val client = port.createPinnedClient()                             │
 * │   val response = client.newCall(request).execute()                   │
 * │                                                                      │
 * │ NOTE: Certificate pinning MUST include backup pins. If all pins      │
 * │ expire or rotate simultaneously, the app becomes bricked. Always     │
 * │ pin at least 2 certificates (primary + backup CA intermediate).      │
 * │ Google Play requires apps using pinning to have a remote kill        │
 * │ switch or dynamic pin update mechanism.                              │
 * │                                                                      │
 * │ Related standards: OWASP MASVS-NETWORK-1, RFC 7469 (HPKP, now      │
 * │ deprecated for browsers but principle applies to mobile).            │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.security

import okhttp3.OkHttpClient

/**
 * Port interface for TLS certificate pinning — the domain contract.
 *
 * Provides a pre-configured [OkHttpClient] with certificate pins applied
 * for TheWatch API domains. All network calls MUST use the client returned
 * by [createPinnedClient] rather than a bare OkHttpClient.
 */
interface CertificatePinningPort {

    /**
     * Create an [OkHttpClient] with TLS certificate pins applied.
     *
     * The returned client should be cached/reused (OkHttpClient is designed
     * to be a singleton). Call [refreshPins] if remote pin rotation occurs.
     *
     * @return A configured OkHttpClient with CertificatePinner attached.
     */
    fun createPinnedClient(): OkHttpClient

    /**
     * Get the list of currently pinned domains and their SHA-256 hashes.
     * Useful for diagnostics and logging.
     *
     * @return Map of domain pattern to list of pin hashes.
     */
    fun getPinnedDomains(): Map<String, List<String>>

    /**
     * Refresh pins from remote configuration.
     * No-op for Mock and basic Native tiers.
     * Live tier fetches updated pins from a trusted config endpoint.
     *
     * @return true if pins were updated, false if no change or not supported.
     */
    suspend fun refreshPins(): Boolean

    /**
     * Validate that a given domain's certificate chain matches our pins.
     * Used for pre-flight checks before critical API calls (SOS dispatch, etc.).
     *
     * @param domain The domain to validate (e.g., "api.thewatch.app").
     * @return [PinValidationResult] indicating match/mismatch.
     */
    suspend fun validatePins(domain: String): PinValidationResult
}

/**
 * Result of a certificate pin validation check.
 */
sealed class PinValidationResult {
    /** Pins match — connection is trusted. */
    object Valid : PinValidationResult()
    /** Pin mismatch — possible MITM attack. */
    data class PinMismatch(val expectedPins: List<String>, val actualHash: String?) : PinValidationResult()
    /** Could not connect to validate (offline, DNS failure, etc.). */
    data class ConnectionFailed(val reason: String) : PinValidationResult()
    /** Domain is not in the pinning configuration. */
    object NotPinned : PinValidationResult()
}
