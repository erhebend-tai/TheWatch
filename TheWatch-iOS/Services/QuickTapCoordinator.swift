// QuickTapCoordinator — app-level coordinator for tap-to-SOS.
// Collects QuickTapEvents from QuickTapDetector and dispatches to SOS pipeline.
// Runs at application scope, independent of any screen.

import Foundation
import Combine

/// Application-scoped coordinator for quick-tap SOS triggering.
///
/// Flow:
/// 1. User rapidly taps volume button (4x in 5s) or shakes device
/// 2. QuickTapDetector emits QuickTapEvent
/// 3. This coordinator catches it, escalates location, fires SOS
/// 4. LocationCoordinator switches to EMERGENCY mode
@Observable
final class QuickTapCoordinator {
    static let shared = QuickTapCoordinator()

    private(set) var isSOSActive = false
    private(set) var lastTapEvent: QuickTapEvent?

    private let quickTapDetector = QuickTapDetector.shared
    private let locationCoordinator = LocationCoordinator.shared
    private let sosTriggerService = SosTriggerService.shared
    private var cancellables = Set<AnyCancellable>()

    /// Whether quick-tap SOS is enabled by the user.
    var isEnabled: Bool {
        get { UserDefaults.standard.bool(forKey: "quickTapSOSEnabled") }
        set {
            UserDefaults.standard.set(newValue, forKey: "quickTapSOSEnabled")
            if newValue { enable() } else { disable() }
        }
    }

    private init() {
        // Collect tap events at app scope
        quickTapDetector.tapEventPublisher
            .receive(on: DispatchQueue.main)
            .sink { [weak self] event in
                self?.handleTapEvent(event)
            }
            .store(in: &cancellables)

        // Auto-enable if user previously enabled
        if UserDefaults.standard.bool(forKey: "quickTapSOSEnabled") {
            quickTapDetector.enable()
        }
    }

    func enable() {
        quickTapDetector.enable()
        print("[QuickTapCoordinator] Quick-tap SOS enabled")
    }

    func disable() {
        quickTapDetector.disable()
        print("[QuickTapCoordinator] Quick-tap SOS disabled")
    }

    private func handleTapEvent(_ event: QuickTapEvent) {
        lastTapEvent = event
        isSOSActive = true

        print("[QuickTapCoordinator] SOS triggered via \(event.triggerType.rawValue): " +
              "\(event.tapCount) taps in \(event.windowSeconds)s")

        // Trigger SOS through the centralized service — handles auth, API, offline queue
        sosTriggerService.trigger(
            source: .quickTap,
            description: "Quick-tap SOS: \(event.tapCount) \(event.triggerType.rawValue) taps"
        )

        // Fire SOS notification — any active ViewModel can observe
        NotificationCenter.default.post(
            name: .quickTapSOS,
            object: nil,
            userInfo: [
                "type": "emergency",
                "description": "Quick-tap SOS: \(event.tapCount) \(event.triggerType.rawValue) taps",
                "trigger_source": "QUICK_TAP",
                "tap_count": event.tapCount
            ]
        )
    }

    /// Cancel SOS triggered by quick-tap.
    func cancelSOS() {
        guard isSOSActive else { return }
        isSOSActive = false
        locationCoordinator.deescalateToNormal()
        quickTapDetector.reset()

        NotificationCenter.default.post(
            name: .quickTapSOSCancelled,
            object: nil
        )
        print("[QuickTapCoordinator] Quick-tap SOS cancelled")
    }
}

// MARK: - Notification Names

extension Notification.Name {
    static let quickTapSOS = Notification.Name("com.thewatch.quickTapSOS")
    static let quickTapSOSCancelled = Notification.Name("com.thewatch.quickTapSOSCancelled")
}
