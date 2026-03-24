// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:         EvidenceService.swift
// Purpose:      Protocol (hexagonal port) for all evidence capture, storage,
//               retrieval, and chain-of-custody operations. Defines the
//               boundary between domain logic and evidence infrastructure.
// Created:      2026-03-24
// Author:       Claude
// Dependencies: Foundation, CoreLocation, Combine, Evidence.swift
//
// Usage Example:
//   let service: EvidenceServiceProtocol = MockEvidenceService()
//   let photo = try await service.capturePhoto(
//       incidentId: "inc-001",
//       location: CLLocationCoordinate2D(latitude: 30.27, longitude: -97.74)
//   )
//   let chain = try await service.getEvidenceForIncident("inc-001")
//   let isValid = try await service.verifyChain(incidentId: "inc-001")
//
// Adapters:
//   - MockEvidenceService: In-memory, dev/testing
//   - NativeEvidenceService: AVCaptureSession + FileManager (future)
//   - CloudEvidenceService: Firebase Storage + Firestore (future)
//
// Standards Reference:
//   - ISO/IEC 27037:2012 (Digital Evidence handling)
//   - NIST SP 800-86 (Guide to Integrating Forensic Techniques)
//   - SWGDE Best Practices for Digital Evidence
// ============================================================================

import Foundation
import CoreLocation
import Combine

// MARK: - Verification Result

/// Result of a tamper detection verification operation.
struct VerificationResult: Sendable {
    let isValid: Bool
    let message: String
    let tamperedItemId: UUID?
    let itemsVerified: Int
    let verifiedAt: Date

    init(isValid: Bool, message: String, tamperedItemId: UUID? = nil, itemsVerified: Int = 0) {
        self.isValid = isValid
        self.message = message
        self.tamperedItemId = tamperedItemId
        self.itemsVerified = itemsVerified
        self.verifiedAt = Date()
    }
}

// MARK: - Evidence Service Protocol

/// Primary port for evidence capture, storage, and retrieval.
/// All methods are async to support camera I/O, disk operations, and network sync.
/// Implementations MUST be thread-safe.
protocol EvidenceServiceProtocol: AnyObject {

    // MARK: Capture Operations

    /// Capture a photo for the given incident.
    /// - Parameters:
    ///   - incidentId: The incident this photo belongs to
    ///   - location: GPS coordinates at capture time
    ///   - annotation: Optional text overlay on the photo
    /// - Returns: The created Evidence item
    func capturePhoto(
        incidentId: String,
        location: CLLocationCoordinate2D?,
        annotation: String?
    ) async throws -> Evidence

    /// Record video for the given incident.
    /// - Parameters:
    ///   - incidentId: The incident this video belongs to
    ///   - location: GPS coordinates at start of recording
    ///   - maxDuration: Maximum recording duration (default 60s)
    /// - Returns: The created Evidence item after recording completes
    func captureVideo(
        incidentId: String,
        location: CLLocationCoordinate2D?,
        maxDuration: TimeInterval
    ) async throws -> Evidence

    /// Record audio memo for the given incident.
    /// - Parameters:
    ///   - incidentId: The incident this audio belongs to
    ///   - location: GPS coordinates at recording time
    /// - Returns: The created Evidence item after recording completes
    func recordAudio(
        incidentId: String,
        location: CLLocationCoordinate2D?
    ) async throws -> Evidence

    /// Submit a structured situation report.
    /// - Parameters:
    ///   - incidentId: The incident this sitrep belongs to
    ///   - situationType: Category of the situation
    ///   - severity: Severity level
    ///   - description: Free-text description
    ///   - attachedEvidenceIds: IDs of photo/video evidence to attach
    ///   - location: GPS coordinates
    /// - Returns: The created Evidence item
    func submitSitrep(
        incidentId: String,
        situationType: SituationType,
        severity: SitrepSeverity,
        description: String,
        attachedEvidenceIds: [UUID],
        location: CLLocationCoordinate2D?
    ) async throws -> Evidence

    // MARK: Retrieval Operations

    /// Get all evidence for an incident, ordered by timestamp ascending.
    func getEvidenceForIncident(_ incidentId: String) async throws -> [Evidence]

    /// Get a single evidence item by ID.
    func getEvidenceById(_ evidenceId: UUID) async throws -> Evidence?

    /// Get evidence filtered by type for a given incident.
    func getEvidenceByType(_ incidentId: String, type: EvidenceType) async throws -> [Evidence]

    /// Reactive publisher for evidence changes on an incident.
    func evidencePublisher(for incidentId: String) -> AnyPublisher<[Evidence], Never>

    // MARK: Chain-of-Custody

    /// Compute SHA-256 hash for evidence content within the chain.
    /// hash = SHA-256(contentBytes + previousHash.utf8 + timestamp)
    func computeHash(contentBytes: Data, previousHash: String, timestamp: Date) -> String

    /// Verify the entire hash chain for an incident.
    func verifyChain(incidentId: String) async throws -> VerificationResult

    // MARK: Lifecycle

    /// Delete evidence by ID.
    func deleteEvidence(_ evidenceId: UUID) async throws

    /// Get total storage used by evidence for an incident (bytes).
    func getStorageUsed(incidentId: String) async throws -> Int64
}
