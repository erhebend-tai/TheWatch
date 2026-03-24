// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:    Services/Emergency/NG911Service.swift
// Purpose: Protocol + mock for Next-Generation 911 (NG911) integration.
//          Handles auto-dial to emergency services after escalation timer
//          expires. Supports NG911 data payloads (location, medical info,
//          alert context) sent alongside the voice call via NENA i3 standards.
//          Toggle-able from SettingsView. When enabled and timer expires,
//          the system initiates a 911 call with supplementary data.
// Date:    2026-03-24
// Author:  Claude (Anthropic)
// Deps:    Foundation, UIKit (for tel: URL scheme), Models/EscalationConfig
// Usage:
//   let ng911: NG911ServiceProtocol = MockNG911Service()
//   try await ng911.initiateEmergencyCall(context: callContext)
//   try await ng911.sendSupplementaryData(payload: ng911Payload)
//
// Hexagonal: This is a PORT. Production adapter would use:
//   - CXCallDirectoryManager for call initiation
//   - HTTPS POST to NG911 ECRF/LVF endpoints for location data
//   - Apple CallKit for call state management
// Standards: NENA i3 NG911, RFC 6881 (SIP for emergency calls),
//            NENA-STA-010 (NG911 data formats), FCC 911 mandates
// NOTE: On iOS, programmatic 911 dialing requires user confirmation via
//       system dialog. Cannot bypass this per Apple security policy.
// ============================================================================

import Foundation
#if canImport(UIKit)
import UIKit
#endif

// MARK: - NG911 Call Context
/// Data sent alongside a 911 call to provide dispatchers with context.
/// Aligns with NENA i3 Additional Data Associated with a Call (ADR).
struct NG911CallContext: Codable, Hashable {
    var alertId: String
    var userId: String
    var latitude: Double
    var longitude: Double
    var altitude: Double?
    var horizontalAccuracy: Double?
    var address: String?
    var medicalConditions: String?
    var medications: String?
    var bloodType: String?
    var allergies: String?
    var emergencyPhrase: String?
    var alertSeverity: String
    var alertType: String
    var responderCount: Int
    var elapsedSeconds: TimeInterval
    var timestamp: Date

    init(
        alertId: String = "",
        userId: String = "",
        latitude: Double = 0,
        longitude: Double = 0,
        altitude: Double? = nil,
        horizontalAccuracy: Double? = nil,
        address: String? = nil,
        medicalConditions: String? = nil,
        medications: String? = nil,
        bloodType: String? = nil,
        allergies: String? = nil,
        emergencyPhrase: String? = nil,
        alertSeverity: String = "High",
        alertType: String = "SOS",
        responderCount: Int = 0,
        elapsedSeconds: TimeInterval = 0,
        timestamp: Date = Date()
    ) {
        self.alertId = alertId
        self.userId = userId
        self.latitude = latitude
        self.longitude = longitude
        self.altitude = altitude
        self.horizontalAccuracy = horizontalAccuracy
        self.address = address
        self.medicalConditions = medicalConditions
        self.medications = medications
        self.bloodType = bloodType
        self.allergies = allergies
        self.emergencyPhrase = emergencyPhrase
        self.alertSeverity = alertSeverity
        self.alertType = alertType
        self.responderCount = responderCount
        self.elapsedSeconds = elapsedSeconds
        self.timestamp = timestamp
    }
}

// MARK: - NG911 Service Protocol (Port)
/// Hexagonal port for NG911 emergency call integration.
/// Production adapters would use CallKit, tel: URL scheme, and HTTPS
/// endpoints for NG911 data relay to PSAPs (Public Safety Answering Points).
protocol NG911ServiceProtocol {
    /// Initiate a 911 emergency call with supplementary data.
    /// On iOS this opens the tel: URL which shows a system confirmation dialog.
    func initiateEmergencyCall(context: NG911CallContext) async throws

    /// Send supplementary data payload to NG911 endpoint (NENA i3 ADR).
    /// Called alongside or just before the voice call to provide dispatcher context.
    func sendSupplementaryData(context: NG911CallContext) async throws

    /// Check if NG911 auto-dial is available on this device/region
    func isNG911Available() async throws -> Bool

    /// Cancel a pending auto-dial (user responded before timer expired)
    func cancelPendingDial() async throws

    /// Get the status of the last emergency call attempt
    func getLastCallStatus() async throws -> NG911CallStatus
}

// MARK: - NG911 Call Status
enum NG911CallStatus: String, Codable {
    case idle = "Idle"
    case pending = "Pending"
    case dialing = "Dialing"
    case connected = "Connected"
    case completed = "Completed"
    case failed = "Failed"
    case cancelledByUser = "Cancelled by User"
}

// MARK: - Mock NG911 Service (Adapter)
/// Mock implementation for development/testing. Logs actions to console.
/// NEVER actually dials 911 in mock mode.
@Observable
final class MockNG911Service: NG911ServiceProtocol {
    var lastStatus: NG911CallStatus = .idle
    var pendingContext: NG911CallContext?

    func initiateEmergencyCall(context: NG911CallContext) async throws {
        try await Task.sleep(nanoseconds: 500_000_000)
        pendingContext = context
        lastStatus = .dialing
        print("[MockNG911] SIMULATED 911 call initiated for alert \(context.alertId)")
        print("[MockNG911] Location: \(context.latitude), \(context.longitude)")
        print("[MockNG911] Medical: \(context.medicalConditions ?? "none")")

        // In production, this would call:
        // if let url = URL(string: "tel://911") {
        //     await UIApplication.shared.open(url)
        // }

        // Simulate call completion
        try await Task.sleep(nanoseconds: 1_000_000_000)
        lastStatus = .completed
        print("[MockNG911] SIMULATED 911 call completed")
    }

    func sendSupplementaryData(context: NG911CallContext) async throws {
        try await Task.sleep(nanoseconds: 300_000_000)
        print("[MockNG911] Supplementary data sent to PSAP for alert \(context.alertId)")
        // In production: HTTPS POST to NG911 ECRF/LVF endpoint
    }

    func isNG911Available() async throws -> Bool {
        // In production: check device capabilities, carrier support, region
        return true
    }

    func cancelPendingDial() async throws {
        try await Task.sleep(nanoseconds: 100_000_000)
        pendingContext = nil
        lastStatus = .cancelledByUser
        print("[MockNG911] Pending 911 dial cancelled by user")
    }

    func getLastCallStatus() async throws -> NG911CallStatus {
        return lastStatus
    }
}
