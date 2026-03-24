/**
 * ┌──────────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                         │
 * │ File:    MockTamperDetectionAdapter.kt                                  │
 * │ Purpose: Mock adapter for TamperDetectionPort. Uses real SHA-256        │
 * │          computation so chain logic is properly testable even in mock.   │
 * │ Date:    2026-03-24                                                     │
 * │ Author:  Claude                                                         │
 * │ Deps:    TamperDetectionPort, Evidence model, java.security             │
 * │                                                                         │
 * │ Usage Example:                                                          │
 * │   val adapter = MockTamperDetectionAdapter()                            │
 * │   val hash = adapter.computeHash("hello".toByteArray(), "GENESIS", ts) │
 * │   val result = adapter.verifyItem(evidence, contentBytes)               │
 * │   println(result.isValid) // true if hash matches                       │
 * └──────────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.evidence.mock

import android.util.Log
import com.thewatch.app.data.evidence.TamperDetectionPort
import com.thewatch.app.data.evidence.VerificationResult
import com.thewatch.app.data.model.Evidence
import java.security.MessageDigest
import javax.inject.Inject
import javax.inject.Singleton

@Singleton
class MockTamperDetectionAdapter @Inject constructor() : TamperDetectionPort {

    companion object {
        private const val TAG = "TheWatch.MockTamper"
        private const val GENESIS_HASH = "GENESIS"
    }

    override suspend fun computeHash(
        contentBytes: ByteArray,
        previousHash: String,
        timestamp: Long
    ): String {
        val digest = MessageDigest.getInstance("SHA-256")
        digest.update(contentBytes)
        digest.update(previousHash.toByteArray(Charsets.UTF_8))
        digest.update(timestamp.toString().toByteArray(Charsets.UTF_8))
        return digest.digest().joinToString("") { "%02x".format(it) }
    }

    override suspend fun verifyItem(
        evidence: Evidence,
        contentBytes: ByteArray
    ): VerificationResult {
        val recomputed = computeHash(contentBytes, evidence.previousHash, evidence.timestamp)
        val isValid = recomputed == evidence.hash

        Log.d(TAG, "verifyItem: id=${evidence.id}, valid=$isValid, " +
            "stored=${evidence.hash.take(16)}, computed=${recomputed.take(16)}")

        return VerificationResult(
            isValid = isValid,
            message = if (isValid) "Evidence ${evidence.id} integrity verified" else
                "TAMPERED: Evidence ${evidence.id} hash mismatch (stored=${evidence.hash.take(16)}, computed=${recomputed.take(16)})",
            tamperedItemId = if (!isValid) evidence.id else null,
            itemsVerified = 1
        )
    }

    override suspend fun verifyChain(
        chain: List<Evidence>,
        contentProvider: suspend (evidenceId: String) -> ByteArray?
    ): VerificationResult {
        if (chain.isEmpty()) {
            return VerificationResult(
                isValid = true,
                message = "Empty chain — nothing to verify",
                itemsVerified = 0
            )
        }

        val sorted = chain.sortedBy { it.timestamp }
        var expectedPreviousHash = GENESIS_HASH

        for ((index, evidence) in sorted.withIndex()) {
            // Check chain linkage
            if (evidence.previousHash != expectedPreviousHash) {
                Log.w(TAG, "verifyChain: broken link at index $index, id=${evidence.id}")
                return VerificationResult(
                    isValid = false,
                    message = "Chain broken at item ${evidence.id}: expected previousHash=$expectedPreviousHash, got=${evidence.previousHash}",
                    tamperedItemId = evidence.id,
                    itemsVerified = index
                )
            }

            // Check content hash (if content available)
            val content = contentProvider(evidence.id)
            if (content != null) {
                val recomputed = computeHash(content, evidence.previousHash, evidence.timestamp)
                if (recomputed != evidence.hash) {
                    Log.w(TAG, "verifyChain: content hash mismatch at index $index, id=${evidence.id}")
                    return VerificationResult(
                        isValid = false,
                        message = "Content tampered at item ${evidence.id}: hash mismatch",
                        tamperedItemId = evidence.id,
                        itemsVerified = index
                    )
                }
            }

            expectedPreviousHash = evidence.hash
        }

        Log.i(TAG, "verifyChain: ${sorted.size} items verified OK")
        return VerificationResult(
            isValid = true,
            message = "Chain of ${sorted.size} items verified — integrity intact",
            itemsVerified = sorted.size
        )
    }

    override fun getLastHash(chain: List<Evidence>): String {
        return if (chain.isEmpty()) GENESIS_HASH
        else chain.sortedBy { it.timestamp }.last().hash
    }
}
