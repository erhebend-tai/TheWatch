// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:    Services/AutoEscalationService.swift
// Purpose: Protocol + mock for auto-escalation timer management. Manages the
//          countdown from when an alert is raised to when it should escalate
//          to the next tier (community -> 911). Timer range: 5-120 minutes.
//          Uses EscalationConfig model for persistence. Integrates with
//          NG911Service for final escalation step.
// Date:    2026-03-24
// Author:  Claude (Anthropic)
// Deps:    Foundation, Models/EscalationConfig, Services/Emergency/NG911Service
// Usage:
//   let service: AutoEscalationServiceProtocol = MockAutoEscalationService()
//   try await service.startEscalationTimer(for: "alert-123", config: config)
//   try await service.cancelEscalation(for: "alert-123")
//   let remaining = try await service.remainingTime(for: "alert-123")
//
// Hexagonal: This is a PORT. Adapters for production would use background
//            timers, push notifications, and server-side escalation logic.
// Standards: NENA i3 NG911, APCO P25 CAD interface patterns
// ============================================================================

import Foundation

// MARK: - Auto-Escalation Service Protocol (Port)
/// Hexagonal port for managing auto-escalation timers.
/// Production adapters would integrate with server-side escalation engines,
/// Apple Push Notification triggers, and NG911 gateway APIs.
protocol AutoEscalationServiceProtocol {
    /// Start an escalation timer for a given alert
    /// - Parameters:
    ///   - alertId: The alert to monitor
    ///   - config: Escalation configuration (timer duration, 911 toggle, etc.)
    func startEscalationTimer(for alertId: String, config: EscalationConfig) async throws

    /// Cancel an active escalation timer (e.g., alert was acknowledged)
    func cancelEscalation(for alertId: String) async throws

    /// Get remaining seconds on the escalation timer
    func remainingTime(for alertId: String) async throws -> TimeInterval

    /// Get the current escalation config for the user
    func getConfig(userId: String) async throws -> EscalationConfig

    /// Update the escalation config
    func updateConfig(_ config: EscalationConfig, userId: String) async throws

    /// Check if an escalation timer is currently active
    func isEscalationActive(for alertId: String) async throws -> Bool

    /// Manually escalate an alert to the next tier immediately
    func escalateNow(alertId: String) async throws
}

// MARK: - Mock Auto-Escalation Service (Adapter)
/// Mock implementation for development and preview. Simulates timer behavior
/// with in-memory state. Replace with production adapter that uses
/// BGTaskScheduler + server-side timer for reliability.
@Observable
final class MockAutoEscalationService: AutoEscalationServiceProtocol {
    // In-memory timer tracking: alertId -> (startTime, configuredDuration)
    private var activeTimers: [String: (start: Date, durationSeconds: TimeInterval)] = [:]
    private var configs: [String: EscalationConfig] = [:]

    func startEscalationTimer(for alertId: String, config: EscalationConfig) async throws {
        try await Task.sleep(nanoseconds: 100_000_000)
        let duration = TimeInterval(config.timerMinutes * 60)
        activeTimers[alertId] = (start: Date(), durationSeconds: duration)
        print("[MockAutoEscalation] Timer started for alert \(alertId): \(config.timerMinutes) min")
    }

    func cancelEscalation(for alertId: String) async throws {
        try await Task.sleep(nanoseconds: 100_000_000)
        activeTimers.removeValue(forKey: alertId)
        print("[MockAutoEscalation] Timer cancelled for alert \(alertId)")
    }

    func remainingTime(for alertId: String) async throws -> TimeInterval {
        guard let timer = activeTimers[alertId] else { return 0 }
        let elapsed = Date().timeIntervalSince(timer.start)
        return max(0, timer.durationSeconds - elapsed)
    }

    func getConfig(userId: String) async throws -> EscalationConfig {
        try await Task.sleep(nanoseconds: 200_000_000)
        return configs[userId] ?? EscalationConfig()
    }

    func updateConfig(_ config: EscalationConfig, userId: String) async throws {
        try await Task.sleep(nanoseconds: 200_000_000)
        configs[userId] = config
        print("[MockAutoEscalation] Config updated for user \(userId): \(config.timerMinutes) min, 911=\(config.auto911Enabled)")
    }

    func isEscalationActive(for alertId: String) async throws -> Bool {
        guard let timer = activeTimers[alertId] else { return false }
        let elapsed = Date().timeIntervalSince(timer.start)
        return elapsed < timer.durationSeconds
    }

    func escalateNow(alertId: String) async throws {
        try await Task.sleep(nanoseconds: 300_000_000)
        activeTimers.removeValue(forKey: alertId)
        print("[MockAutoEscalation] IMMEDIATE ESCALATION for alert \(alertId)")
    }
}
