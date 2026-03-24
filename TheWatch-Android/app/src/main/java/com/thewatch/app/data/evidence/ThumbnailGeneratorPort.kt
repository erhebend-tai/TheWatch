/**
 * ┌──────────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                         │
 * │ File:    ThumbnailGeneratorPort.kt                                      │
 * │ Purpose: Hexagonal port for generating preview thumbnails of evidence   │
 * │          items. Photos get 200x200 center-crop, videos get first-frame  │
 * │          extraction, audio gets waveform visualization.                 │
 * │ Date:    2026-03-24                                                     │
 * │ Author:  Claude                                                         │
 * │ Deps:    Evidence model, android.graphics.Bitmap                        │
 * │                                                                         │
 * │ Usage Example:                                                          │
 * │   val port: ThumbnailGeneratorPort = // injected via Hilt               │
 * │   val thumbPath = port.generateThumbnail(evidence)                      │
 * │   val bitmap = port.loadThumbnail(thumbPath)                            │
 * │   port.clearCache(incidentId)                                           │
 * │                                                                         │
 * │ Thumbnail Specs:                                                        │
 * │   - Photo: 200x200 JPEG, center-crop, quality 80                       │
 * │   - Video: 200x200 JPEG, first frame via MediaMetadataRetriever        │
 * │   - Audio: 200x200 PNG, waveform visualization (amplitude bars)        │
 * │   - SITREP: No thumbnail (text-only, use icon in UI)                   │
 * │                                                                         │
 * │ Caching Strategy:                                                       │
 * │   - Thumbnails stored in app-specific cache dir                         │
 * │   - Named by evidence ID for O(1) lookup                               │
 * │   - Cleared per-incident or on cache pressure                           │
 * │                                                                         │
 * │ Possible Future Extensions:                                             │
 * │   - Coil/Glide integration for memory-level caching                     │
 * │   - WebP output for smaller file size                                   │
 * │   - Adaptive thumbnail sizes for different screen densities             │
 * │   - Video thumbnail at configurable timestamp (not just first frame)    │
 * │   - BlurHash for progressive loading placeholders                       │
 * └──────────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.evidence

import com.thewatch.app.data.model.Evidence
import com.thewatch.app.data.model.EvidenceType

/**
 * Port interface for evidence thumbnail generation and caching.
 */
interface ThumbnailGeneratorPort {

    /**
     * Generate a thumbnail for the given evidence item.
     * Dispatches to type-specific generation logic.
     *
     * @param evidence The evidence item to generate a thumbnail for
     * @return Absolute file path of the generated thumbnail, or null if not applicable (e.g., SITREP)
     */
    suspend fun generateThumbnail(evidence: Evidence): String?

    /**
     * Generate a photo thumbnail: 200x200 center-crop JPEG.
     *
     * @param sourcePath Path to the full-size photo
     * @param evidenceId Used for naming the thumbnail file
     * @return Path to the generated thumbnail
     */
    suspend fun generatePhotoThumbnail(sourcePath: String, evidenceId: String): String

    /**
     * Generate a video thumbnail: first frame extracted as 200x200 JPEG.
     *
     * @param sourcePath Path to the video file
     * @param evidenceId Used for naming the thumbnail file
     * @return Path to the generated thumbnail
     */
    suspend fun generateVideoThumbnail(sourcePath: String, evidenceId: String): String

    /**
     * Generate an audio waveform thumbnail: 200x200 PNG.
     * Visualizes amplitude data as vertical bars.
     *
     * @param sourcePath Path to the audio file
     * @param evidenceId Used for naming the thumbnail file
     * @return Path to the generated thumbnail
     */
    suspend fun generateAudioThumbnail(sourcePath: String, evidenceId: String): String

    /**
     * Check if a thumbnail exists in cache for the given evidence ID.
     *
     * @param evidenceId The evidence item ID
     * @return True if cached thumbnail exists
     */
    suspend fun hasCachedThumbnail(evidenceId: String): Boolean

    /**
     * Get the cached thumbnail path for an evidence ID.
     *
     * @param evidenceId The evidence item ID
     * @return Path to cached thumbnail, or null if not cached
     */
    suspend fun getCachedThumbnailPath(evidenceId: String): String?

    /**
     * Clear all cached thumbnails for an incident.
     *
     * @param incidentId The incident whose thumbnails to clear
     * @return Number of thumbnails cleared
     */
    suspend fun clearCache(incidentId: String): Int

    /**
     * Clear all thumbnail caches. Used on low-storage conditions.
     *
     * @return Total bytes freed
     */
    suspend fun clearAllCaches(): Long

    /**
     * Get total cache size in bytes.
     */
    suspend fun getCacheSize(): Long
}
