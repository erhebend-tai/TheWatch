// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:         MockEvidenceService.swift
// Purpose:      Mock adapter for EvidenceServiceProtocol. Stores evidence
//               in-memory with simulated latency. Permanent first-class code
//               for development, previews, and testing. Implements full
//               SHA-256 hash chain for verification testing.
// Created:      2026-03-24
// Author:       Claude
// Dependencies: Foundation, CoreLocation, CryptoKit, Combine, Evidence.swift
//
// Usage Example:
//   let service = MockEvidenceService()
//   let photo = try await service.capturePhoto(
//       incidentId: "inc-001",
//       location: CLLocationCoordinate2D(latitude: 30.27, longitude: -97.74),
//       annotation: "Front entrance"
//   )
//   let all = try await service.getEvidenceForIncident("inc-001")
//   let result = try await service.verifyChain(incidentId: "inc-001")
//   assert(result.isValid)
// ============================================================================

import Foundation
import CoreLocation
import CryptoKit
import Combine

@Observable
final class MockEvidenceService: EvidenceServiceProtocol {

    // MARK: - Storage

    private var evidenceStore: [UUID: Evidence] = [:]
    private let evidenceSubject = CurrentValueSubject<[String: [Evidence]], Never>([:])
    private let userId = "mock-user-001"

    /// Simulated network latency range (ms)
    var simulatedLatency: ClosedRange<UInt64> = 100_000_000...300_000_000

    /// Toggle to simulate capture failures
    var simulateFailures = false

    init() {
        seedMockData()
    }

    // MARK: - Capture Operations

    func capturePhoto(
        incidentId: String,
        location: CLLocationCoordinate2D?,
        annotation: String?
    ) async throws -> Evidence {
        try await simulateLatency()
        if simulateFailures { throw EvidenceError.captureFailed("Mock camera failure") }

        let chain = evidenceForIncident(incidentId)
        let previousHash = chain.last?.hash ?? "GENESIS"
        let mockContent = "MOCK_PHOTO_\(UUID().uuidString)".data(using: .utf8)!
        let timestamp = Date()
        let hash = computeHash(contentBytes: mockContent, previousHash: previousHash, timestamp: timestamp)

        let evidence = Evidence(
            incidentId: incidentId,
            type: .photo,
            fileURL: URL(fileURLWithPath: "/mock/evidence/photo_\(UUID().uuidString).jpg"),
            thumbnailURL: URL(fileURLWithPath: "/mock/evidence/thumb_\(UUID().uuidString).jpg"),
            hash: hash,
            previousHash: previousHash,
            timestamp: timestamp,
            location: location,
            metadata: [
                "camera": "rear",
                "flash": "auto",
                "resolution": "4032x3024",
                "format": "JPEG"
            ],
            submittedBy: userId,
            verified: true,
            annotation: annotation,
            fileSizeBytes: Int64.random(in: 500_000...5_000_000),
            mimeType: "image/jpeg"
        )

        store(evidence)
        return evidence
    }

    func captureVideo(
        incidentId: String,
        location: CLLocationCoordinate2D?,
        maxDuration: TimeInterval = 60
    ) async throws -> Evidence {
        try await simulateLatency()
        if simulateFailures { throw EvidenceError.captureFailed("Mock video failure") }

        let chain = evidenceForIncident(incidentId)
        let previousHash = chain.last?.hash ?? "GENESIS"
        let mockContent = "MOCK_VIDEO_\(UUID().uuidString)".data(using: .utf8)!
        let timestamp = Date()
        let hash = computeHash(contentBytes: mockContent, previousHash: previousHash, timestamp: timestamp)
        let duration = TimeInterval.random(in: 5...min(maxDuration, 60))

        let evidence = Evidence(
            incidentId: incidentId,
            type: .video,
            fileURL: URL(fileURLWithPath: "/mock/evidence/video_\(UUID().uuidString).mp4"),
            thumbnailURL: URL(fileURLWithPath: "/mock/evidence/thumb_video_\(UUID().uuidString).jpg"),
            hash: hash,
            previousHash: previousHash,
            timestamp: timestamp,
            location: location,
            metadata: [
                "codec": "H.265",
                "fps": "30",
                "resolution": "1920x1080",
                "format": "MP4"
            ],
            submittedBy: userId,
            verified: true,
            fileSizeBytes: Int64(duration * 2_500_000),
            mimeType: "video/mp4",
            durationSeconds: duration
        )

        store(evidence)
        return evidence
    }

