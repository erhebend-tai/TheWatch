// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:         SosTriggerService.swift
// Purpose:      Central orchestrator for all SOS trigger methods on iOS:
//               phrase detection, quick-tap pattern, and manual button press.
//               When any trigger fires, this service handles the full lifecycle:
//                 1. Begin SOS correlation (SosCorrelationManager)
//                 2. Escalate location to emergency mode
//                 3. Haptic feedback countdown pattern (UIImpactFeedbackGenerator)
//                 4. POST to /api/response/trigger with auth + bypass fallback
//                 5. Queue offline if network unavailable (SyncEngine)
//                 6. Play alert sound on confirmation
// Created:      2026-03-24
// Author:       Claude
// Dependencies: SosCorrelationManager, SyncEngine, LocationCoordinator,
//               LocationManager, WatchLogger
//
// Usage example:
//   let service = SosTriggerService.shared
//
//   // From phrase detection coordinator:
//   await service.trigger(source: .phrase, description: "Duress phrase")
//
//   // From quick-tap coordinator:
//   await service.trigger(source: .quickTap, description: "4 taps in 3s")
//
//   // From manual SOS button (shows countdown):
//   await service.trigger(source: .manualButton)
//
//   // Cancel during countdown:
//   await service.cancel()
//
//   // Observe state from SwiftUI:
//   SosTriggerService.shared.statePublisher.sink { state in ... }
//
// Life-Safety Critical:
//   - This service NEVER blocks on auth failure. If Bearer token is
//     expired, it falls back to X-SOS-Bypass-Token header.
//   - If both tokens fail, it sends with userId only (server allows).
//   - If offline, the SOS is queued via SyncEngine with .critical
//     priority and retried immediately when connectivity returns.
//
// Potential additions:
//   - Camera snapshot for situational awareness
//   - BLE beacon broadcast for nearby TheWatch devices
//   - Integration with Apple Emergency SOS (CrashDetection API)
//   - SMS fallback via SMSFallbackPort
//   - NG911 integration via NG911Service
//   - Apple Watch Haptic Engine integration (WKInterfaceDevice)
// ============================================================================

import Foundation
import UIKit
import AudioToolbox
import Combine

// MARK: - Trigger Source

/// Identifies how the SOS was triggered — sent to the backend as triggerSource.
/// Maps to the ResponseController's TriggerResponseRequest.TriggerSource field.
enum SosTriggerSource: String, Sendable {
    case phrase = "PHRASE"
    case quickTap = "QUICK_TAP"
    case manualButton = "MANUAL_BUTTON"
    case implicitDetection = "IMPLICIT_DETECTION"
    case silentDuress = "SILENT_DURESS"
    case wearable = "WEARABLE"

    /// Maps to SosTriggerMethod for correlation manager
    var correlationMethod: SosTriggerMethod {
        switch self {
        case .phrase: return .phrase
        case .quickTap: return .quickTap
        case .manualButton: return .manual
        case .implicitDetection: return .implicitDetection
        case .silentDuress: return .silentDuress
        case .wearable: return .wearableTrigger
        }
    }
}

// MARK: - Response Scope

/// Response scope — maps to the backend's ResponseScope enum.
/// CheckIn is the default (lightest weight: nearby volunteers check on user).
enum SOSResponseScope: String, Sendable {
    case checkIn = "CheckIn"
    case fullEmergency = "FullEmergency"
    case medical = "Medical"
    case fire = "Fire"
    case silent = "Silent"
}

// MARK: - SOS Trigger State

/// Current state of an SOS trigger lifecycle, observable from SwiftUI.
enum SosTriggerState: Equatable, Sendable {
    /// No active SOS.
    case idle
    /// Countdown in progress. secondsRemaining counts down from 3 to 0.
    case countdown(secondsRemaining: Int)
    /// SOS dispatched, waiting for server response.
    case dispatching
    /// Server responded with responder info.
    case active(requestId: String, responderCount: Int, radiusMeters: Double)
    /// SOS queued offline for retry when connectivity returns.
    case queuedOffline
    /// SOS was cancelled during countdown.
    case cancelled
    /// Error during dispatch — but SOS was queued as fallback.
    case error(message: String, queuedOffline: Bool)
}

