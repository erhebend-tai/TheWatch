import Foundation

protocol AuthServiceProtocol {
    func login(emailOrPhone: String, password: String) async throws -> User
    func signup(user: User, password: String, emergencyContacts: [EmergencyContact]) async throws -> User
    func forgotPassword(emailOrPhone: String) async throws -> String
    func resetPassword(emailOrPhone: String, code: String, newPassword: String) async throws
    func logout() async throws
    func biometricLogin() async throws -> User
    var currentUser: User? { get }
    var isAuthenticated: Bool { get }
}

@Observable
final class MockAuthService: AuthServiceProtocol {
    var currentUser: User?
    var isAuthenticated = false

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
        return user
    }

    func signup(user: User, password: String, emergencyContacts: [EmergencyContact]) async throws -> User {
        try await Task.sleep(nanoseconds: 1_000_000_000)
        var newUser = user
        newUser.id = UUID().uuidString
        currentUser = newUser
        isAuthenticated = true
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
        return user
    }
}
