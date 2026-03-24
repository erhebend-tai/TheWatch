import Foundation

protocol UserServiceProtocol {
    func getUser(userId: String) async throws -> User
    func updateUser(_ user: User) async throws -> User
    func getEmergencyContacts(userId: String) async throws -> [EmergencyContact]
    func addEmergencyContact(_ contact: EmergencyContact, userId: String) async throws
    func updateEmergencyContact(_ contact: EmergencyContact, userId: String) async throws
    func deleteEmergencyContact(contactId: String, userId: String) async throws
}

@Observable
final class MockUserService: UserServiceProtocol {
    func getUser(userId: String) async throws -> User {
        try await Task.sleep(nanoseconds: 300_000_000)
        return User(
            id: userId,
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
            auto911Escalation: false,
            wearableDevices: [
                WearableDevice(
                    id: "device-001",
                    name: "Apple Watch Series 8",
                    type: "Apple Watch",
                    isActive: true,
                    sensorSettings: ["heartRate": true, "fallDetection": true, "temperature": false],
                    implicitDetectionEnabled: true
                )
            ]
        )
    }

    func updateUser(_ user: User) async throws -> User {
        try await Task.sleep(nanoseconds: 400_000_000)
        return user
    }

    func getEmergencyContacts(userId: String) async throws -> [EmergencyContact] {
        try await Task.sleep(nanoseconds: 300_000_000)
        return [
            EmergencyContact(
                id: "contact-001",
                name: "Maria Rivera",
                phone: "+1-555-0124",
                email: "maria@example.com",
                relationship: .family,
                priority: 1
            ),
            EmergencyContact(
                id: "contact-002",
                name: "Dr. James Chen",
                phone: "+1-555-0125",
                email: "dr.chen@example.com",
                relationship: .medical,
                priority: 2
            ),
            EmergencyContact(
                id: "contact-003",
                name: "Jordan Smith",
                phone: "+1-555-0126",
                email: "jordan@example.com",
                relationship: .friend,
                priority: 3
            )
        ]
    }

    func addEmergencyContact(_ contact: EmergencyContact, userId: String) async throws {
        try await Task.sleep(nanoseconds: 300_000_000)
    }

    func updateEmergencyContact(_ contact: EmergencyContact, userId: String) async throws {
        try await Task.sleep(nanoseconds: 300_000_000)
    }

    func deleteEmergencyContact(contactId: String, userId: String) async throws {
        try await Task.sleep(nanoseconds: 300_000_000)
    }
}