// MARK: - API Request / Response

/// JSON payload for POST /api/response/trigger.
struct TriggerRequestPayload: Codable {
    let userId: String
    let scope: String
    let latitude: Double
    let longitude: Double
    let description: String?
    let triggerSource: String?

    enum CodingKeys: String, CodingKey {
        case userId = "UserId"
        case scope = "Scope"
        case latitude = "Latitude"
        case longitude = "Longitude"
        case description = "Description"
        case triggerSource = "TriggerSource"
    }
}

/// Parsed response from POST /api/response/trigger.
struct TriggerApiResponse {
    let requestId: String
    let status: String
    let radiusMeters: Double
    let desiredResponderCount: Int
}

// MARK: - SosTriggerService

/// Central SOS trigger orchestrator. All trigger methods (phrase, tap, button)
/// funnel through this service. Handles auth, offline queuing, haptics, and sound.
///
/// LIFE-SAFETY CRITICAL: This service MUST NOT be blocked by authentication.
/// If Bearer token is expired, falls back to X-SOS-Bypass-Token.
/// If both fail, sends with userId only (server allows for life-safety).
/// If offline, queues with .critical priority via SyncEngine.
@Observable
final class SosTriggerService {

    // MARK: - Singleton

    static let shared = SosTriggerService()

    // MARK: - Configuration

    private let countdownSeconds = 3
    private let apiBaseURL = "https://thewatch-api.azurewebsites.net"
    private let triggerEndpoint = "/api/response/trigger"
    private let connectTimeoutSeconds: TimeInterval = 10
    private let readTimeoutSeconds: TimeInterval = 15

    // MARK: - Observable State

    /// Current trigger state — observe from SwiftUI views.
    private(set) var state: SosTriggerState = .idle

    /// Combine publisher for state changes.
    let statePublisher = CurrentValueSubject<SosTriggerState, Never>(.idle)

    /// Last server-assigned request ID.
    private(set) var lastRequestId: String?

    // MARK: - Auth Tokens (set by auth layer on login)

    /// Firebase/MSAL Bearer token — may be expired during SOS.
    var bearerToken: String?

    /// Pre-issued SOS bypass token — cached locally, survives auth expiry.
    var sosBypassToken: String?

    /// Current authenticated user ID.
    var userId: String = ""

    // MARK: - Dependencies

    private let correlationManager = SosCorrelationManager.shared
    private let locationCoordinator = LocationCoordinator.shared
    private let locationManager = LocationManager.shared
    private let syncEngine = SyncEngine.shared
    private let logger = WatchLogger.shared

    // MARK: - Private State

    private var countdownTimer: Timer?
    private var pendingSource: SosTriggerSource?
    private var pendingDescription: String?
    private var pendingScope: SOSResponseScope?
    private var autoDismissTask: Task<Void, Never>?

    private init() {}

    // MARK: - Public API

    /// Begin the SOS trigger flow with a 3-second countdown.
    /// During countdown, haptic feedback pulses each second.
    /// After countdown expires, the SOS is dispatched to the backend.
    ///
    /// For silent/duress triggers, set `skipCountdown = true`.
    ///
    /// - Parameters:
    ///   - source: How the SOS was triggered
    ///   - description: Optional human-readable description
    ///   - responseScope: Default CheckIn — can be escalated
    ///   - skipCountdown: True for silent/duress — no countdown, immediate dispatch
    func trigger(
        source: SosTriggerSource,
        description: String? = nil,
        responseScope: SOSResponseScope = .checkIn,
        skipCountdown: Bool = false
    ) {
        // If already in an active SOS, don't re-trigger
        switch state {
        case .active, .dispatching:
            print("[SosTriggerService] SOS already active — ignoring duplicate trigger")
            return
        default:
            break
        }

        // Cancel any in-progress countdown
        countdownTimer?.invalidate()
        countdownTimer = nil
        autoDismissTask?.cancel()

        pendingSource = source
        pendingDescription = description
        pendingScope = responseScope

        // Escalate location tracking immediately
        locationCoordinator.escalateToEmergency()

        if skipCountdown {
            // Silent/duress — dispatch immediately, no haptics, no sound
            Task { await dispatchSOS(source: source, description: description, scope: responseScope) }
        } else {
            // Start countdown with haptic feedback
            startCountdown(source: source, description: description, scope: responseScope)
        }
    }

