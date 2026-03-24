// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:         Evidence.swift
// Purpose:      Data model for all evidence types (photo, video, audio, sitrep)
//               collected during an incident. Each evidence item participates
//               in a SHA-256 hash chain for tamper detection and chain-of-custody
//               verification per ISO/IEC 27037:2012 and NIST SP 800-86.
// Created:      2026-03-24
// Author:       Claude
// Dependencies: Foundation, CoreLocation
//
// Usage Example:
//   let photo = Evidence(
//       id: UUID(),
//       incidentId: "incident-001",
//       type: .photo,
//       fileURL: URL(fileURLWithPath: "/evidence/photo_001.jpg"),
//       hash: "sha256:abc123...",
//       previousHash: "GENESIS",
//       timestamp: Date(),
//       location: CLLocationCoordinate2D(latitude: 30.2672, longitude: -97.7431),
//       submittedBy: "user-001"
//   )
//
// Chain-of-Custody Hash Calculation:
//   hash = SHA-256(fileBytes + previousHash.utf8 + timestamp.timeIntervalSince1970)
//   Genesis block uses previousHash = "GENESIS"
//
// Standards Reference:
//   - ISO/IEC 27037:2012 (Digital Evidence handling)
//   - NIST SP 800-86 (Guide to Integrating Forensic Techniques)
//   - SWGDE Best Practices for Digital Evidence
//   - Federal Rules of Evidence, Rule 901(b)(9)
//
// Potential Additions:
//   - EXIF metadata extraction for photos
//   - Video codec info (H.264/H.265 profile)
//   - Digital signature with user's private key (PKI-based)
//   - Blockchain anchoring for immutable timestamping
// ============================================================================

import Foundation
import CoreLocation

// MARK: - Evidence Type

/// Types of evidence supported by TheWatch.
/// Each type has different capture, storage, and thumbnail generation logic.
enum EvidenceType: String, Codable, CaseIterable, Sendable {
    /// Still image captured via AVCaptureSession or selected from photo library
    case photo = "PHOTO"

    /// Video recording, max 60s during emergency auto-capture
    case video = "VIDEO"

    /// Audio recording via AVAudioRecorder, voice memo / ambient capture
    case audio = "AUDIO"

    /// Structured situation report: type, severity, description, attachments
    case sitrep = "SITREP"

    var displayName: String {
        switch self {
        case .photo: return "Photo"
        case .video: return "Video"
        case .audio: return "Audio"
        case .sitrep: return "Situation Report"
        }
    }

    var systemImage: String {
        switch self {
        case .photo: return "camera.fill"
        case .video: return "video.fill"
        case .audio: return "mic.fill"
        case .sitrep: return "doc.text.fill"
        }
    }
}

// MARK: - Sitrep Severity

/// Severity levels for sitrep submissions.
/// Aligned with existing Alert severity in the codebase.
enum SitrepSeverity: String, Codable, CaseIterable, Sendable {
    case low = "LOW"
    case medium = "MEDIUM"
    case high = "HIGH"
    case critical = "CRITICAL"

    var displayName: String {
        switch self {
        case .low: return "Low"
        case .medium: return "Medium"
        case .high: return "High"
        case .critical: return "Critical"
        }
    }

    var color: String {
        switch self {
        case .low: return "green"
        case .medium: return "yellow"
        case .high: return "orange"
        case .critical: return "red"
        }
    }
}

// MARK: - Situation Type

/// Situation type categories for structured sitrep submission.
enum SituationType: String, Codable, CaseIterable, Sendable {
    case medicalEmergency = "MEDICAL_EMERGENCY"
    case fire = "FIRE"
    case naturalDisaster = "NATURAL_DISASTER"
    case assault = "ASSAULT"
    case suspiciousActivity = "SUSPICIOUS_ACTIVITY"
    case trafficAccident = "TRAFFIC_ACCIDENT"
    case structuralDamage = "STRUCTURAL_DAMAGE"
    case hazmatSpill = "HAZMAT_SPILL"
    case missingPerson = "MISSING_PERSON"
    case welfareCheck = "WELFARE_CHECK"
    case other = "OTHER"