    func recordAudio(
        incidentId: String,
        location: CLLocationCoordinate2D?
    ) async throws -> Evidence {
        try await simulateLatency()
        if simulateFailures { throw EvidenceError.captureFailed("Mock audio failure") }

        let chain = evidenceForIncident(incidentId)
        let previousHash = chain.last?.hash ?? "GENESIS"
        let mockContent = "MOCK_AUDIO_\(UUID().uuidString)".data(using: .utf8)!
        let timestamp = Date()
        let hash = computeHash(contentBytes: mockContent, previousHash: previousHash, timestamp: timestamp)
        let duration = TimeInterval.random(in: 10...120)

        let evidence = Evidence(
            incidentId: incidentId,
            type: .audio,
            fileURL: URL(fileURLWithPath: "/mock/evidence/audio_\(UUID().uuidString).m4a"),
            thumbnailURL: URL(fileURLWithPath: "/mock/evidence/waveform_\(UUID().uuidString).png"),
            hash: hash,
            previousHash: previousHash,
            timestamp: timestamp,
            location: location,
            metadata: [
                "sampleRate": "44100",
                "channels": "1",
                "codec": "AAC",
                "format": "M4A",
                "transcription": "[Mock transcription placeholder]"
            ],
            submittedBy: userId,
            verified: true,
            fileSizeBytes: Int64(duration * 16_000),
            mimeType: "audio/mp4",
            durationSeconds: duration
        )

        store(evidence)
        return evidence
    }

    func submitSitrep(
        incidentId: String,
        situationType: SituationType,
        severity: SitrepSeverity,
        description: String,
        attachedEvidenceIds: [UUID],
        location: CLLocationCoordinate2D?
    ) async throws -> Evidence {
        try await simulateLatency()

        let chain = evidenceForIncident(incidentId)
        let previousHash = chain.last?.hash ?? "GENESIS"
        let content = "\(situationType.rawValue)|\(severity.rawValue)|\(description)".data(using: .utf8)!
        let timestamp = Date()
        let hash = computeHash(contentBytes: content, previousHash: previousHash, timestamp: timestamp)

        let evidence = Evidence(
            incidentId: incidentId,
            type: .sitrep,
            hash: hash,
            previousHash: previousHash,
            timestamp: timestamp,
            location: location,
            metadata: ["wordCount": "\(description.split(separator: " ").count)"],
            submittedBy: userId,
            verified: true,
            annotation: description,
            situationType: situationType,
            severity: severity,
            attachedEvidenceIds: attachedEvidenceIds,
            fileSizeBytes: Int64(content.count),
            mimeType: "text/plain"
        )

        store(evidence)
        return evidence
    }

    // MARK: - Retrieval

    func getEvidenceForIncident(_ incidentId: String) async throws -> [Evidence] {
        try await simulateLatency()
        return evidenceForIncident(incidentId)
    }

    func getEvidenceById(_ evidenceId: UUID) async throws -> Evidence? {
        try await simulateLatency()
        return evidenceStore[evidenceId]
    }

    func getEvidenceByType(_ incidentId: String, type: EvidenceType) async throws -> [Evidence] {
        try await simulateLatency()
        return evidenceForIncident(incidentId).filter { $0.type == type }
    }

    func evidencePublisher(for incidentId: String) -> AnyPublisher<[Evidence], Never> {
        evidenceSubject
            .map { store in store[incidentId] ?? [] }
            .eraseToAnyPublisher()
    }

    // MARK: - Chain-of-Custody

    func computeHash(contentBytes: Data, previousHash: String, timestamp: Date) -> String {
        var data = contentBytes
        data.append(previousHash.data(using: .utf8)!)
        data.append("\(timestamp.timeIntervalSince1970)".data(using: .utf8)!)
        let digest = SHA256.hash(data: data)
        return digest.map { String(format: "%02x", $0) }.joined()
    }

    func verifyChain(incidentId: String) async throws -> VerificationResult {
        let chain = evidenceForIncident(incidentId)
        guard !chain.isEmpty else {
            return VerificationResult(isValid: true, message: "Empty chain", itemsVerified: 0)
        }

        var expectedPreviousHash = "GENESIS"
        for (index, item) in chain.enumerated() {
            if item.previousHash != expectedPreviousHash {
                return VerificationResult(
                    isValid: false,
                    message: "Chain broken at item \(index + 1): previousHash mismatch",
                    tamperedItemId: item.id,
                    itemsVerified: index
                )
            }
            expectedPreviousHash = item.hash
        }

        return VerificationResult(
            isValid: true,
            message: "All \(chain.count) items verified. Chain intact.",
            itemsVerified: chain.count
        )
    }

    // MARK: - Lifecycle

    func deleteEvidence(_ evidenceId: UUID) async throws {
        try await simulateLatency()
        guard let evidence = evidenceStore.removeValue(forKey: evidenceId) else {
            throw EvidenceError.notFound(evidenceId)
        }
        notifyChange(for: evidence.incidentId)
    }