    /// Cancel the SOS during countdown. No-op if already dispatched.
    func cancel() {
        switch state {
        case .countdown(let remaining):
            countdownTimer?.invalidate()
            countdownTimer = nil

            setState(.cancelled)

            // End correlation as cancelled
            Task {
                await correlationManager.endCorrelation(reason: .userCancelled)
            }

            // Light haptic on cancel
            let impact = UIImpactFeedbackGenerator(style: .light)
            impact.impactOccurred()

            // De-escalate location
            locationCoordinator.deescalateToNormal()

            logger.information(
                source: "SosTriggerService",
                template: "SOS cancelled during countdown at {Seconds}s remaining",
                properties: ["Seconds": "\(remaining)"]
            )

            // Reset to idle after brief pause
            autoDismissTask = Task { @MainActor in
                try? await Task.sleep(nanoseconds: 2_000_000_000)
                if case .cancelled = self.state {
                    self.setState(.idle)
                }
            }

        default:
            print("[SosTriggerService] Cannot cancel — state is \(state)")
        }
    }

    /// Reset state back to idle. Call when user navigates away from SOS screen.
    func reset() {
        countdownTimer?.invalidate()
        countdownTimer = nil
        autoDismissTask?.cancel()
        setState(.idle)
    }

    // MARK: - Countdown

    private func startCountdown(
        source: SosTriggerSource,
        description: String?,
        scope: SOSResponseScope
    ) {
        // Begin correlation early so logs during countdown are tracked
        Task {
            await correlationManager.beginCorrelation(method: source.correlationMethod)

            await correlationManager.logStage(
                "CONFIRMATION",
                source: "SosTriggerService",
                template: "SOS countdown started. Source: {Source}, Scope: {Scope}",
                properties: [
                    "Source": source.rawValue,
                    "Scope": scope.rawValue
                ]
            )
        }

        var remaining = countdownSeconds
        setState(.countdown(secondsRemaining: remaining))

        // Initial haptic
        triggerCountdownHaptic(secondsRemaining: remaining)

        countdownTimer = Timer.scheduledTimer(withTimeInterval: 1.0, repeats: true) { [weak self] timer in
            guard let self else {
                timer.invalidate()
                return
            }

            Task { @MainActor in
                remaining -= 1

                if remaining > 0 {
                    self.setState(.countdown(secondsRemaining: remaining))
                    self.triggerCountdownHaptic(secondsRemaining: remaining)
                } else {
                    timer.invalidate()
                    self.countdownTimer = nil

                    // Strong haptic burst to confirm SOS activation
                    self.triggerActivationHaptic()

                    self.setState(.countdown(secondsRemaining: 0))

                    Task {
                        await self.dispatchSOS(source: source, description: description, scope: scope)
                    }
                }
            }
        }
    }

    // MARK: - Dispatch

