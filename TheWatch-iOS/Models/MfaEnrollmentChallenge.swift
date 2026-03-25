// MfaEnrollmentChallenge.swift
// TheWatch-iOS
//
// Model representing a multi-factor authentication enrollment challenge
// returned by the backend POST /api/account/mfa/enroll endpoint.
//
// Example JSON from backend:
// {
//   "method": "totp",
//   "challengeUri": "otpauth://totp/TheWatch:user@example.com?secret=JBSWY3DPEHPK3PXP&issuer=TheWatch",
//   "sessionId": "sess_abc123def456",
//   "backupCodes": ["12345678", "87654321", "11223344", "44332211", "99887766"]
// }
//
// Usage:
//   let challenge = try JSONDecoder().decode(MfaEnrollmentChallenge.self, from: data)
//   // Present QR code from challengeUri for TOTP, or SMS verification for SMS method
//   // Store backupCodes securely for user to save offline

import Foundation

struct MfaEnrollmentChallenge: Codable, Hashable, Sendable {
    /// The MFA method: "totp", "sms", or "backup"
    let method: String

    /// For TOTP: an otpauth:// URI suitable for QR code generation.
    /// For SMS: a masked phone number like "+1***0123".
    let challengeUri: String

    /// Opaque session identifier used to confirm enrollment via
    /// POST /api/account/mfa/enroll/confirm with { sessionId, code }.
    let sessionId: String

    /// One-time backup codes the user must store securely.
    /// Typically 5-10 codes, each 8 alphanumeric characters.
    let backupCodes: [String]

    enum CodingKeys: String, CodingKey {
        case method
        case challengeUri = "challenge_uri"
        case sessionId = "session_id"
        case backupCodes = "backup_codes"
    }

    /// Convenience initializer that also accepts camelCase keys from the backend.
    /// The backend may return either snake_case or camelCase depending on config.
    init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: FlexibleCodingKeys.self)

        method = try container.decode(String.self, forKey: .init("method"))

        // Accept both "challenge_uri" and "challengeUri"
        if let val = try? container.decode(String.self, forKey: .init("challenge_uri")) {
            challengeUri = val
        } else {
            challengeUri = try container.decode(String.self, forKey: .init("challengeUri"))
        }

        // Accept both "session_id" and "sessionId"
        if let val = try? container.decode(String.self, forKey: .init("session_id")) {
            sessionId = val
        } else {
            sessionId = try container.decode(String.self, forKey: .init("sessionId"))
        }

        // Accept both "backup_codes" and "backupCodes"
        if let val = try? container.decode([String].self, forKey: .init("backup_codes")) {
            backupCodes = val
        } else {
            backupCodes = try container.decode([String].self, forKey: .init("backupCodes"))
        }
    }

    init(method: String, challengeUri: String, sessionId: String, backupCodes: [String]) {
        self.method = method
        self.challengeUri = challengeUri
        self.sessionId = sessionId
        self.backupCodes = backupCodes
    }

    func encode(to encoder: Encoder) throws {
        var container = encoder.container(keyedBy: CodingKeys.self)
        try container.encode(method, forKey: .method)
        try container.encode(challengeUri, forKey: .challengeUri)
        try container.encode(sessionId, forKey: .sessionId)
        try container.encode(backupCodes, forKey: .backupCodes)
    }
}

/// Internal flexible coding key for accepting both snake_case and camelCase.
private struct FlexibleCodingKeys: CodingKey {
    var stringValue: String
    var intValue: Int?

    init(_ string: String) {
        self.stringValue = string
        self.intValue = nil
    }

    init?(stringValue: String) {
        self.stringValue = stringValue
        self.intValue = nil
    }

    init?(intValue: Int) {
        self.stringValue = "\(intValue)"
        self.intValue = intValue
    }
}
