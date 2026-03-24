/**
 * ┌──────────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                         │
 * │ File:    EvidencePort.kt                                                │
 * │ Purpose: Hexagonal port (domain contract) for all evidence operations.  │
 * │          Defines the boundary between domain logic and evidence          │
 * │          capture/storage/retrieval infrastructure.                       │
 * │ Date:    2026-03-24                                                     │
 * │ Author:  Claude                                                         │
 * │ Deps:    Evidence model, kotlinx.coroutines.flow                        │
 * │                                                                         │
 * │ Usage Example:                                                          │
 * │   // In a ViewModel or use-case:                                        │
 * │   val port: EvidencePort = // injected via Hilt                         │
 * │   val photo = port.capturePhoto(incidentId, lat, lng)                   │
 * │   val allEvidence = port.getEvidenceForIncident(incidentId).first()     │
 * │   val isValid = port.verifyChain(incidentId)                            │
 * │                                                                         │
 * │ Adapters (implementations):                                             │
 * │   - MockEvidenceAdapter: In-memory, dev/testing                         │
 * │   - NativeEvidenceAdapter: CameraX + Room + local filesystem            │
 * │   - CloudEvidenceAdapter: Firebase Storage + Firestore (future)         │
 * │                                                                         │
 * │ Standards Reference:                                                    │
 * │   - ISO/IEC 27037:2012 (Digital Evidence handling)                      │
 * │   - NIST SP 800-86 (Guide to Integrating Forensic Techniques)          │
 * │   - Chain of Custody: SWGDE, IOCE, ENFSI guidelines                    │
 * └──────────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.evidence

import com.thewatch.app.data.model.Evidence
import com.thewatch.app.data.model.EvidenceType
import com.thewatch.app.data.model.SitrepSeverity
import com.thewatch.app.data.model.SituationType
import kotlinx.coroutines.flow.Flow

/**
 * Primary port for evidence capture, storage, and retrieval.
 *
 * All methods are suspend functions to support async I/O (camera, disk, network).
 * Implementations MUST be thread-safe — evidence can be captured from foreground
 * UI or background services (e.g., auto-capture during SOS).
 */
interface EvidencePort {

    // ── Capture Operations ────────────────────────────────────────────────

    /**
     * Capture a photo for the given incident.
     * Implementation should use CameraX, auto-geotag with [latitude]/[longitude],
     * embed timestamp, compute hash, and store to local filesystem.
     *
     * @param incidentId The incident this photo belongs to
     * @param latitude WGS84 latitude at capture time
     * @param longitude WGS84 longitude at capture time
     * @param annotation Optional text overlay on the photo
     * @return Result containing the created Evidence item, or failure
     */
    suspend fun capturePhoto(
        incidentId: String,
        latitude: Double,
        longitude: Double,
        annotation: String? = null
    ): Result<Evidence>

    /**
     * Start/stop video recording for the given incident.
     * Max 60 seconds during emergency mode. Auto-stops and submits.
     * Implementation should use CameraX VideoCapture API.
     *
     * @param incidentId The incident this video belongs to
     * @param latitude WGS84 latitude at start of recording
     * @param longitude WGS84 longitude at start of recording
     * @param maxDurationMillis Maximum recording duration (default 60s)
     * @return Result containing the created Evidence item after recording completes
     */
    suspend fun captureVideo(
        incidentId: String,
        latitude: Double,
        longitude: Double,
        maxDurationMillis: Long = 60_000L
    ): Result<Evidence>

    /**
     * Record an audio memo for the given incident.
     * Uses MediaRecorder. Includes placeholder for auto-transcription.
     *
     * @param incidentId The incident this audio belongs to
     * @param latitude WGS84 latitude at recording time
     * @param longitude WGS84 longitude at recording time
     * @return Result containing the created Evidence item after recording completes
     */
    suspend fun recordAudio(
        incidentId: String,
        latitude: Double,
        longitude: Double
    ): Result<Evidence>

    /**
     * Submit a structured situation report.
     * Includes type, severity, free-text description, and optional photo attachments.
     *
     * @param incidentId The incident this sitrep belongs to
     * @param situationType Category of the situation
     * @param severity Severity level
     * @param description Free-text description
     * @param attachedEvidenceIds IDs of photo/video evidence to attach
     * @param latitude WGS84 latitude
     * @param longitude WGS84 longitude
     * @return Result containing the created Evidence item
     */
    suspend fun submitSitrep(
        incidentId: String,
        situationType: SituationType,
        severity: SitrepSeverity,
        description: String,
        attachedEvidenceIds: List<String> = emptyList(),
        latitude: Double? = null,
        longitude: Double? = null
    ): Result<Evidence>

    // ── Retrieval Operations ──────────────────────────────────────────────

    /**
     * Get all evidence for an incident as a reactive Flow.
     * Items are ordered by timestamp ascending (chain order).
     *
     * @param incidentId The incident to query
     * @return Flow emitting the current list of evidence items
     */
    fun getEvidenceForIncident(incidentId: String): Flow<List<Evidence>>

    /**
     * Get a single evidence item by ID.
     *
     * @param evidenceId The evidence item ID
     * @return The evidence item, or null if not found
     */
    suspend fun getEvidenceById(evidenceId: String): Evidence?

    /**
     * Get evidence filtered by type for a given incident.
     *
     * @param incidentId The incident to query
     * @param type Filter by evidence type
     * @return List of matching evidence items
     */
    suspend fun getEvidenceByType(
        incidentId: String,
        type: EvidenceType
    ): List<Evidence>

    // ── Hash / Chain-of-Custody Operations ────────────────────────────────

    /**
     * Compute SHA-256 hash for evidence content.
     * Hash = SHA-256(contentBytes + previousHash + timestamp.toString())
     *
     * @param contentBytes Raw bytes of the evidence file
     * @param previousHash Hash of the previous item in the chain ("GENESIS" for first)
     * @param timestamp Capture timestamp as epoch millis
     * @return Hex-encoded SHA-256 hash string
     */
    suspend fun computeHash(
        contentBytes: ByteArray,
        previousHash: String,
        timestamp: Long
    ): String

    /**
     * Verify the entire hash chain for an incident.
     * Walks through all evidence items in timestamp order and recomputes
     * each hash, comparing against the stored value.
     *
     * @param incidentId The incident whose chain to verify
     * @return True if the entire chain is intact; false if any item is tampered
     */
    suspend fun verifyChain(incidentId: String): Boolean

    // ── Lifecycle ─────────────────────────────────────────────────────────

    /**
     * Delete evidence by ID. Removes file from disk and entry from store.
     * Only allowed for the submitter or an admin.
     *
     * @param evidenceId The evidence item to delete
     * @return Result indicating success or failure
     */
    suspend fun deleteEvidence(evidenceId: String): Result<Unit>

    /**
     * Get total storage used by evidence for an incident.
     *
     * @param incidentId The incident to measure
     * @return Total bytes used
     */
    suspend fun getStorageUsed(incidentId: String): Long
}