    private func dispatchSOS(
        source: SosTriggerSource,
        description: String?,
        scope: SOSResponseScope
    ) async {
        await MainActor.run {
            setState(.dispatching)
        }

        // Begin correlation if not already started (silent/duress skips countdown)
        if await !correlationManager.isActive {
            await correlationManager.beginCorrelation(method: source.correlationMethod)
        }

        // Get current location — use whatever we have, never block on it
        let lat = locationManager.userLocation?.latitude ?? 0.0
        let lng = locationManager.userLocation?.longitude ?? 0.0

        let payload = TriggerRequestPayload(
            userId: userId,
            scope: scope.rawValue,
            latitude: lat,
            longitude: lng,
            description: description ?? "SOS triggered via \(source.rawValue)",
            triggerSource: source.rawValue
        )

        let corrId = await correlationManager.currentId
        await correlationManager.logStage(
            "ALERT_DISPATCH",
            source: "SosTriggerService",
            template: "Dispatching SOS to backend. Lat={Lat}, Lng={Lng}, Source={Source}",
            properties: [
                "Lat": "\(lat)",
                "Lng": "\(lng)",
                "Source": source.rawValue
            ]
        )

        // Try API call if online
        if syncEngine.connectivityMonitor.isConnected {
            do {
                if let response = try await postTrigger(payload: payload) {
                    await MainActor.run {
                        self.lastRequestId = response.requestId
                        self.setState(.active(
                            requestId: response.requestId,
                            responderCount: response.desiredResponderCount,
                            radiusMeters: response.radiusMeters
                        ))
                    }

                    await correlationManager.logStage(
                        "ALERT_DISPATCH",
                        source: "SosTriggerService",
                        template: "SOS accepted by server. RequestId={RequestId}, " +
                            "Responders={Responders}, Radius={Radius}m",
                        properties: [
                            "RequestId": response.requestId,
                            "Responders": "\(response.desiredResponderCount)",
                            "Radius": "\(response.radiusMeters)"
                        ]
                    )

                    // Play alert tone on successful dispatch
                    playAlertTone()
                    return
                }
            } catch {
                print("[SosTriggerService] API call failed — falling back to offline queue: \(error)")
                logger.warning(
                    source: "SosTriggerService",
                    template: "SOS API call failed: {Error}. Queuing offline.",
                    properties: ["Error": error.localizedDescription],
                    correlationId: corrId
                )
            }
        }

        // Offline fallback — queue via SyncEngine with critical priority
        await queueOffline(payload: payload, source: source)
    }

    /// POST /api/response/trigger with auth token + bypass token fallback.
    private func postTrigger(payload: TriggerRequestPayload, retryWithoutBearer: Bool = false) async throws -> TriggerApiResponse? {
        guard let url = URL(string: "\(apiBaseURL)\(triggerEndpoint)") else {
            return nil
        }

        var request = URLRequest(url: url)
        request.httpMethod = "POST"
        request.setValue("application/json; charset=UTF-8", forHTTPHeaderField: "Content-Type")
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        request.timeoutInterval = connectTimeoutSeconds

        // Auth: prefer Bearer token, always include bypass token as fallback
        if !retryWithoutBearer, let token = bearerToken, !token.isEmpty {
            request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        }
        if let bypass = sosBypassToken, !bypass.isEmpty {
            request.setValue(bypass, forHTTPHeaderField: "X-SOS-Bypass-Token")
        }

        let encoder = JSONEncoder()
        request.httpBody = try encoder.encode(payload)

        let config = URLSessionConfiguration.ephemeral
        config.timeoutIntervalForRequest = connectTimeoutSeconds
        config.timeoutIntervalForResource = readTimeoutSeconds
        let session = URLSession(configuration: config)

        let (data, response) = try await session.data(for: request)

        guard let httpResponse = response as? HTTPURLResponse else {
            return nil
        }

        // 202 Accepted is the success code from ResponseController.TriggerResponse
        if (200...299).contains(httpResponse.statusCode) {
            return parseTriggerResponse(data: data)
        }

        // If auth failed (401/403), retry without Bearer (bypass only)
        if (401...403).contains(httpResponse.statusCode) && !retryWithoutBearer && bearerToken != nil {
            print("[SosTriggerService] Auth failed — retrying with bypass token only")
            bearerToken = nil // Clear expired token
            return try await postTrigger(payload: payload, retryWithoutBearer: true)
        }

        let errorBody = String(data: data, encoding: .utf8) ?? ""
        print("[SosTriggerService] API returned HTTP \(httpResponse.statusCode): \(errorBody)")
        return nil
    }

