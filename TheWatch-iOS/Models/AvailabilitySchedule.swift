// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:    Models/AvailabilitySchedule.swift
// Purpose: Model for volunteer availability scheduling. Represents a weekly
//          calendar grid where volunteers mark which hours they're available
//          to respond to emergencies. Supports "available now" instant toggle,
//          recurring weekly patterns, and one-off overrides.
// Date:    2026-03-24
// Author:  Claude (Anthropic)
// Deps:    Foundation
// Usage:
//   var schedule = AvailabilitySchedule(userId: "vol-1")
//   schedule.setAvailable(day: .monday, hour: 9, available: true)
//   schedule.setAvailable(day: .monday, hour: 10, available: true)
//   let isFree = schedule.isAvailable(day: .monday, hour: 9) // true
//   schedule.availableNow = true // instant override
//
// Related: VolunteeringView, AvailabilityScheduleView, VolunteerService
// Standards: ISO 8601 weekday numbering (Monday=1, Sunday=7)
// ============================================================================

import Foundation

// MARK: - Weekday
/// Days of the week for availability grid. Ordered Monday-Sunday per ISO 8601.
enum Weekday: Int, Codable, CaseIterable, Identifiable, Comparable {
    case monday = 1
    case tuesday = 2
    case wednesday = 3
    case thursday = 4
    case friday = 5
    case saturday = 6
    case sunday = 7

    var id: Int { rawValue }

    var shortName: String {
        switch self {
        case .monday: return "Mon"
        case .tuesday: return "Tue"
        case .wednesday: return "Wed"
        case .thursday: return "Thu"
        case .friday: return "Fri"
        case .saturday: return "Sat"
        case .sunday: return "Sun"
        }
    }

    var fullName: String {
        switch self {
        case .monday: return "Monday"
        case .tuesday: return "Tuesday"
        case .wednesday: return "Wednesday"
        case .thursday: return "Thursday"
        case .friday: return "Friday"
        case .saturday: return "Saturday"
        case .sunday: return "Sunday"
        }
    }

    static func < (lhs: Weekday, rhs: Weekday) -> Bool {
        lhs.rawValue < rhs.rawValue
    }
}

// MARK: - Time Slot
/// A single hour slot in the availability grid.
struct TimeSlot: Codable, Hashable, Identifiable {
    var id: String { "\(day.rawValue)-\(hour)" }
    var day: Weekday
    var hour: Int // 0-23
    var isAvailable: Bool

    /// Human-readable time range for this slot (e.g., "9:00 AM - 10:00 AM")
    var timeRangeDisplay: String {
        let formatter = DateFormatter()
        formatter.dateFormat = "h:00 a"

        var startComponents = DateComponents()
        startComponents.hour = hour
        let startDate = Calendar.current.date(from: startComponents) ?? Date()

        var endComponents = DateComponents()
        endComponents.hour = (hour + 1) % 24
        let endDate = Calendar.current.date(from: endComponents) ?? Date()

        return "\(formatter.string(from: startDate)) - \(formatter.string(from: endDate))"
    }
}

// MARK: - Availability Schedule
/// Weekly availability grid for a volunteer. 7 days x 24 hours = 168 slots.
struct AvailabilitySchedule: Codable, Hashable, Identifiable {
    var id: String
    var userId: String
    var availableNow: Bool           // Instant toggle override
    var slots: [TimeSlot]            // 168 slots (7 days * 24 hours)
    var timezone: String             // IANA timezone (e.g., "America/Chicago")
    var lastUpdated: Date
    var isDesignatedResponder: Bool   // Part of designated responder program

    init(
        id: String = UUID().uuidString,
        userId: String = "",
        availableNow: Bool = false,
        slots: [TimeSlot]? = nil,
        timezone: String = TimeZone.current.identifier,
        lastUpdated: Date = Date(),
        isDesignatedResponder: Bool = false
    ) {
        self.id = id
        self.userId = userId
        self.availableNow = availableNow
        self.timezone = timezone
        self.lastUpdated = lastUpdated
        self.isDesignatedResponder = isDesignatedResponder

        // Initialize all 168 slots if not provided
        if let provided = slots {
            self.slots = provided
        } else {
            var generated: [TimeSlot] = []
            for day in Weekday.allCases {
                for hour in 0..<24 {
                    generated.append(TimeSlot(day: day, hour: hour, isAvailable: false))
                }
            }
            self.slots = generated
        }
    }

    // MARK: - Slot Access
    /// Check if a specific day/hour is available
    func isAvailable(day: Weekday, hour: Int) -> Bool {
        slots.first(where: { $0.day == day && $0.hour == hour })?.isAvailable ?? false
    }

    /// Set availability for a specific day/hour
    mutating func setAvailable(day: Weekday, hour: Int, available: Bool) {
        if let index = slots.firstIndex(where: { $0.day == day && $0.hour == hour }) {
            slots[index].isAvailable = available
            lastUpdated = Date()
        }
    }

    /// Get all available slots for a given day
    func availableHours(for day: Weekday) -> [Int] {
        slots.filter { $0.day == day && $0.isAvailable }.map(\.hour).sorted()
    }

    /// Total available hours per week
    var totalAvailableHours: Int {
        slots.filter(\.isAvailable).count
    }

    /// Check if the volunteer is currently available based on the schedule
    var isCurrentlyScheduled: Bool {
        let now = Date()
        let calendar = Calendar.current
        let weekdayNumber = calendar.component(.weekday, from: now)
        // Calendar weekday: 1=Sunday, 2=Monday, etc. Convert to ISO 8601.
        let isoWeekday = weekdayNumber == 1 ? 7 : weekdayNumber - 1
        guard let day = Weekday(rawValue: isoWeekday) else { return false }
        let hour = calendar.component(.hour, from: now)
        return isAvailable(day: day, hour: hour)
    }

    /// Effective availability: availableNow OR currently scheduled
    var isEffectivelyAvailable: Bool {
        availableNow || isCurrentlyScheduled
    }

    /// Set entire day available/unavailable
    mutating func setDayAvailability(day: Weekday, available: Bool) {
        for hour in 0..<24 {
            setAvailable(day: day, hour: hour, available: available)
        }
    }

    /// Set a range of hours for a day
    mutating func setRange(day: Weekday, startHour: Int, endHour: Int, available: Bool) {
        for hour in startHour..<endHour {
            setAvailable(day: day, hour: hour, available: available)
        }
    }
}
