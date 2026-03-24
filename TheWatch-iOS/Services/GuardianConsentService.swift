// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:         GuardianConsentService.swift
// Purpose:      Protocol (port) and mock adapter for Guardian Consent
//               verification. When a user under 18 signs up, a legal guardian
//               must provide consent. This service handles sending consent
//               requests to guardians (via email/SMS), verifying consent codes,
//               and checking consent status. Follows hexagonal port/adapter
//               pattern per project architecture standards.
// Created:      2026-03-24
// Author:       Claude
// Dependencies: Foundation
// Related:      GuardianConsentView.swift (view),
//               GuardianConsentViewModel.swift (view model),
//               SignUpViewModel.swift (triggers consent flow if DOB < 18)
//
// Usage Example:
//   let service: GuardianConsentServiceProtocol = MockGuardianConsentService()
//   let requestId = try await service.sendConsentRequest(
//       minorEmail: "kid@example.com",
//       guardianEmail: "parent@example.com",
//       guardianPhone: "+15550100",
//       guardianName: "Jane Rivera",
//       relationship: .parent
//   )
//   let verified = try await service.verifyConsentCode(
//       requestId: requestId,
//       code: "123456"
//   )
//
// Legal/Compliance Notes:
//   - COPPA (Children's Online Privacy Protection Act) requires verifiable
//     parental consent for users under 13 in the US.
//   - GDPR Article 8 requires parental consent for users under 16 in the EU
//     (member states may lower to 13).
//   - TheWatch sets the threshold at 18 to align with most jurisdictions'
//     legal guardianship age and the severity of safety-critical features.
//   - Consent records must be retained for audit/compliance.
//
// Potential Additions:
//   - Digital signature capture for guardian consent
//   - Video verification call with guardian
//   - Government ID verification (e.g., via Jumio, Onfido)
//   - Multi-guardian consent (both parents required)
//   - Consent expiration and renewal workflows
//   - Court-order guardianship document upload
//   - Age verification via third-party identity provider
// ============================================================================

import Foundation

// MARK: - Guardian Relationship

/// The relationship between the guardian and the minor.
enum GuardianRelationship: String, Codable, CaseIterable, Sendable {
    case parent = "Parent"
    case legalGuardian = "Legal Guardian"
    case grandparent = "Grandparent"
    case fosterParent = "Foster Parent"
    case courtAppointed = "Court-Appointed Guardian"
    case other = "Other"
}

// MARK: - Consent Status

/// Status of a guardian consent request.
enum ConsentStatus: String, Codable, Sendable {
    case pending = "Pending"
    case verified = "Verified"
    case expired = "Expired"
    case denied = "Denied"
    case revoked = "Revoked"
}

// MARK: - Consent Request Model

/// Represents a guardian consent request record.
struct GuardianConsentRequest: Codable, Identifiable, Sendable {
    let id: String
    let minorEmail: String
    let guardianEmail: String
    let guardianPhone: String?
    let guardianName: String
    let relationship: GuardianRelationship
    var status: ConsentStatus
    let createdAt: Date
    var verifiedAt: Date?
    var expiresAt: Date

    /// Default consent expiration: 72 hours from creation
    static let defaultExpirationHours: Int = 72
}

// MARK: - Consent Errors

enum GuardianConsentError: Error, LocalizedError, Sendable {
    case requestNotFound
    case codeExpired
    case codeInvalid
    case alreadyVerified
    case alreadyDenied
    case sendFailed(underlying: String)
    case minorAgeNotVerified
    case guardianEmailRequired
    case rateLimitExceeded

    var errorDescription: String? {
        switch self {
        case .requestNotFound:
            return "Consent request not found."
        case .codeExpired:
            return "Consent verification code has expired. Please request a new one."
        case .codeInvalid:
            return "Invalid verification code. Please check and try again."
        case .alreadyVerified:
            return "Guardian consent has already been verified."
        case .alreadyDenied:
            return "Guardian has denied consent for this account."
        case .sendFailed(let underlying):
            return "Failed to send consent request: \(underlying)"
        case .minorAgeNotVerified:
            return "Minor's age has not been verified."
        case .guardianEmailRequired:
            return "Guardian's email address is required."
        case .rateLimitExceeded:
            return "Too many consent requests. Please wait before trying again."
        }
    }
}

// MARK: - GuardianConsentServiceProtocol

/// Port protocol for guardian consent verification.
/// Adapters provide either mock or production implementations.
protocol GuardianConsentServiceProtocol: Sendable {

    /// Send a consent request to the guardian via email and/or SMS.
    /// - Parameters:
    ///   - minorEmail: The email of the minor signing up
    ///   - guardianEmail: The guardian's email address
    ///   - guardianPhone: The guardian's phone number (optional, for SMS)
    ///   - guardianName: The guardian's full name
    ///   - relationship: The relationship to the minor
    /// - Returns: The consent request ID for tracking
    /// - Throws: `GuardianConsentError` on failure
    func sendConsentRequest(
        minorEmail: String,
        guardianEmail: String,
        guardianPhone: String?,
        guardianName: String,
        relationship: GuardianRelationship
    ) async throws -> String

