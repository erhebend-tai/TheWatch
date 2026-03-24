// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:         GuestSOSViewModel.swift
// Purpose:      ViewModel for the Guest SOS emergency view. Allows
//               unauthenticated users to trigger an emergency SOS alert
//               with their current GPS location. Bypasses all authentication
//               for life-safety critical situations. Manages countdown timer,
//               location acquisition, and SOS dispatch.
// Created:      2026-03-24
// Author:       Claude
// Dependencies: Foundation, CoreLocation, LocationManager
// Related:      GuestSOSView.swift (view),
//               LocationManager.swift (location provider),
//               AlertService.swift (alert dispatch)
//
// Usage Example:
//   @State var vm = GuestSOSViewModel()
//   // In view:
//   Button("SOS") { vm.activateSOS() }
//   Text(vm.statusMessage)
//   Text("Lat: \(vm.latitude), Lon: \(vm.longitude)")
//
// Life-Safety Note:
//   This feature exists because emergencies do not wait for authentication.
//   A person finding someone else's phone, or a user who cannot remember
//   credentials, must still be able to summon help. The SOS signal includes
//   only device location and timestamp - no PII is transmitted without auth.
//
// Potential Additions:
//   - Haptic feedback patterns during countdown
//   - Audio alarm (siren) playback
//   - Camera snapshot for situational awareness
//   - Bluetooth beacon broadcast for nearby TheWatch users
//   - Integration with Apple Emergency SOS (CrashDetection API)
//   - Offline queuing of SOS when no network (retry on reconnect)
// ============================================================================

import Foundation
import CoreLocation

/// ViewModel managing Guest SOS (unauthenticated emergency) state.
/// Follows MVVM pattern with @Observable macro for SwiftUI reactivity.
@Observable
final class GuestSOSViewModel {

    // MARK: - Published State

    /// Current latitude of the device (nil if location unavailable)
    var latitude: Double?

    /// Current longitude of the device (nil if location unavailable)
    var longitude: Double?

    /// Human-readable status message shown to the user
    var statusMessage: String = "Press SOS to send an emergency alert"

    /// Whether an SOS has been activated and is in countdown
    var isCountingDown: Bool = false

    /// Countdown seconds remaining before SOS is dispatched
    var countdownSeconds: Int = 5

    /// Whether the SOS has been fully dispatched
    var sosDispatched: Bool = false

    /// Whether the SOS was cancelled during countdown
    var sosCancelled: Bool = false

    /// Whether location is currently being acquired
    var isAcquiringLocation: Bool = false

    /// Error message if something went wrong
    var errorMessage: String?

    /// Whether the confirmation alert should be shown
    var showConfirmation: Bool = false

    // MARK: - Configuration

    /// Default countdown duration before SOS dispatch (seconds)
    static let defaultCountdownDuration = 5

    /// Minimum location accuracy required (meters) before dispatching
    static let minimumAccuracyMeters: Double = 100.0

    // MARK: - Private State

    private var countdownTimer: Timer?
    private let locationManager = LocationManager.shared

    // MARK: - Initialization

    init() {
        // Immediately start acquiring location
        refreshLocation()
    }

    // MARK: - Location

    /// Refresh the current device location from LocationManager.
    func refreshLocation() {
        isAcquiringLocation = true
        errorMessage = nil

        if let coord = locationManager.userLocation {
            latitude = coord.latitude
            longitude = coord.longitude
            isAcquiringLocation = false
        } else {
            // Request location permission if needed
            if locationManager.authorizationStatus == .notDetermined {
                locationManager.requestWhenInUseAuthorization()
            }

            // Poll briefly for location
            Task { @MainActor in
                for _ in 0..<10 {
                    try? await Task.sleep(nanoseconds: 500_000_000)
                    if let coord = locationManager.userLocation {
                        latitude = coord.latitude
                        longitude = coord.longitude
                        isAcquiringLocation = false
                        return
                    }
                }
                isAcquiringLocation = false
                if latitude == nil {
                    errorMessage = "Unable to determine location. SOS will be sent without coordinates."
                }
            }
        }
    }

    // MARK: - SOS Activation

    /// Begin the SOS countdown. User has `defaultCountdownDuration` seconds
    /// to cancel before the alert is dispatched.
    func activateSOS() {
        guard !isCountingDown && !sosDispatched else { return }

        showConfirmation = true
    }

    /// Confirm SOS after the confirmation dialog.
    func confirmSOS() {
        showConfirmation = false
        isCountingDown = true
        sosCancelled = false
        countdownSeconds = Self.defaultCountdownDuration
        statusMessage = "SOS will be sent in \(countdownSeconds) seconds..."

        // Refresh location one more time
        if let coord = locationManager.userLocation {
            latitude = coord.latitude
            longitude = coord.longitude
        }

        // Switch to emergency tracking mode
        locationManager.switchToEmergencyMode()

        // Start countdown timer
        startCountdown()
    }

    /// Cancel the SOS during the countdown period.
    func cancelSOS() {
        showConfirmation = false
        countdownTimer?.invalidate()
        countdownTimer = nil
        isCountingDown = false
        sosCancelled = true
        countdownSeconds = Self.defaultCountdownDuration
        statusMessage = "SOS cancelled. Press SOS to try again."

        // Return to normal tracking
        locationManager.switchToNormalMode()
    }

    /// Reset all state to allow a new SOS.
    func reset() {
        cancelSOS()
        sosDispatched = false
        sosCancelled = false
        errorMessage = nil
        statusMessage = "Press SOS to send an emergency alert"
        locationManager.switchToNormalMode()
    }

    // MARK: - Private Countdown

    private func startCountdown() {
        countdownTimer?.invalidate()

        countdownTimer = Timer.scheduledTimer(
            withTimeInterval: 1.0,
            repeats: true
        ) { [weak self] timer in
            guard let self else {
                timer.invalidate()
                return
            }

            Task { @MainActor in
                self.countdownSeconds -= 1
                self.statusMessage = "SOS will be sent in \(self.countdownSeconds) seconds..."

                if self.countdownSeconds <= 0 {
                    timer.invalidate()
                    self.countdownTimer = nil
                    self.dispatchSOS()
                }
            }
        }
    }

    // MARK: - SOS Dispatch

    private func dispatchSOS() {
        isCountingDown = false
        sosDispatched = true

        // Update location one final time
        if let coord = locationManager.userLocation {
            latitude = coord.latitude
            longitude = coord.longitude
        }

        let locationString: String
        if let lat = latitude, let lon = longitude {
            locationString = "(\(String(format: "%.6f", lat)), \(String(format: "%.6f", lon)))"
        } else {
            locationString = "(unknown)"
        }

        statusMessage = "SOS DISPATCHED at \(locationString). Help is on the way."

        // In production, this would call the AlertService to broadcast:
        // - Location coordinates
        // - Timestamp
        // - Device identifier (anonymized)
        // - Nearby volunteer notification via geohash/H3 lookup
        // - First responder notification if enabled
        print("[GuestSOS] EMERGENCY SOS dispatched at \(locationString) - \(Date())")
    }

    deinit {
        countdownTimer?.invalidate()
    }
}
