// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:    Services/CheckInScheduleService.swift
// Purpose: Protocol + mock for check-in schedule management. Users configure
//          recurring check-in reminders (daily, every 12h, every 6h, or custom
//          intervals). If a check-in is missed, nearby volunteers are notified.
//          Uses BGTaskScheduler for reliable background reminder delivery.
// Date:    2026-03-24
// Author:  Claude (Anthropic)
// Deps:    Foundation, BackgroundTasks, UserNotifications
// Usage:
//   let service: CheckInScheduleServiceProtocol = MockCheckInScheduleService()
//   try await service.setSchedule(userId: "u1", frequency: .every12Hours)
//   try await service.recordCheckIn(userId: "u1")
//   let next = try await service.nextCheckInDate(userId: "u1")
//
// Hexagonal: PORT. Production adapter uses BGTaskScheduler, UNUserNotificationCenter,
//            and server-side cron for redundancy. Missed check-ins trigger
//            server-side escalation to nearby volunteers via push notification.
// Standards: Adheres to Apple Human Interface Guidelines for notification cadence.
//            Welfare check patterns from ASIS International guidelines.
// ============================================================================

import Foundation

// MARK: - Check-In Frequency
/// Predefined check-in intervals. Custom allows arbitrary minute intervals.
enum CheckInFrequency: String, Codable, CaseIterable, Identifiable {
    case daily = "Daily"
    case every12Hours = "Every 12 Hours"
    case every6Hours = "Every 6 Hours"
    case custom = "Custom"

    var id: String { rawValue }

    /// Interval in seconds for the frequency
    var intervalSeconds: TimeInterval {
        switch self {
        case .daily: return 24 * 3600
        case .every12Hours: return 12 * 3600
        case .every6Hours: return 6 * 3600
        case .custom: return 0 // caller must provide custom interval
        }
    }

    var displayName: String { rawValue }

    /// SF Symbol icon for the frequency
    var icon: String {
        switch self {
        case .daily: return "sun.max.fill"
        case .every12Hours: return "clock.fill"
        case .every6Hours: return "clock.badge.exclamationmark.fill"
        case .custom: return "slider.horizontal.3"
        }
    }
}

// MARK: - Check-In Schedule
/// Persisted schedule configuration for a user's check-in reminders.
struct CheckInSchedule: Codable, Hashable, Identifiable {
    var id: String
    var userId: String
    var frequency: CheckInFrequency
    var customIntervalMinutes: Int?
    var isEnabled: Bool
    var startTime: Date          // When the first check-in is expected
    var lastCheckIn: Date?       // When the user last checked in
    var nextCheckIn: Date?       // Computed next expected check-in
    var missedCount: Int         // Consecutive missed check-ins
    var graceWindowMinutes: Int  // Extra time before triggering escalation
    var notifyContactsOnMiss: Bool
    var updatedAt: Date

    static let defaultGraceMinutes = 15

    init(
        id: String = UUID().uuidString,
        userId: String = "",
        frequency: CheckInFrequency = .daily,
        customIntervalMinutes: Int? = nil,
        isEnabled: Bool = true,
        startTime: Date = Date(),
        lastCheckIn: Date? = nil,
        nextCheckIn: Date? = nil,
        missedCount: Int = 0,
        graceWindowMinutes: Int = CheckInSchedule.defaultGraceMinutes,
        notifyContactsOnMiss: Bool = true,
        updatedAt: Date = Date()
    ) {
        self.id = id
        self.userId = userId
        self.frequency = frequency
        self.customIntervalMinutes = customIntervalMinutes
        self.isEnabled = isEnabled
        self.startTime = startTime
        self.lastCheckIn = lastCheckIn
        self.nextCheckIn = nextCheckIn
        self.missedCount = missedCount
        self.graceWindowMinutes = graceWindowMinutes
        self.notifyContactsOnMiss = notifyContactsOnMiss
        self.updatedAt = updatedAt
    }

    /// Effective interval in seconds, accounting for custom values
    var effectiveIntervalSeconds: TimeInterval {
        if frequency == .custom, let custom = customIntervalMinutes {
            return TimeInterval(custom * 60)
        }
        return frequency.intervalSeconds
    }
}

// MARK: - Check-In Schedule Service Protocol (Port)
protocol CheckInScheduleServiceProtocol {
    /// Set/update the check-in schedule for a user
    func setSchedule(userId: String, frequency: CheckInFrequency, customMinutes: Int?) async throws

    /// Get the current schedule for a user
    func getSchedule(userId: String) async throws -> CheckInSchedule?

    /// Record that the user has checked in
    func recordCheckIn(userId: String) async throws

    /// Get the next expected check-in date
    func nextCheckInDate(userId: String) async throws -> Date?

    /// Enable or disable the schedule
    func setEnabled(userId: String, enabled: Bool) async throws

    /// Get the number of consecutive missed check-ins
    func missedCheckInCount(userId: String) async throws -> Int

    /// Register BGTask for background check-in reminders
    func registerBackgroundTask() async throws
}

// MARK: - Mock Check-In Schedule Service (Adapter)
@Observable
final class MockCheckInScheduleService: CheckInScheduleServiceProtocol {
    private var schedules: [String: CheckInSchedule] = [:]

    func setSchedule(userId: String, frequency: CheckInFrequency, customMinutes: Int?) async throws {
        try await Task.sleep(nanoseconds: 200_000_000)
        let interval = frequency == .custom
            ? TimeInterval((customMinutes ?? 60) * 60)
            : frequency.intervalSeconds
        let nextDate = Date().addingTimeInterval(interval)

        schedules[userId] = CheckInSchedule(
            userId: userId,
            frequency: frequency,
            customIntervalMinutes: customMinutes,
            isEnabled: true,
            startTime: Date(),
            nextCheckIn: nextDate
        )
        print("[MockCheckIn] Schedule set for \(userId): \(frequency.rawValue)")
    }

    func getSchedule(userId: String) async throws -> CheckInSchedule? {
        try await Task.sleep(nanoseconds: 100_000_000)
        return schedules[userId] ?? CheckInSchedule(
            userId: userId,
            frequency: .daily,
            isEnabled: true,
            startTime: Date(),
            nextCheckIn: Date().addingTimeInterval(24 * 3600)
        )
    }

    func recordCheckIn(userId: String) async throws {
        try await Task.sleep(nanoseconds: 200_000_000)
        if var schedule = schedules[userId] {
            schedule.lastCheckIn = Date()
            schedule.missedCount = 0
            schedule.nextCheckIn = Date().addingTimeInterval(schedule.effectiveIntervalSeconds)
            schedules[userId] = schedule
        }
        print("[MockCheckIn] Check-in recorded for \(userId)")
    }

    func nextCheckInDate(userId: String) async throws -> Date? {
        return schedules[userId]?.nextCheckIn
    }

    func setEnabled(userId: String, enabled: Bool) async throws {
        try await Task.sleep(nanoseconds: 100_000_000)
        schedules[userId]?.isEnabled = enabled
        print("[MockCheckIn] Schedule \(enabled ? "enabled" : "disabled") for \(userId)")
    }

    func missedCheckInCount(userId: String) async throws -> Int {
        return schedules[userId]?.missedCount ?? 0
    }

    func registerBackgroundTask() async throws {
        // In production: BGTaskScheduler.shared.register(...)
        // Identifier: "com.thewatch.checkin-reminder"
        print("[MockCheckIn] Background task registered (mock)")
    }
}
