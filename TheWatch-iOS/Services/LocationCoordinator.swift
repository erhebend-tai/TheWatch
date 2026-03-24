// LocationCoordinator — app-level coordinator for location tracking.
// Manages LocationManager mode transitions triggered by SOS pipeline.
// Any SOS trigger (phrase, tap, manual button) calls this to escalate.

import Foundation
import CoreLocation
import Combine

/// Application-scoped coordinator for location tracking.
/// Wraps LocationManager into a clean API that any SOS trigger can call
/// to escalate tracking mode without knowing about CLLocationManager internals.
@Observable
final class LocationCoordinator {
    static let shared = LocationCoordinator()

    /// Current tracking mode.
    private(set) var currentMode: LocationTrackingMode = .normal

    /// Whether tracking is active.
    var isTracking: Bool { locationManager.isAuthorized }

    /// Current user location.
    var currentLocation: CLLocationCoordinate2D? { locationManager.userLocation }

    private let locationManager = LocationManager.shared

    private init() {}

    // MARK: - Tracking Control

    /// Start location tracking in normal mode.
    /// Call this at app startup after permission is granted.
    func startNormalTracking() {
        currentMode = .normal
        locationManager.switchToNormalMode()
        print("[LocationCoordinator] Started NORMAL tracking")
    }

    /// Escalate to emergency mode — called by any SOS trigger.
    func escalateToEmergency() {
        guard currentMode != .emergency else { return }
        currentMode = .emergency
        locationManager.switchToEmergencyMode()
        print("[LocationCoordinator] Escalated to EMERGENCY mode")
    }

    /// Return to normal tracking — called when SOS is cancelled.
    func deescalateToNormal() {
        guard currentMode == .emergency else { return }
        currentMode = .normal
        locationManager.switchToNormalMode()
        print("[LocationCoordinator] De-escalated to NORMAL mode")
    }

    /// Request always authorization (life-safety requirement).
    func requestAlwaysAuthorization() {
        locationManager.requestAlwaysAuthorization()
    }
}