    var displayName: String {
        switch self {
        case .medicalEmergency: return "Medical Emergency"
        case .fire: return "Fire"
        case .naturalDisaster: return "Natural Disaster"
        case .assault: return "Assault"
        case .suspiciousActivity: return "Suspicious Activity"
        case .trafficAccident: return "Traffic Accident"
        case .structuralDamage: return "Structural Damage"
        case .hazmatSpill: return "Hazmat Spill"
        case .missingPerson: return "Missing Person"
        case .welfareCheck: return "Welfare Check"
        case .other: return "Other"
        }
    }

    var systemImage: String {
        switch self {
        case .medicalEmergency: return "cross.case.fill"
        case .fire: return "flame.fill"
        case .naturalDisaster: return "tornado"
        case .assault: return "exclamationmark.shield.fill"
        case .suspiciousActivity: return "eye.fill"
        case .trafficAccident: return "car.fill"
        case .structuralDamage: return "building.2.fill"
        case .hazmatSpill: return "hazardsign.fill"
        case .missingPerson: return "person.fill.questionmark"
        case .welfareCheck: return "heart.text.square.fill"
        case .other: return "questionmark.circle.fill"
        }
    }
}

// MARK: - Evidence Model

/// Core evidence data model.
///
/// Participates in a SHA-256 hash chain: each item's `hash` is computed from
/// the content bytes + `previousHash` + `timestamp`, creating an append-only,
/// tamper-evident log per incident.
struct Evidence: Identifiable, Codable, Sendable {
    /// Unique evidence item ID
    let id: UUID

    /// The incident this evidence belongs to
    let incidentId: String

    /// Type discriminator: photo, video, audio, sitrep
    let type: EvidenceType

    /// File URL on device (nil for SITREP text-only entries)
    let fileURL: URL?

    /// URL to generated thumbnail (200x200 photo, first-frame video, waveform audio)
    let thumbnailURL: URL?

    /// SHA-256 hash: hex-encoded, computed from content + previousHash + timestamp
    let hash: String

    /// Hash of the preceding evidence item in this incident's chain. "GENESIS" for first.
    let previousHash: String

    /// When evidence was captured
    let timestamp: Date

    /// Location where evidence was captured (WGS84)
    let location: CLLocationCoordinate2D?

    /// Freeform metadata: camera direction, flash, duration, transcription, etc.
    let metadata: [String: String]

    /// User ID of the person who submitted this evidence
    let submittedBy: String

    /// True if hash chain verification passed on last retrieval
    var verified: Bool

    /// Text annotation overlaid on photo, or description for SITREP
    let annotation: String?

    /// For SITREP: situation type
    let situationType: SituationType?

    /// For SITREP: severity level
    let severity: SitrepSeverity?

    /// For SITREP: list of attached evidence IDs
    let attachedEvidenceIds: [UUID]

    /// File size in bytes
    let fileSizeBytes: Int64

    /// MIME type of the evidence file
    let mimeType: String?

    /// Duration in seconds for VIDEO and AUDIO types
    let durationSeconds: TimeInterval?

    init(
        id: UUID = UUID(),
        incidentId: String,
        type: EvidenceType,
        fileURL: URL? = nil,
        thumbnailURL: URL? = nil,
        hash: String,
        previousHash: String = "GENESIS",
        timestamp: Date = Date(),
        location: CLLocationCoordinate2D? = nil,
        metadata: [String: String] = [:],
        submittedBy: String,
        verified: Bool = false,
        annotation: String? = nil,
        situationType: SituationType? = nil,
        severity: SitrepSeverity? = nil,
        attachedEvidenceIds: [UUID] = [],
        fileSizeBytes: Int64 = 0,
        mimeType: String? = nil,
        durationSeconds: TimeInterval? = nil
    ) {
        self.id = id
        self.incidentId = incidentId
        self.type = type
        self.fileURL = fileURL
        self.thumbnailURL = thumbnailURL
        self.hash = hash
        self.previousHash = previousHash
        self.timestamp = timestamp
        self.location = location
        self.metadata = metadata
        self.submittedBy = submittedBy
        self.verified = verified
        self.annotation = annotation
        self.situationType = situationType
        self.severity = severity
        self.attachedEvidenceIds = attachedEvidenceIds
        self.fileSizeBytes = fileSizeBytes
        self.mimeType = mimeType
        self.durationSeconds = durationSeconds
    }

