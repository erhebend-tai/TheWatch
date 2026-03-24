import Foundation

struct Shelter: Codable, Hashable, Identifiable {
    let id: String
    var name: String
    var address: String
    var latitude: Double
    var longitude: Double
    var capacity: Int
    var currentOccupancy: Int
    var services: [String] // e.g., ["Medical", "Food", "Water", "Blankets"]
    var phone: String?
    var website: String?
    var isOpen: Bool
    var operatingHours: String?
    var pets: Bool
    var wheelchairAccessible: Bool

    init(
        id: String = UUID().uuidString,
        name: String = "",
        address: String = "",
        latitude: Double = 0,
        longitude: Double = 0,
        capacity: Int = 0,
        currentOccupancy: Int = 0,
        services: [String] = [],
        phone: String? = nil,
        website: String? = nil,
        isOpen: Bool = true,
        operatingHours: String? = nil,
        pets: Bool = false,
        wheelchairAccessible: Bool = true
    ) {
        self.id = id
        self.name = name
        self.address = address
        self.latitude = latitude
        self.longitude = longitude
        self.capacity = capacity
        self.currentOccupancy = currentOccupancy
        self.services = services
        self.phone = phone
        self.website = website
        self.isOpen = isOpen
        self.operatingHours = operatingHours
        self.pets = pets
        self.wheelchairAccessible = wheelchairAccessible
    }

    var availableBeds: Int {
        max(0, capacity - currentOccupancy)
    }

    var occupancyPercentage: Double {
        capacity > 0 ? Double(currentOccupancy) / Double(capacity) : 0
    }
}
