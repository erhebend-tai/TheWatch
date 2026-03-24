import Foundation

struct User: Codable, Hashable {
    let id: String
    var email: String
    var phone: String
    var firstName: String
    var lastName: String
    var dateOfBirth: Date
    var bloodType: String?
    var medicalConditions: String?
    var medications: String?
    var profileImageURL: String?
    var defaultSeverity: AlertSeverity
    var autoEscalationMinutes: Int
    var auto911Escalation: Bool
    var duressCode: String?
    var personalClearWord: String?
    var checkInSchedule: String?
    var wearableDevices: [WearableDevice]
    var createdAt: Date
    var updatedAt: Date

    var displayName: String {
        "\(firstName) \(lastName)"
    }

    init(
        id: String = UUID().uuidString,
        email: String = "",
        phone: String = "",
        firstName: String = "",
        lastName: String = "",
        dateOfBirth: Date = Date(),
        bloodType: String? = nil,
        medicalConditions: String? = nil,
        medications: String? = nil,
        profileImageURL: String? = nil,
        defaultSeverity: AlertSeverity = .medium,
        autoEscalationMinutes: Int = 5,
        auto911Escalation: Bool = false,
        duressCode: String? = nil,
        personalClearWord: String? = nil,
        checkInSchedule: String? = nil,
        wearableDevices: [WearableDevice] = [],
        createdAt: Date = Date(),
        updatedAt: Date = Date()
    ) {
        self.id = id
        self.email = email
        self.phone = phone
        self.firstName = firstName
        self.lastName = lastName
        self.dateOfBirth = dateOfBirth
        self.bloodType = bloodType
        self.medicalConditions = medicalConditions
        self.medications = medications
        self.profileImageURL = profileImageURL
        self.defaultSeverity = defaultSeverity
        self.autoEscalationMinutes = autoEscalationMinutes
        self.auto911Escalation = auto911Escalation
        self.duressCode = duressCode
        self.personalClearWord = personalClearWord
        self.checkInSchedule = checkInSchedule
        self.wearableDevices = wearableDevices
        self.createdAt = createdAt
        self.updatedAt = updatedAt
    }
}

struct WearableDevice: Codable, Hashable {
    let id: String
    var name: String
    var type: String // e.g., "Apple Watch", "Fitbit"
    var isActive: Bool
    var sensorSettings: [String: Bool] // e.g., ["heartRate": true, "fallDetection": false]
    var implicitDetectionEnabled: Bool

    init(
        id: String = UUID().uuidString,
        name: String = "",
        type: String = "",
        isActive: Bool = true,
        sensorSettings: [String: Bool] = [:],
        implicitDetectionEnabled: Bool = false
    ) {
        self.id = id
        self.name = name
        self.type = type
        self.isActive = isActive
        self.sensorSettings = sensorSettings
        self.implicitDetectionEnabled = implicitDetectionEnabled
    }
}