    func getStorageUsed(incidentId: String) async throws -> Int64 {
        evidenceForIncident(incidentId).reduce(0) { $0 + $1.fileSizeBytes }
    }

    // MARK: - Private Helpers

    private func evidenceForIncident(_ incidentId: String) -> [Evidence] {
        evidenceStore.values
            .filter { $0.incidentId == incidentId }
            .sorted { $0.timestamp < $1.timestamp }
    }

    private func store(_ evidence: Evidence) {
        evidenceStore[evidence.id] = evidence
        notifyChange(for: evidence.incidentId)
    }

    private func notifyChange(for incidentId: String) {
        var current = evidenceSubject.value
        current[incidentId] = evidenceForIncident(incidentId)
        evidenceSubject.send(current)
    }

    private func simulateLatency() async throws {
        try await Task.sleep(nanoseconds: UInt64.random(in: simulatedLatency))
    }

    // MARK: - Seed Data

    private func seedMockData() {
        let incidentId = "mock-incident-001"
        let baseLocation = CLLocationCoordinate2D(latitude: 37.7749, longitude: -122.4194)

        let photoContent = "SEED_PHOTO".data(using: .utf8)!
        let photoHash = computeHash(contentBytes: photoContent, previousHash: "GENESIS", timestamp: Date(timeIntervalSinceNow: -3600))

        let photo = Evidence(
            incidentId: incidentId,
            type: .photo,
            fileURL: URL(fileURLWithPath: "/mock/evidence/seed_photo.jpg"),
            thumbnailURL: URL(fileURLWithPath: "/mock/evidence/seed_thumb.jpg"),
            hash: photoHash,
            previousHash: "GENESIS",
            timestamp: Date(timeIntervalSinceNow: -3600),
            location: baseLocation,
            metadata: ["camera": "rear", "flash": "off"],
            submittedBy: userId,
            verified: true,
            annotation: "Scene overview from street level",
            fileSizeBytes: 2_340_000,
            mimeType: "image/jpeg"
        )
        evidenceStore[photo.id] = photo

        let audioContent = "SEED_AUDIO".data(using: .utf8)!
        let audioHash = computeHash(contentBytes: audioContent, previousHash: photoHash, timestamp: Date(timeIntervalSinceNow: -3000))

        let audio = Evidence(
            incidentId: incidentId,
            type: .audio,
            fileURL: URL(fileURLWithPath: "/mock/evidence/seed_audio.m4a"),
            thumbnailURL: URL(fileURLWithPath: "/mock/evidence/seed_waveform.png"),
            hash: audioHash,
            previousHash: photoHash,
            timestamp: Date(timeIntervalSinceNow: -3000),
            location: baseLocation,
            metadata: ["sampleRate": "44100", "channels": "1", "transcription": "I can see smoke coming from the building..."],
            submittedBy: userId,
            verified: true,
            fileSizeBytes: 480_000,
            mimeType: "audio/mp4",
            durationSeconds: 30
        )
        evidenceStore[audio.id] = audio

        let sitrepContent = "MEDICAL_EMERGENCY|HIGH|Multiple injuries reported".data(using: .utf8)!
        let sitrepHash = computeHash(contentBytes: sitrepContent, previousHash: audioHash, timestamp: Date(timeIntervalSinceNow: -2400))

        let sitrep = Evidence(
            incidentId: incidentId,
            type: .sitrep,
            hash: sitrepHash,
            previousHash: audioHash,
            timestamp: Date(timeIntervalSinceNow: -2400),
            location: baseLocation,
            metadata: ["wordCount": "5"],
            submittedBy: userId,
            verified: true,
            annotation: "Multiple injuries reported, fire department on scene. Two individuals need immediate medical attention.",
            situationType: .medicalEmergency,
            severity: .high,
            attachedEvidenceIds: [photo.id],
            fileSizeBytes: 156,
            mimeType: "text/plain"
        )
        evidenceStore[sitrep.id] = sitrep

        notifyChange(for: incidentId)
    }
}

// MARK: - Evidence Errors

enum EvidenceError: Error, LocalizedError {
    case captureFailed(String)
    case notFound(UUID)
    case chainBroken(String)
    case storageUnavailable
    case permissionDenied

    var errorDescription: String? {
        switch self {
        case .captureFailed(let reason): return "Evidence capture failed: \(reason)"
        case .notFound(let id): return "Evidence item \(id) not found"
        case .chainBroken(let detail): return "Chain of custody broken: \(detail)"
        case .storageUnavailable: return "Evidence storage is unavailable"
        case .permissionDenied: return "Camera/microphone permission denied"
        }
    }
}