    // MARK: - Codable for CLLocationCoordinate2D

    enum CodingKeys: String, CodingKey {
        case id, incidentId, type, fileURL, thumbnailURL, hash, previousHash
        case timestamp, latitude, longitude, metadata, submittedBy, verified
        case annotation, situationType, severity, attachedEvidenceIds
        case fileSizeBytes, mimeType, durationSeconds
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decode(UUID.self, forKey: .id)
        incidentId = try c.decode(String.self, forKey: .incidentId)
        type = try c.decode(EvidenceType.self, forKey: .type)
        fileURL = try c.decodeIfPresent(URL.self, forKey: .fileURL)
        thumbnailURL = try c.decodeIfPresent(URL.self, forKey: .thumbnailURL)
        hash = try c.decode(String.self, forKey: .hash)
        previousHash = try c.decode(String.self, forKey: .previousHash)
        timestamp = try c.decode(Date.self, forKey: .timestamp)
        let lat = try c.decodeIfPresent(Double.self, forKey: .latitude)
        let lng = try c.decodeIfPresent(Double.self, forKey: .longitude)
        if let lat, let lng {
            location = CLLocationCoordinate2D(latitude: lat, longitude: lng)
        } else {
            location = nil
        }
        metadata = try c.decode([String: String].self, forKey: .metadata)
        submittedBy = try c.decode(String.self, forKey: .submittedBy)
        verified = try c.decode(Bool.self, forKey: .verified)
        annotation = try c.decodeIfPresent(String.self, forKey: .annotation)
        situationType = try c.decodeIfPresent(SituationType.self, forKey: .situationType)
        severity = try c.decodeIfPresent(SitrepSeverity.self, forKey: .severity)
        attachedEvidenceIds = try c.decode([UUID].self, forKey: .attachedEvidenceIds)
        fileSizeBytes = try c.decode(Int64.self, forKey: .fileSizeBytes)
        mimeType = try c.decodeIfPresent(String.self, forKey: .mimeType)
        durationSeconds = try c.decodeIfPresent(TimeInterval.self, forKey: .durationSeconds)
    }

    func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(id, forKey: .id)
        try c.encode(incidentId, forKey: .incidentId)
        try c.encode(type, forKey: .type)
        try c.encodeIfPresent(fileURL, forKey: .fileURL)
        try c.encodeIfPresent(thumbnailURL, forKey: .thumbnailURL)
        try c.encode(hash, forKey: .hash)
        try c.encode(previousHash, forKey: .previousHash)
        try c.encode(timestamp, forKey: .timestamp)
        try c.encodeIfPresent(location?.latitude, forKey: .latitude)
        try c.encodeIfPresent(location?.longitude, forKey: .longitude)
        try c.encode(metadata, forKey: .metadata)
        try c.encode(submittedBy, forKey: .submittedBy)
        try c.encode(verified, forKey: .verified)
        try c.encodeIfPresent(annotation, forKey: .annotation)
        try c.encodeIfPresent(situationType, forKey: .situationType)
        try c.encodeIfPresent(severity, forKey: .severity)
        try c.encode(attachedEvidenceIds, forKey: .attachedEvidenceIds)
        try c.encode(fileSizeBytes, forKey: .fileSizeBytes)
        try c.encodeIfPresent(mimeType, forKey: .mimeType)
        try c.encodeIfPresent(durationSeconds, forKey: .durationSeconds)
    }
}
