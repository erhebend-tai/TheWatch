// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:    Models/EscalationConfig.swift
// Purpose: Configuration model for auto-escalation timers and 911 escalation.
//          Stores user preferences for how long to wait before escalating an
//          emergency alert to the next tier (community -> 911). Configurable
//          from 5 to 120 minutes. Persisted via SwiftData or UserDefaults.
// Date:    2026-03-24
// Author:  Claude (Anthropic)
// Deps:    Foundation
// Usage:
//   let config = EscalationConfig(timerMinutes: 10, auto911Enabled: true)
//   // config.timerMinutes -> 10
//   // config.auto911Enabled -> true
//   // config.escalationTier -> .community first, then .firstResponder
//
// Related: AutoEscalationService, NG911Service, SettingsView
// Standards: Aligns with NENA i3 NG911 escalation model (next-gen 911)
// ============================================================================

import Foundation

// MARK: - Escalation Tier
/// Represents the escalation hierarchy for emergency alerts.
/// Community volunteers are notified first; if no response within the timer,
/// escalation proceeds to first responders (911/NG911).
enum EscalationTier: String, Codable, CaseIterable {
    case community = "Community"
    case firstResponder = "First Responder"
    case ng911 = "NG-911"

    var displayName: String { rawValue }

    /// Default response window per tier in seconds
    var defaultTimeoutSeconds: TimeInterval {
        switch self {
        case .community: return 300       // 5 min
        case .firstResponder: return 600  // 10 min
        case .ng911: return 0             // immediate once escalated
        }
    }
}

// MARK: - Escalation Config
/// User-configurable escalation settings.
/// Controls how quickly an unacknowledged alert escalates from community
/// responders to 911 auto-dial.
struct EscalationConfig: Codable, Hashable {
    /// Unique config identifier
    var id: String

    /// Minutes before escalating to next tier (5-120)
    var timerMinutes: Int

    /// Whether auto-dial to 911 is enabled after timer expiry
    var auto911Enabled: Bool

    /// Current escalation tier (read at runtime)
    var currentTier: EscalationTier

    /// Whether the user wants a warning prompt before 911 dial
    var showWarningBeforeDial: Bool

    /// Optional custom message to relay to 911 dispatcher via NG911 data payload
    var ng911DataPayload: String?

    /// Timestamp of last configuration change
    var updatedAt: Date

    // MARK: - Validation Constants
    static let minimumTimerMinutes = 5
    static let maximumTimerMinutes = 120
    static let defaultTimerMinutes = 10

    init(
        id: String = UUID().uuidString,
        timerMinutes: Int = EscalationConfig.defaultTimerMinutes,
        auto911Enabled: Bool = true,
        currentTier: EscalationTier = .community,
        showWarningBeforeDial: Bool = true,
        ng911DataPayload: String? = nil,
        updatedAt: Date = Date()
    ) {
        self.id = id
        self.timerMinutes = max(
            EscalationConfig.minimumTimerMinutes,
            min(EscalationConfig.maximumTimerMinutes, timerMinutes)
        )
        self.auto911Enabled = auto911Enabled
        self.currentTier = currentTier
        self.showWarningBeforeDial = showWarningBeforeDial
        self.ng911DataPayload = ng911DataPayload
        self.updatedAt = updatedAt
    }

    /// Validates that the timer is within acceptable bounds
    var isValid: Bool {
        timerMinutes >= EscalationConfig.minimumTimerMinutes &&
        timerMinutes <= EscalationConfig.maximumTimerMinutes
    }

    /// Human-readable description of the escalation timer
    var timerDescription: String {
        if timerMinutes < 60 {
            return "\(timerMinutes) min"
        } else {
            let hours = timerMinutes / 60
            let mins = timerMinutes % 60
            return mins > 0 ? "\(hours)h \(mins)m" : "\(hours)h"
        }
    }
}
