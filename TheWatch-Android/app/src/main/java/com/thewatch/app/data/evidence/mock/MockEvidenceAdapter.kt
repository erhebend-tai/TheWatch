/**
 * ┌──────────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                         │
 * │ File:    MockEvidenceAdapter.kt                                         │
 * │ Purpose: In-memory mock implementation of EvidencePort for dev/testing. │
 * │          Simulates photo/video/audio capture and sitrep submission       │
 * │          without requiring actual camera, microphone, or filesystem.     │
 * │          Computes real SHA-256 hashes for chain-of-custody testing.      │
 * │ Date:    2026-03-24                                                     │
 * │ Author:  Claude                                                         │
 * │ Deps:    EvidencePort, Evidence model, java.security.MessageDigest      │
 * │                                                                         │
 * │ Usage Example:                                                          │
 * │   // Wired automatically via Hilt AppModule in dev builds               │
 * │   val adapter = MockEvidenceAdapter()                                   │
 * │   val result = adapter.capturePhoto("inc-001", 30.27, -97.74, "note")  │
 * │   val evidence = result.getOrThrow()                                    │
 * │   println(evidence.hash) // real SHA-256                                │
 * │   println(adapter.verifyChain("inc-001")) // true                       │
 * │                                                                         │
 * │ Notes:                                                                  │
 * │   - File paths are synthetic (no actual files on disk)                  │
 * │   - Hash computation is real SHA-256 (validates chain logic)            │
 * │   - Thread-safe via ConcurrentHashMap                                   │
 * └──────────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.evidence.mock

import android.util.Log
import com.thewatch.app.data.evidence.EvidencePort
import com.thewatch.app.data.model.Evidence
import com.thewatch.app.data.model.EvidenceType
import com.thewatch.app.data.model.SitrepSeverity
import com.thewatch.app.data.model.SituationType
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.map
import java.security.MessageDigest
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import javax.inject.Inject
import javax.inject.Singleton

@Singleton
class MockEvidenceAdapter @Inject constructor() : EvidencePort {

    companion object {
        private const val TAG = "TheWatch.MockEvidence"
        private const val GENESIS_HASH = "GENESIS"
    }

    /** In-memory store: incidentId -> list of evidence items */
    private val store = ConcurrentHashMap<String, MutableList<Evidence>>()

    /** Reactive flow for all evidence across incidents */
    private val allEvidenceFlow = MutableStateFlow<Map<String, List<Evidence>>>(emptyMap())

    // ── Capture Operations ────────────────────────────────────────────────

    override suspend fun capturePhoto(
        incidentId: String,
        latitude: Double,
        longitude: Double,
        annotation: String?
    ): Result<Evidence> {
        return try {
            val id = UUID.randomUUID().toString()
            val timestamp = System.currentTimeMillis()
            val chain = store.getOrDefault(incidentId, mutableListOf())
            val previousHash = if (chain.isEmpty()) GENESIS_HASH else chain.last().hash

            // Mock content: synthetic bytes representing a photo
            val mockContent = "MOCK_PHOTO_${id}_${timestamp}".toByteArray()
            val hash = computeHash(mockContent, previousHash, timestamp)

            val evidence = Evidence(
                id = id,
                incidentId = incidentId,
                type = EvidenceType.PHOTO,
                filePath = "/mock/evidence/photos/${id}.jpg",
                thumbnailPath = "/mock/evidence/thumbs/${id}_thumb.jpg",
                hash = hash,
                previousHash = previousHash,
                timestamp = timestamp,
                latitude = latitude,
                longitude = longitude,
                metadata = mapOf(
                    "camera" to "rear",
                    "flash" to "auto",
                    "resolution" to "4032x3024",
                    "mock" to "true"
                ),
                submittedBy = "mock-user-001",
                verified = true,
                annotation = annotation,
                fileSizeBytes = 2_500_000L,
                mimeType = "image/jpeg"
            )

            addToStore(incidentId, evidence)
            Log.i(TAG, "capturePhoto: id=$id, incident=$incidentId, hash=${hash.take(16)}...")
            Result.success(evidence)
        } catch (e: Exception) {
            Log.e(TAG, "capturePhoto failed", e)
            Result.failure(e)
        }
    }

    override suspend fun captureVideo(
        incidentId: String,
        latitude: Double,
        longitude: Double,
        maxDurationMillis: Long
    ): Result<Evidence> {
        return try {
            val id = UUID.randomUUID().toString()
            val timestamp = System.currentTimeMillis()
            val chain = store.getOrDefault(incidentId, mutableListOf())
            val previousHash = if (chain.isEmpty()) GENESIS_HASH else chain.last().hash

            val mockContent = "MOCK_VIDEO_${id}_${timestamp}".toByteArray()
            val hash = computeHash(mockContent, previousHash, timestamp)

            // Simulate recording duration (capped at maxDurationMillis)
            val recordedDuration = minOf(maxDurationMillis, 30_000L)

            val evidence = Evidence(
                id = id,
                incidentId = incidentId,
                type = EvidenceType.VIDEO,
                filePath = "/mock/evidence/videos/${id}.mp4",
                thumbnailPath = "/mock/evidence/thumbs/${id}_thumb.jpg",
                hash = hash,
                previousHash = previousHash,
                timestamp = timestamp,
                latitude = latitude,
                longitude = longitude,
                metadata = mapOf(
                    "codec" to "H.264",
                    "resolution" to "1920x1080",
                    "fps" to "30",
                    "mock" to "true"
                ),
                submittedBy = "mock-user-001",
                verified = true,
                fileSizeBytes = 15_000_000L,
                mimeType = "video/mp4",
                durationMillis = recordedDuration
            )

            addToStore(incidentId, evidence)
            Log.i(TAG, "captureVideo: id=$id, duration=${recordedDuration}ms, incident=$incidentId")
            Result.success(evidence)
        } catch (e: Exception) {
            Log.e(TAG, "captureVideo failed", e)
            Result.failure(e)
        }
    }

    override suspend fun recordAudio(
        incidentId: String,
        latitude: Double,
        longitude: Double
    ): Result<Evidence> {
        return try {
            val id = UUID.randomUUID().toString()
            val timestamp = System.currentTimeMillis()
            val chain = store.getOrDefault(incidentId, mutableListOf())
            val previousHash = if (chain.isEmpty()) GENESIS_HASH else chain.last().hash

            val mockContent = "MOCK_AUDIO_${id}_${timestamp}".toByteArray()
            val hash = computeHash(mockContent, previousHash, timestamp)

            val evidence = Evidence(
                id = id,
                incidentId = incidentId,
                type = EvidenceType.AUDIO,
                filePath = "/mock/evidence/audio/${id}.m4a",
                thumbnailPath = "/mock/evidence/thumbs/${id}_waveform.png",
                hash = hash,
                previousHash = previousHash,
                timestamp = timestamp,
                latitude = latitude,
                longitude = longitude,
                metadata = mapOf(
                    "sampleRate" to "44100",
                    "channels" to "1",
                    "codec" to "AAC",
                    "transcription" to "[Auto-transcription placeholder: mock audio content]",
                    "mock" to "true"
                ),
                submittedBy = "mock-user-001",
                verified = true,
                fileSizeBytes = 500_000L,
                mimeType = "audio/mp4",
                durationMillis = 15_000L
            )

            addToStore(incidentId, evidence)
            Log.i(TAG, "recordAudio: id=$id, incident=$incidentId")
            Result.success(evidence)
        } catch (e: Exception) {
            Log.e(TAG, "recordAudio failed", e)
            Result.failure(e)
        }
    }

    override suspend fun submitSitrep(
        incidentId: String,
        situationType: SituationType,
        severity: SitrepSeverity,
        description: String,
        attachedEvidenceIds: List<String>,
        latitude: Double?,
        longitude: Double?
    ): Result<Evidence> {
        return try {
            val id = UUID.randomUUID().toString()
            val timestamp = System.currentTimeMillis()
            val chain = store.getOrDefault(incidentId, mutableListOf())
            val previousHash = if (chain.isEmpty()) GENESIS_HASH else chain.last().hash

            val sitrepContent = "SITREP|$situationType|$severity|$description".toByteArray()
            val hash = computeHash(sitrepContent, previousHash, timestamp)

            val evidence = Evidence(
                id = id,
                incidentId = incidentId,
                type = EvidenceType.SITREP,
                filePath = null,
                thumbnailPath = null,
                hash = hash,
                previousHash = previousHash,
                timestamp = timestamp,
                latitude = latitude,
                longitude = longitude,
                metadata = mapOf(
                    "situationType" to situationType.name,
                    "severity" to severity.name,
                    "attachmentCount" to attachedEvidenceIds.size.toString(),
                    "mock" to "true"
                ),
                submittedBy = "mock-user-001",
                verified = true,
                annotation = description,
                situationType = situationType,
                severity = severity,
                attachedEvidenceIds = attachedEvidenceIds,
                fileSizeBytes = description.toByteArray().size.toLong(),
                mimeType = "text/plain"
            )

            addToStore(incidentId, evidence)
            Log.i(TAG, "submitSitrep: id=$id, type=$situationType, severity=$severity")
            Result.success(evidence)
        } catch (e: Exception) {
            Log.e(TAG, "submitSitrep failed", e)
            Result.failure(e)
        }
    }

    // ── Retrieval Operations ──────────────────────────────────────────────

    override fun getEvidenceForIncident(incidentId: String): Flow<List<Evidence>> {
        return allEvidenceFlow.map { map ->
            map[incidentId]?.sortedBy { it.timestamp } ?: emptyList()
        }
    }

    override suspend fun getEvidenceById(evidenceId: String): Evidence? {
        return store.values.flatten().find { it.id == evidenceId }
    }

    override suspend fun getEvidenceByType(
        incidentId: String,
        type: EvidenceType
    ): List<Evidence> {
        return store.getOrDefault(incidentId, mutableListOf())
            .filter { it.type == type }
            .sortedBy { it.timestamp }
    }

    // ── Hash / Chain-of-Custody ───────────────────────────────────────────

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

    override suspend fun verifyChain(incidentId: String): Boolean {
        val chain = store.getOrDefault(incidentId, mutableListOf())
            .sortedBy { it.timestamp }

        if (chain.isEmpty()) return true

        var expectedPreviousHash = GENESIS_HASH
        for (evidence in chain) {
            if (evidence.previousHash != expectedPreviousHash) {
                Log.w(TAG, "verifyChain: broken link at ${evidence.id}, " +
                    "expected previousHash=$expectedPreviousHash, got=${evidence.previousHash}")
                return false
            }
            expectedPreviousHash = evidence.hash
        }

        Log.i(TAG, "verifyChain: incident=$incidentId, ${chain.size} items verified OK")
        return true
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────

    override suspend fun deleteEvidence(evidenceId: String): Result<Unit> {
        return try {
            var found = false
            store.forEach { (incidentId, list) ->
                if (list.removeIf { it.id == evidenceId }) {
                    found = true
                    Log.i(TAG, "deleteEvidence: removed $evidenceId from incident $incidentId")
                }
            }
            emitUpdate()
            if (found) Result.success(Unit) else Result.failure(NoSuchElementException("Evidence $evidenceId not found"))
        } catch (e: Exception) {
            Result.failure(e)
        }
    }

    override suspend fun getStorageUsed(incidentId: String): Long {
        return store.getOrDefault(incidentId, mutableListOf())
            .sumOf { it.fileSizeBytes }
    }

    // ── Internal Helpers ──────────────────────────────────────────────────

    private fun addToStore(incidentId: String, evidence: Evidence) {
        store.getOrPut(incidentId) { mutableListOf() }.add(evidence)
        emitUpdate()
    }

    private fun emitUpdate() {
        allEvidenceFlow.value = store.mapValues { it.value.toList() }
    }

    /** Expose all stored evidence for testing. */
    fun allEvidence(): Map<String, List<Evidence>> = store.mapValues { it.value.toList() }

    /** Clear all stored evidence for testing. */
    fun clear() {
        store.clear()
        emitUpdate()
    }
}
