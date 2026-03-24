/**
 * ┌──────────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                         │
 * │ File:    MockThumbnailGeneratorAdapter.kt                               │
 * │ Purpose: Mock adapter for ThumbnailGeneratorPort. Returns synthetic     │
 * │          thumbnail paths without generating actual bitmap files.         │
 * │          Tracks cache state in memory for testing.                       │
 * │ Date:    2026-03-24                                                     │
 * │ Author:  Claude                                                         │
 * │ Deps:    ThumbnailGeneratorPort, Evidence model                         │
 * │                                                                         │
 * │ Usage Example:                                                          │
 * │   val adapter = MockThumbnailGeneratorAdapter()                         │
 * │   val path = adapter.generateThumbnail(evidence)                        │
 * │   println(path) // "/mock/thumbs/{id}_thumb.jpg"                        │
 * │   println(adapter.hasCachedThumbnail(evidence.id)) // true              │
 * └──────────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.evidence.mock

import android.util.Log
import com.thewatch.app.data.evidence.ThumbnailGeneratorPort
import com.thewatch.app.data.model.Evidence
import com.thewatch.app.data.model.EvidenceType
import java.util.concurrent.ConcurrentHashMap
import javax.inject.Inject
import javax.inject.Singleton

@Singleton
class MockThumbnailGeneratorAdapter @Inject constructor() : ThumbnailGeneratorPort {

    companion object {
        private const val TAG = "TheWatch.MockThumb"
        private const val MOCK_THUMB_SIZE = 15_000L // ~15KB per mock thumbnail
    }

    /** Cache: evidenceId -> thumbnailPath */
    private val cache = ConcurrentHashMap<String, String>()

    /** Track which incident each evidence belongs to for clearCache */
    private val evidenceToIncident = ConcurrentHashMap<String, String>()

    override suspend fun generateThumbnail(evidence: Evidence): String? {
        return when (evidence.type) {
            EvidenceType.PHOTO -> {
                val path = generatePhotoThumbnail(
                    evidence.filePath ?: return null,
                    evidence.id
                )
                evidenceToIncident[evidence.id] = evidence.incidentId
                path
            }
            EvidenceType.VIDEO -> {
                val path = generateVideoThumbnail(
                    evidence.filePath ?: return null,
                    evidence.id
                )
                evidenceToIncident[evidence.id] = evidence.incidentId
                path
            }
            EvidenceType.AUDIO -> {
                val path = generateAudioThumbnail(
                    evidence.filePath ?: return null,
                    evidence.id
                )
                evidenceToIncident[evidence.id] = evidence.incidentId
                path
            }
            EvidenceType.SITREP -> {
                Log.d(TAG, "SITREP type — no thumbnail generated for ${evidence.id}")
                null
            }
        }
    }

    override suspend fun generatePhotoThumbnail(sourcePath: String, evidenceId: String): String {
        val thumbPath = "/mock/thumbs/${evidenceId}_photo_200x200.jpg"
        cache[evidenceId] = thumbPath
        Log.d(TAG, "generatePhotoThumbnail: $evidenceId -> $thumbPath")
        return thumbPath
    }

    override suspend fun generateVideoThumbnail(sourcePath: String, evidenceId: String): String {
        val thumbPath = "/mock/thumbs/${evidenceId}_video_frame0_200x200.jpg"
        cache[evidenceId] = thumbPath
        Log.d(TAG, "generateVideoThumbnail: $evidenceId -> $thumbPath")
        return thumbPath
    }

    override suspend fun generateAudioThumbnail(sourcePath: String, evidenceId: String): String {
        val thumbPath = "/mock/thumbs/${evidenceId}_waveform_200x200.png"
        cache[evidenceId] = thumbPath
        Log.d(TAG, "generateAudioThumbnail: $evidenceId -> $thumbPath")
        return thumbPath
    }

    override suspend fun hasCachedThumbnail(evidenceId: String): Boolean {
        return cache.containsKey(evidenceId)
    }

    override suspend fun getCachedThumbnailPath(evidenceId: String): String? {
        return cache[evidenceId]
    }

    override suspend fun clearCache(incidentId: String): Int {
        val toRemove = evidenceToIncident.filter { it.value == incidentId }.keys
        var count = 0
        toRemove.forEach { id ->
            cache.remove(id)
            evidenceToIncident.remove(id)
            count++
        }
        Log.i(TAG, "clearCache: removed $count thumbnails for incident $incidentId")
        return count
    }

    override suspend fun clearAllCaches(): Long {
        val count = cache.size
        val freedBytes = count * MOCK_THUMB_SIZE
        cache.clear()
        evidenceToIncident.clear()
        Log.i(TAG, "clearAllCaches: removed $count thumbnails, freed ~${freedBytes} bytes")
        return freedBytes
    }

    override suspend fun getCacheSize(): Long {
        return cache.size * MOCK_THUMB_SIZE
    }
}
