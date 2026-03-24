/**
 * ┌──────────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                         │
 * │ File:    Evidence.kt                                                    │
 * │ Purpose: Data model for all evidence types (photo, video, audio,        │
 * │          sitrep) collected during an incident. Each evidence item        │
 * │          participates in a SHA-256 hash chain for tamper detection       │
 * │          and chain-of-custody verification.                             │
 * │ Date:    2026-03-24                                                     │
 * │ Author:  Claude                                                         │
 * │ Deps:    kotlinx.serialization                                          │
 * │                                                                         │
 * │ Usage Example:                                                          │
 * │   val photo = Evidence(                                                 │
 * │       id = UUID.randomUUID().toString(),                                │
 * │       incidentId = "incident-001",                                      │
 * │       type = EvidenceType.PHOTO,                                        │
 * │       filePath = "/data/evidence/photo_001.jpg",                        │
 * │       thumbnailPath = "/data/evidence/thumb_photo_001.jpg",             │
 * │       hash = "sha256:abc123...",                                        │
 * │       previousHash = "sha256:000...",                                   │
 * │       timestamp = System.currentTimeMillis(),                           │
 * │       latitude = 30.2672,                                               │
 * │       longitude = -97.7431,                                             │
 * │       metadata = mapOf("camera" to "rear", "flash" to "off"),           │
 * │       submittedBy = "user-001",                                         │
 * │       verified = true                                                   │
 * │   )                                                                     │
 * │                                                                         │
 * │ Chain-of-Custody Hash Calculation:                                      │
 * │   hash = SHA-256(fileBytes + previousHash + timestamp.toString())       │
 * │   This ensures any modification to content, ordering, or timing         │
 * │   is detectable. The genesis block uses previousHash = "GENESIS".       │
 * │                                                                         │
 * │ Standards Reference:                                                    │
 * │   - NIST SP 800-92 (Guide to Computer Security Log Management)          │
 * │   - ISO/IEC 27037:2012 (Digital Evidence — Identification, Collection)  │
 * │   - SWGDE Best Practices for Digital Evidence                           │
 * │   - Federal Rules of Evidence, Rule 901(b)(9)                           │
 * │                                                                         │
 * │ Possible Future Extensions:                                             │
 * │   - EXIF metadata extraction for photos (orientation, GPS accuracy)     │
 * │   - Video codec info (H.264/H.265 profile, bitrate)                    │
 * │   - Audio format metadata (sample rate, channels, codec)                │
 * │   - Digital signature with user's private key (PKI-based)               │
 * │   - Blockchain anchoring for immutable timestamping                     │
 * │   - IPFS content-addressed storage for distributed evidence             │
 * └──────────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.model

import kotlinx.serialization.Serializable

/**
 * Evidence types supported by the system.
 * Each type has different capture, storage, and thumbnail generation logic.
 */
@Serializable
enum class EvidenceType {
    /** Still image captured via CameraX or selected from gallery */
    PHOTO,

    /** Video recording, max 60s during emergency auto-capture */
    VIDEO,

    /** Audio recording via MediaRecorder, voice memo / ambient capture */
    AUDIO,

    /** Structured situation report: type, severity, description, attachments */
    SITREP
}

/**
 * Severity levels for sitrep submissions.
 * Aligned with existing Alert severity in the codebase (LOW, MEDIUM, HIGH, CRITICAL).
 */
@Serializable
enum class SitrepSeverity {
    LOW,
    MEDIUM,
    HIGH,
    CRITICAL
}

/**
 * Situation type categories for structured sitrep submission.
 * Covers the primary emergency categories TheWatch handles.
 */
@Serializable
enum class SituationType {
    MEDICAL_EMERGENCY,
    FIRE,
    NATURAL_DISASTER,
    ASSAULT,
    SUSPICIOUS_ACTIVITY,
    TRAFFIC_ACCIDENT,
    STRUCTURAL_DAMAGE,
    HAZMAT_SPILL,
    MISSING_PERSON,
    WELFARE_CHECK,
    OTHER
}

/**
 * Core evidence data class.
 *
 * Participates in a hash chain: each item's [hash] is computed from
 * the content bytes + [previousHash] + [timestamp], creating an
 * append-only, tamper-evident log per incident.
 *
 * The [verified] flag is set after chain integrity check on retrieval.
 */
@Serializable
data class Evidence(
    /** Unique evidence item ID (UUID v4) */
    val id: String,

    /** The incident this evidence belongs to */
    val incidentId: String,

    /** Type discriminator: PHOTO, VIDEO, AUDIO, SITREP */
    val type: EvidenceType,

    /** Absolute file path on device (null for SITREP text-only entries) */
    val filePath: String? = null,

    /** Path to generated thumbnail (200x200 photo, first-frame video, waveform audio) */
    val thumbnailPath: String? = null,

    /** SHA-256 hash: hex-encoded, computed from content + previousHash + timestamp */
    val hash: String,

    /** Hash of the preceding evidence item in this incident's chain. "GENESIS" for first. */
    val previousHash: String = "GENESIS",

    /** Unix epoch millis when evidence was captured */
    val timestamp: Long,

    /** Latitude where evidence was captured (WGS84) */
    val latitude: Double? = null,

    /** Longitude where evidence was captured (WGS84) */
    val longitude: Double? = null,

    /** Freeform metadata: camera direction, flash, duration, transcription, etc. */
    val metadata: Map<String, String> = emptyMap(),

    /** User ID of the person who submitted this evidence */
    val submittedBy: String,

    /** True if hash chain verification passed on last retrieval */
    val verified: Boolean = false,

    /** Text annotation overlaid on photo, or description for SITREP */
    val annotation: String? = null,

    /** For SITREP: situation type */
    val situationType: SituationType? = null,

    /** For SITREP: severity level */
    val severity: SitrepSeverity? = null,

    /** For SITREP: list of attached evidence IDs (photos, etc.) */
    val attachedEvidenceIds: List<String> = emptyList(),

    /** File size in bytes, for storage management */
    val fileSizeBytes: Long = 0L,

    /** MIME type of the evidence file */
    val mimeType: String? = null,

    /** Duration in milliseconds for VIDEO and AUDIO types */
    val durationMillis: Long? = null
)
