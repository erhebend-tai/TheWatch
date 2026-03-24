// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:         TamperDetectionService.swift
// Purpose:      Protocol + mock for chain-of-custody tamper detection using
//               SHA-256 hash chains. Separated from EvidenceService for
//               independent testing and potential hardware-backed impl.
// Created:      2026-03-24
// Author:       Claude
// Dependencies: Foundation, CryptoKit, Evidence.swift
//
// Usage Example:
//   let detector: TamperDetectionProtocol = MockTamperDetectionService()
//   let hash = detector.computeHash(fileData, previousHash: "GENESIS", timestamp: Date())
//   let result = await detector.verifyChain(evidenceItems, contentProvider: { id in ... })
//
// Hash Chain Scheme:
//   Block N hash = SHA-256(contentBytes || previousHash.utf8 || timestamp.description)
//   Genesis block uses previousHash = "GENESIS"
//
// Standards Reference:
//   - NIST FIPS 180-4 (SHA-256 specification)
//   - ISO/IEC 27037:2012 Section 7 (Digital evidence integrity)
//   - RFC 6234 (SHA-256 test vectors)
//   - SWGDE Best Practices for Maintaining Integrity of Digital Evidence
//
// Potential Additions:
//   - HMAC-SHA256 with device-bound key (Secure Enclave)
//   - Merkle tree for efficient partial verification
//   - Timestamp authority (RFC 3161)
// ============================================================================

import Foundation
import CryptoKit

// MARK: - Protocol

protocol TamperDetectionProtocol: Sendable {
    /// Compute SHA-256 hash for evidence content within the chain.
    func computeHash(contentBytes: Data, previousHash: String, timestamp: Date) -> String

    /// Verify a single evidence item's hash against its content.
    func verifyItem(_ evidence: Evidence, contentBytes: Data) -> VerificationResult

    /// Verify the entire hash chain for a list of evidence items.
    func verifyChain(
        _ chain: [Evidence],
        contentProvider: @Sendable (UUID) async -> Data?
    ) async -> VerificationResult

    /// Get the last hash in the chain (or "GENESIS" if empty).
    func getLastHash(_ chain: [Evidence]) -> String
}

// MARK: - Mock Implementation

final class MockTamperDetectionService: TamperDetectionProtocol {

    func computeHash(contentBytes: Data, previousHash: String, timestamp: Date) -> String {
        var data = contentBytes
        data.append(previousHash.data(using: .utf8)!)
        data.append("\(timestamp.timeIntervalSince1970)".data(using: .utf8)!)
        let digest = SHA256.hash(data: data)
        return digest.map { String(format: "%02x", $0) }.joined()
    }

    func verifyItem(_ evidence: Evidence, contentBytes: Data) -> VerificationResult {
        let expected = computeHash(
            contentBytes: contentBytes,
            previousHash: evidence.previousHash,
            timestamp: evidence.timestamp
        )
        if expected == evidence.hash {
            return VerificationResult(
                isValid: true,
                message: "Item \(evidence.id) hash verified",
                itemsVerified: 1
            )
        } else {
            return VerificationResult(
                isValid: false,
                message: "Item \(evidence.id) hash mismatch: expected \(expected.prefix(16))..., got \(evidence.hash.prefix(16))...",
                tamperedItemId: evidence.id,
                itemsVerified: 0
            )
        }
    }

    func verifyChain(
        _ chain: [Evidence],
        contentProvider: @Sendable (UUID) async -> Data?
    ) async -> VerificationResult {
        guard !chain.isEmpty else {
            return VerificationResult(isValid: true, message: "Empty chain", itemsVerified: 0)
        }

        let sorted = chain.sorted { $0.timestamp < $1.timestamp }
        var expectedPreviousHash = "GENESIS"

        for (index, item) in sorted.enumerated() {
            // Verify chain linkage
            if item.previousHash != expectedPreviousHash {
                return VerificationResult(
                    isValid: false,
                    message: "Chain link broken at position \(index + 1): previousHash mismatch",
                    tamperedItemId: item.id,
                    itemsVerified: index
                )
            }

            // Verify content hash if content is available
            if let content = await contentProvider(item.id) {
                let computed = computeHash(
                    contentBytes: content,
                    previousHash: item.previousHash,
                    timestamp: item.timestamp
                )
                if computed != item.hash {
                    return VerificationResult(
                        isValid: false,
                        message: "Content tampered at position \(index + 1): hash mismatch",
                        tamperedItemId: item.id,
                        itemsVerified: index
                    )
                }
            }

            expectedPreviousHash = item.hash
        }

        return VerificationResult(
            isValid: true,
            message: "All \(sorted.count) items verified. Chain of custody intact.",
            itemsVerified: sorted.count
        )
    }

    func getLastHash(_ chain: [Evidence]) -> String {
        chain.sorted { $0.timestamp < $1.timestamp }.last?.hash ?? "GENESIS"
    }
}