    /// Verify a consent code entered by the guardian.
    /// - Parameters:
    ///   - requestId: The consent request ID
    ///   - code: The 6-digit verification code
    /// - Returns: `true` if consent is verified
    /// - Throws: `GuardianConsentError` on failure
    func verifyConsentCode(requestId: String, code: String) async throws -> Bool

    /// Check the current status of a consent request.
    /// - Parameter requestId: The consent request ID
    /// - Returns: The current consent status
    /// - Throws: `GuardianConsentError.requestNotFound` if not found
    func checkConsentStatus(requestId: String) async throws -> ConsentStatus

    /// Resend the consent request to the guardian.
    /// - Parameter requestId: The existing consent request ID
    /// - Throws: `GuardianConsentError` on failure
    func resendConsentRequest(requestId: String) async throws

    /// Revoke previously granted consent (guardian-initiated).
    /// - Parameter requestId: The consent request ID
    /// - Throws: `GuardianConsentError` on failure
    func revokeConsent(requestId: String) async throws
}

// MARK: - Mock Guardian Consent Service

/// Mock implementation for development, previews, and testing.
final class MockGuardianConsentService: GuardianConsentServiceProtocol, @unchecked Sendable {

    /// The valid verification code for mock testing
    static let mockValidCode = "123456"

    /// Track all requests for inspection
    private(set) var requests: [String: GuardianConsentRequest] = [:]

    /// Configurable failure mode for testing
    var shouldFailSend = false
    var shouldFailVerify = false

    func sendConsentRequest(
        minorEmail: String,
        guardianEmail: String,
        guardianPhone: String?,
        guardianName: String,
        relationship: GuardianRelationship
    ) async throws -> String {
        try await Task.sleep(nanoseconds: 800_000_000)

        if shouldFailSend {
            throw GuardianConsentError.sendFailed(underlying: "Mock send failure")
        }

        guard !guardianEmail.isEmpty else {
            throw GuardianConsentError.guardianEmailRequired
        }

        let requestId = UUID().uuidString
        let request = GuardianConsentRequest(
            id: requestId,
            minorEmail: minorEmail,
            guardianEmail: guardianEmail,
            guardianPhone: guardianPhone,
            guardianName: guardianName,
            relationship: relationship,
            status: .pending,
            createdAt: Date(),
            expiresAt: Date().addingTimeInterval(
                Double(GuardianConsentRequest.defaultExpirationHours) * 3600
            )
        )

        requests[requestId] = request
        print("[MockGuardianConsent] Consent request \(requestId) sent to \(guardianEmail)")
        return requestId
    }

    func verifyConsentCode(requestId: String, code: String) async throws -> Bool {
        try await Task.sleep(nanoseconds: 500_000_000)

        if shouldFailVerify {
            throw GuardianConsentError.codeInvalid
        }

        guard var request = requests[requestId] else {
            throw GuardianConsentError.requestNotFound
        }

        guard request.status == .pending else {
            if request.status == .verified {
                throw GuardianConsentError.alreadyVerified
            }
            throw GuardianConsentError.alreadyDenied
        }

        guard request.expiresAt > Date() else {
            request.status = .expired
            requests[requestId] = request
            throw GuardianConsentError.codeExpired
        }

        guard code == Self.mockValidCode else {
            throw GuardianConsentError.codeInvalid
        }

        request.status = .verified
        request.verifiedAt = Date()
        requests[requestId] = request
        print("[MockGuardianConsent] Consent \(requestId) VERIFIED")
        return true
    }

    func checkConsentStatus(requestId: String) async throws -> ConsentStatus {
        try await Task.sleep(nanoseconds: 300_000_000)

        guard let request = requests[requestId] else {
            throw GuardianConsentError.requestNotFound
        }

        return request.status
    }

    func resendConsentRequest(requestId: String) async throws {
        try await Task.sleep(nanoseconds: 800_000_000)

        guard var request = requests[requestId] else {
            throw GuardianConsentError.requestNotFound
        }

        // Reset expiration
        request.expiresAt = Date().addingTimeInterval(
            Double(GuardianConsentRequest.defaultExpirationHours) * 3600
        )
        requests[requestId] = request
        print("[MockGuardianConsent] Consent request \(requestId) resent")
    }

    func revokeConsent(requestId: String) async throws {
        try await Task.sleep(nanoseconds: 500_000_000)

        guard var request = requests[requestId] else {
            throw GuardianConsentError.requestNotFound
        }

        request.status = .revoked
        requests[requestId] = request
        print("[MockGuardianConsent] Consent \(requestId) REVOKED")
    }
}
