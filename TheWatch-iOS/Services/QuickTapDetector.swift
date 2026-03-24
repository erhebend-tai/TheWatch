// QuickTapDetector — detects rapid multi-tap patterns for covert SOS triggering.
// Default: 4 taps within 5 seconds triggers SOS.
//
// On iOS, we support multiple trigger types:
// 1. Volume button presses (via AVAudioSession output volume KVO)
// 2. Screen taps (via a SwiftUI gesture modifier)
// 3. Device shake (motionEnded — works even from pocket if app is active)
//
// This is a pure state machine — deterministic, no ML, configurable.

import Foundation
import AVFoundation
import Combine

// MARK: - Models

struct QuickTapEvent {
    let tapCount: Int
    let windowSeconds: TimeInterval
    let triggerType: TapTriggerType
    let timestamp: Date
}

enum TapTriggerType: String {
    case volumeButton = "VOLUME_BUTTON"
    case screenTap = "SCREEN_TAP"
    case deviceShake = "DEVICE_SHAKE"
}

// MARK: - QuickTapDetector

@Observable
final class QuickTapDetector {
    static let shared = QuickTapDetector()

    // Configuration — can be updated from user settings
    var requiredTaps: Int = 4
    var windowDuration: TimeInterval = 5.0

    // State
    private(set) var isEnabled = false

    // Event publisher
    let tapEventPublisher = PassthroughSubject<QuickTapEvent, Never>()

    // Rolling timestamp windows per trigger type
    private var volumeTaps: [Date] = []
    private var screenTaps: [Date] = []
    private var shakeTaps: [Date] = []

    // Cooldown
    private let cooldownDuration: TimeInterval = 3.0
    private var lastTriggerTime: Date = .distantPast

    // Volume button monitoring
    private var volumeObservation: NSKeyValueObservation?
    private var lastKnownVolume: Float = -1

    private init() {}

    // MARK: - Enable / Disable

    func enable() {
        isEnabled = true
        startVolumeMonitoring()
        print("[QuickTapDetector] Enabled: \(requiredTaps) taps in \(windowDuration)s")
    }

    func disable() {
        isEnabled = false
        stopVolumeMonitoring()
        volumeTaps.removeAll()
        screenTaps.removeAll()
        shakeTaps.removeAll()
        print("[QuickTapDetector] Disabled")
    }

    // MARK: - Volume Button Detection

    /// Monitor volume changes via AVAudioSession.
    /// Each volume change (up or down) counts as a "tap".
    /// iOS doesn't let us intercept the actual button press, but we can
    /// observe the output volume changing and count rapid changes.
    private func startVolumeMonitoring() {
        let audioSession = AVAudioSession.sharedInstance()
        do {
            try audioSession.setActive(true)
        } catch {
            print("[QuickTapDetector] Failed to activate audio session: \(error)")
        }

        lastKnownVolume = audioSession.outputVolume

        // KVO on outputVolume
        volumeObservation = audioSession.observe(\.outputVolume, options: [.new]) { [weak self] _, change in
            guard let self, self.isEnabled else { return }
            guard let newVolume = change.newValue else { return }

            // Only count if volume actually changed (not initial observation)
            if self.lastKnownVolume >= 0 && newVolume != self.lastKnownVolume {
                self.recordTap(&self.volumeTaps, type: .volumeButton)
            }
            self.lastKnownVolume = newVolume
        }
    }

    private func stopVolumeMonitoring() {
        volumeObservation?.invalidate()
        volumeObservation = nil
    }

    // MARK: - Screen Tap (called from SwiftUI gesture)

    /// Call this from a transparent overlay gesture or from a dedicated "SOS zone".
    func onScreenTap() {
        guard isEnabled else { return }
        recordTap(&screenTaps, type: .screenTap)
    }

    // MARK: - Device Shake (called from UIResponder.motionEnded)

    /// Call this from a UIHostingController or custom UIWindow subclass
    /// that overrides motionEnded(_:with:).
    func onDeviceShake() {
        guard isEnabled else { return }
        recordTap(&shakeTaps, type: .deviceShake)
    }

    // MARK: - Core Pattern Detection

    private func recordTap(_ tapList: inout [Date], type: TapTriggerType) {
        let now = Date()

        // Check cooldown
        if now.timeIntervalSince(lastTriggerTime) < cooldownDuration { return }

        // Add timestamp
        tapList.append(now)

        // Prune expired timestamps
        let cutoff = now.addingTimeInterval(-windowDuration)
        tapList.removeAll { $0 < cutoff }

        print("[QuickTapDetector] \(type.rawValue): \(tapList.count)/\(requiredTaps) taps in window")

        // Check threshold
        if tapList.count >= requiredTaps {
            print("[QuickTapDetector] TRIGGERED: \(requiredTaps) \(type.rawValue) taps in \(windowDuration)s")

            lastTriggerTime = now
            tapList.removeAll()

            let event = QuickTapEvent(
                tapCount: requiredTaps,
                windowSeconds: windowDuration,
                triggerType: type,
                timestamp: now
            )
            tapEventPublisher.send(event)
        }
    }

    /// Reset all tap state. Call when SOS is cancelled or app state changes.
    func reset() {
        volumeTaps.removeAll()
        screenTaps.removeAll()
        shakeTaps.removeAll()
    }
}
