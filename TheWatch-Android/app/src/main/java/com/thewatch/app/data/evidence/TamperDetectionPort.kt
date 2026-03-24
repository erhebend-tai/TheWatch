/**
 * ┌──────────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                         │
 * │ File:    TamperDetectionPort.kt                                         │
 * │ Purpose: Hexagonal port for chain-of-custody tamper detection.           │
 * │          Provides SHA-256 hash chain computation and verification        │
 * │          for evidence items, ensuring forensic integrity.               │
 * │ Date:    2026-03-24                                                     │
 * │ Author:  Claude                                                         │
 * │ Deps:    Evidence model, java.security.MessageDigest                    │
 * │                                                                         │
 * │ Usage Example:                                                          │
 * │   val port: TamperDetectionPort = // injected via Hilt                  │
 * │   val hash = port.computeHash(fileBytes, "GENESIS", timestamp)          │
 * │   val result = port.verifyItem(evidence, fileBytes)                     │
 * │   val chainOk = port.verifyChain(evidenceList)                          │
 * │                                                                         │
 * │ Hash Chain Scheme:                                                      │
 * │   Block N hash = SHA-256( contentBytes || previousHash || timestamp )   │
 * │   Genesis block uses previousHash = "GENESIS"                           │
 * │   Any modification to content, ordering, or timestamp breaks chain.     │
 * │                                                                         │
 * │ Standards Reference:                                                    │
 * │   - NIST FIPS 180-4 (SHA-256 specification)                             │
 * │   - ISO/IEC 27037:2012 Section 7 (Digital evidence integrity)           │
 * │   - RFC 6234 (SHA-256 test vectors)                                     │
 * │   - SWGDE Best Practices for Maintaining Integrity of Digital Evidence  │
 * │                                                                         │
 * │ Possible Future Extensions:                                             │
 * │   - HMAC-SHA256 with device-bound key for source authentication         │
 * │   - Merkle tree for efficient partial verification                      │
 * │   - Timestamp authority (RFC 3161) for third-party timestamping         │
 * │   - Blockchain anchoring (Bitcoin/Ethereum OP_RETURN)                   │
 * └──────────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.evidence

import com.thewatch.app.data.model.Evidence

/**
 * Verification result for a single evidence item or an entire chain.
 */
data class VerificationResult(
    /** Whether the verification passed */
    val isValid: Boolean,

    /** Human-readable summary */
    val message: String,

    /** If invalid, the ID of the first tampered item (null if valid) */
    val tamperedItemId: String? = null,

    /** Number of items verified */
    val itemsVerified: Int = 0,

    /** Timestamp of verification */
    val verifiedAt: Long = System.currentTimeMillis()
)

/**
 * Port interface for tamper detection and chain-of-custody verification.
 *
 * Separated from EvidencePort so that verification logic can be tested
 * independently and swapped between mock/native/hardware-backed implementations.
 */
interface TamperDetectionPort {

    /**
     * Compute SHA-256 hash for evidence content within the chain.
     *
     * Formula: SHA-256( contentBytes + previousHash.toByteArray() + timestamp.toString().toByteArray() )
     *
     * @param contentBytes Raw file bytes (photo JPEG, video MP4, audio M4A, or sitrep text UTF-8)
     * @param previousHash Hex-encoded hash of the previous item ("GENESIS" for first)
     * @param timestamp Capture timestamp as epoch millis
     * @return Hex-encoded SHA-256 hash
     */
    suspend fun computeHash(
        contentBytes: ByteArray,
        previousHash: String,
        timestamp: Long
    ): String

    /**
     * Verify a single evidence item's hash against its content.
     *
     * @param evidence The evidence item to verify
     * @param contentBytes The raw file bytes to hash
     * @return VerificationResult indicating pass/fail
     */
    suspend fun verifyItem(
        evidence: Evidence,
        contentBytes: ByteArray
    ): VerificationResult

    /**
     * Verify the entire hash chain for a list of evidence items.
     * Items must be in timestamp order (ascending).
     * Each item's previousHash must match the preceding item's hash.
     *
     * @param chain Evidence items in chain order
     * @param contentProvider Lambda to fetch file bytes for each evidence ID
     * @return VerificationResult for the entire chain
     */
    suspend fun verifyChain(
        chain: List<Evidence>,
        contentProvider: suspend (evidenceId: String) -> ByteArray?
    ): VerificationResult

    /**
     * Get the last hash in the chain for a given incident.
     * Used as previousHash when appending new evidence.
     *
     * @param chain Current chain of evidence items (may be empty)
     * @return The hash of the last item, or "GENESIS" if chain is empty
     */
    fun getLastHash(chain: List<Evidence>): String
}
