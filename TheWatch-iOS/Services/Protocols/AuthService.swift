import Foundation

// AuthServiceProtocol
//
// The canonical port for all authentication and account-security operations.
// Implementations:
//   - MockAuthService       (below)          — deterministic stubs for SwiftUI previews, UI tests, and the Mock adapter tier
//   - FirebaseAuthService   (Services/)      — production adapter backed by Firebase Auth SDK + backend API
//
// Example usage (protocol-oriented DI):
//   @Environment(MockAuthService.self) var authService   // SwiftUI preview
//   @Environment(FirebaseAuthService.self) var authService // production
//
// Every method is async throws so callers can use structured concurrency.
// ViewModels should call these on @MainActor and surface errors via @Published errorMessage.

protocol AuthServiceProtocol {
    // ── Core authentication ──────────────────────────────────────────
    func login(emailOrPhone: String, password: String) async throws -> User
    func signup(user: User, password: String, emergencyContacts: [EmergencyContact]) async throws -> User
    func forgotPassword(emailOrPhone: String) async throws -> String
    func resetPassword(emailOrPhone: String, code: String, newPassword: String) async throws
    func logout() async throws
    func biometricLogin() async throws -> User

    // ── Email verification ───────────────────────────────────────────
    /// Sends (or re-sends) a verification email to the currently signed-in user.
    func sendEmailVerification() async throws

    /// Reloads the Firebase user record and returns the refreshed app User.
    /// Use after the user taps the verification link so isEmailVerified updates.
    func refreshUser() async throws -> User

    // ── Multi-factor authentication ──────────────────────────────────
    /// Begin MFA enrollment for the given method ("totp" or "sms").
    /// For SMS, pass the phone number; for TOTP, phoneNumber may be nil.
    /// Returns a challenge containing the session ID, QR URI, and backup codes.
    func enrollMfa(method: String, phoneNumber: String?) async throws -> MfaEnrollmentChallenge

    /// Confirm MFA enrollment by submitting the 6-digit code from the authenticator
    /// or SMS message against the enrollment session.
    func confirmMfaEnrollment(sessionId: String, code: String) async throws -> Bool

    /// Verify an MFA code during login. Called after login() returns a user
    /// whose account has MFA enabled.
    /// - Parameters:
    ///   - code: The 6-digit verification code.
    ///   - method: "totp", "sms", or "backup".
    func verifyMfaCode(code: String, method: String) async throws -> Bool

    // ── Observable state ─────────────────────────────────────────────
    var currentUser: User? { get }
    var isAuthenticated: Bool { get }

    /// True when the current Firebase user's email has been verified.
    var isEmailVerified: Bool { get }

    /// True when the backend reports MFA is enabled for this account.
    var isMfaEnabled: Bool { get }
}

@Observable
final class MockAuthService: AuthServiceProtocol {
    var currentUser: User?
    var isAuthenticated = false
    var isEmailVerified = false
    var isMfaEnabled = false

    func login(emailOrPhone: String, password: String) async throws -> User {
        try await Task.sleep(nanoseconds: 1_000_000_000) // Simulate network delay
        let user = User(
            id: "user-001",
            email: "alex@example.com",
            phone: "+1-555-0123",
            firstName: "Alex",
            lastName: "Rivera",
            dateOfBirth: Date(timeIntervalSince1970: 576057600),
            bloodType: "O+",
            medicalConditions: "None reported",
            medications: "Aspirin 81mg daily",
            defaultSeverity: .medium,
            autoEscalationMinutes: 5,
            auto911Escalation: false
        )
        currentUser = user
        isAuthenticated = true
        isEmailVerified = true
        return user
    }

    func signup(user: User, password: String, emergencyContacts: [EmergencyContact]) async throws -> User {
        try await Task.sleep(nanoseconds: 1_000_000_000)
        var newUser = user
        newUser.id = UUID().uuidString
        currentUser = newUser
        isAuthenticated = true
        isEmailVerified = false // New signups need email verification
        return newUser
    }

    func forgotPassword(emailOrPhone: String) async throws -> String {
        try await Task.sleep(nanoseconds: 800_000_000)
        return "123456" // Mock OTP code
    }

    func resetPassword(emailOrPhone: String, code: String, newPassword: String) async throws {
        try await Task.sleep(nanoseconds: 800_000_000)
    }

    func logout() async throws {
        try await Task.sleep(nanoseconds: 500_000_000)
        currentUser = nil
        isAuthenticated = false
        isEmailVerified = false
        isMfaEnabled = false
    }

    func biometricLogin() async throws -> User {
        try await Task.sleep(nanoseconds: 1_000_000_000)
        let user = User(
            id: "user-001",
            email: "alex@example.com",
            phone: "+1-555-0123",
            firstName: "Alex",
            lastName: "Rivera",
            dateOfBirth: Date(timeIntervalSince1970: 576057600),
            bloodType: "O+",
            medicalConditions: "None reported",
            medications: "Aspirin 81mg daily"
        )
        currentUser = user
        isAuthenticated = true
        isEmailVerified = true
        return user
    }

    func sendEmailVerification() async throws {
        try await Task.sleep(nanoseconds: 500_000_000)
        // Mock: no-op, pretend email was sent
    }

    func refreshUser() async throws -> User {
        try await Task.sleep(nanoseconds: 500_000_000)
        isEmailVerified = true
        guard let user = currentUser else {
            throw AuthError.notAuthenticated
        }
        return user
    }

    func enrollMfa(method: String, phoneNumber: String?) async throws -> MfaEnrollmentChallenge {
        try await Task.sleep(nanoseconds: 800_000_000)
        return MfaEnrollmentChallenge(
            method: method,
            challengeUri: "otpauth://totp/TheWatch:mock@example.com?secret=MOCKBASE32SECRET&issuer=TheWatch",
            sessionId: "mock-session-\(UUID().uuidString)",
            backupCodes: ["12345678", "87654321", "11223344", "44332211", "99887766"]
        )
    }

    func confirmMfaEnrollment(sessionId: String, code: String) async throws -> Bool {
        try await Task.sleep(nanoseconds: 800_000_000)
        isMfaEnabled = true
        return true
    }

    func verifyMfaCode(code: String, method: String) async throws -> Bool {
        try await Task.sleep(nanoseconds: 800_000_000)
        return code == "123456" // Mock: accept this specific code
    }
}

// ── Auth Errors ──────────────────────────────────────────────────────
enum AuthError: LocalizedError {
    case notAuthenticated
    case emailNotVerified
    case mfaRequired
    case invalidCredentials
    case networkError(underlying: Error)
    case serverError(message: String)
    case biometricNotAvailable
    case credentialsCacheEmpty

    var errorDescription: String? {
        switch self {
        case .notAuthenticated:
            return "You are not signed in."
        case .emailNotVerified:
            return "Please verify your email address before continuing."
        case .mfaRequired:
            return "Multi-factor authentication is required."
        case .invalidCredentials:
            return "Invalid email or password."
        case .networkError(let underlying):
            return "Network error: \(underlying.localizedDescription)"
        case .serverError(let message):
            return "Server error: \(message)"
        case .biometricNotAvailable:
            return "Biometric authentication is not available on this device."
        case .credentialsCacheEmpty:
            return "No saved credentials found. Please sign in with your password first."
        }
    }
}
