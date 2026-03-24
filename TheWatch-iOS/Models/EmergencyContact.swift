import Foundation

struct EmergencyContact: Codable, Hashable, Identifiable {
    let id: String
    var name: String
    var phone: String
    var email: String
    var relationship: ContactRelationship
    var priority: Int // 1-3, lower is higher priority
    var createdAt: Date

    init(
        id: String = UUID().uuidString,
        name: String = "",
        phone: String = "",
        email: String = "",
        relationship: ContactRelationship = .family,
        priority: Int = 1,
        createdAt: Date = Date()
    ) {
        self.id = id
        self.name = name
        self.phone = phone
        self.email = email
        self.relationship = relationship
        self.priority = priority
        self.createdAt = createdAt
    }
}

enum ContactRelationship: String, Codable, CaseIterable {
    case family = "Family"
    case friend = "Friend"
    case medical = "Medical Professional"
    case neighbor = "Neighbor"
    case coworker = "Coworker"
    case other = "Other"
}