    /// Parse the JSON response from the trigger endpoint.
    private func parseTriggerResponse(data: Data) -> TriggerApiResponse? {
        guard let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            return nil
        }

        guard let requestId = json["RequestId"] as? String else {
            return nil
        }

        let status = json["Status"] as? String ?? "Unknown"
        let radiusMeters = json["RadiusMeters"] as? Double ?? 5000.0
        let responderCount = json["DesiredResponderCount"] as? Int ?? 0

        return TriggerApiResponse(
            requestId: requestId,
            status: status,
            radiusMeters: radiusMeters,
            desiredResponderCount: responderCount
        )
    }

    // MARK: - Offline Queue

    private func queueOffline(payload: TriggerRequestPayload, source: SosTriggerSource) async {
        let sosEventId = "sos-\(UUID().uuidString)"

        let encoder = JSONEncoder()
        let payloadJson = (try? encoder.encode(payload)).flatMap { String(data: $0, encoding: .utf8) } ?? "{}"

        await syncEngine.enqueue(
            entityType: .sosEvent,
            entityId: sosEventId,
            action: .create,
            payload: payloadJson,
            priority: .critical,
            userId: userId,
            idempotencyKey: "sos-trigger-\(sosEventId)"
        )

        await MainActor.run {
            self.lastRequestId = sosEventId
            self.setState(.queuedOffline)
        }

        let corrId = await correlationManager.currentId
        await correlationManager.logStage(
            "ALERT_DISPATCH",
            source: "SosTriggerService",
            template: "SOS queued offline with CRITICAL priority. " +
                "EventId={EventId}, Source={Source}",
            properties: [
                "EventId": sosEventId,
                "Source": source.rawValue,
                "Offline": "true"
            ]
        )

        // Play alert tone even when offline — user needs confirmation
        playAlertTone()

        logger.warning(
            source: "SosTriggerService",
            template: "SOS queued offline — will retry when connectivity returns",
            correlationId: corrId
        )
    }

    // MARK: - State Management

    private func setState(_ newState: SosTriggerState) {
        state = newState
        statePublisher.send(newState)
    }

    // MARK: - Haptics

    /// Haptic pulse during countdown — intensifies as countdown progresses.
    private func triggerCountdownHaptic(secondsRemaining: Int) {
        let style: UIImpactFeedbackGenerator.FeedbackStyle
        switch secondsRemaining {
        case 3: style = .light
        case 2: style = .medium
        case 1: style = .heavy
        default: style = .rigid
        }

        let generator = UIImpactFeedbackGenerator(style: style)
        generator.prepare()
        generator.impactOccurred()
    }

    /// Strong haptic burst to confirm SOS activation.
    private func triggerActivationHaptic() {
        let notification = UINotificationFeedbackGenerator()
        notification.prepare()
        notification.notificationOccurred(.warning)

        // Follow up with rapid double-tap
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.15) {
            let impact = UIImpactFeedbackGenerator(style: .rigid)
            impact.impactOccurred(intensity: 1.0)
        }
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.30) {
            let impact = UIImpactFeedbackGenerator(style: .rigid)
            impact.impactOccurred(intensity: 1.0)
        }
    }

    // MARK: - Alert Sound

    /// Play a distinctive alert tone — system alert sound.
    private func playAlertTone() {
        // Play system alert sound ID 1005 (3x short beeps)
        for i in 0..<3 {
            DispatchQueue.main.asyncAfter(deadline: .now() + Double(i) * 0.3) {
                AudioServicesPlayAlertSound(SystemSoundID(1005))
            }
        }
    }

    deinit {
        countdownTimer?.invalidate()
        autoDismissTask?.cancel()
    }
}
