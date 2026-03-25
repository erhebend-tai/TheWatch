// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:         SosCountdownViewModel.swift
// Purpose:      ViewModel for the full-screen SOS countdown overlay.
//               Observes SosTriggerService state and exposes it to SwiftUI.
//               Handles auto-dismiss timing and state transitions.
// Created:      2026-03-24
// Author:       Claude
// Dependencies: SosTriggerService, Combine
//
// Usage example:
//   @State var viewModel = SosCountdownViewModel()
//
//   // Start SOS from manual button:
//   viewModel.startSOS(source: .manualButton)
//
//   // Start SOS from phrase detection:
//   viewModel.startSOS(source: .phrase, description: "Duress phrase")
//
//   // Cancel during countdown:
//   viewModel.cancel()
//
//   // Observe in view:
//   Text(viewModel.statusText)
//   if viewModel.showCountdown { Text("\(viewModel.countdownSeconds)") }
//
// Potential additions:
//   - Camera snapshot attachment for SOS report
//   - Voice memo recording during active SOS
//   - Breadcrumb trail visualization
//   - Nearby volunteer ETA display
//   - Emergency services direct-dial button
// ============================================================================

import Foundation
import Combine

/// ViewModel for the full-screen SOS countdown overlay.
/// Bridges SosTriggerService state into SwiftUI-friendly properties.
@Observable
final class SosCountdownViewModel {

    // MARK: - UI State

    /// Whether the countdown overlay should be visible.
    private(set) var isPresented: Bool = false

    /// The current SOS state — directly from SosTriggerService.
    private(set) var triggerState: SosTriggerState = .idle

    /// Countdown seconds remaining (0 when not counting down).
    var countdownSeconds: Int {
        if case .countdown(let remaining) = triggerState {
            return remaining
        }
        return 0
    }

    /// Whether the countdown is actively running.
    var showCountdown: Bool {
        if case .countdown = triggerState { return true }
        return false
    }

    /// Whether the SOS is being dispatched (loading state).
    var isDispatching: Bool {
        triggerState == .dispatching
    }

    /// Whether the SOS is confirmed active (server responded).
    var isActive: Bool {
        if case .active = triggerState { return true }
        return false
    }

    /// Whether the SOS was queued offline.
    var isQueuedOffline: Bool {
        triggerState == .queuedOffline
    }

    /// Whether the SOS was cancelled.
    var isCancelled: Bool {
        triggerState == .cancelled
    }

    /// Whether there was an error.
    var hasError: Bool {
        if case .error = triggerState { return true }
        return false
    }

    /// Human-readable status text for the current state.
    var statusText: String {
        switch triggerState {
        case .idle:
            return ""
        case .countdown(let remaining):
            return remaining > 0 ? "SOS will be sent in \(remaining)s" : "Sending SOS..."
        case .dispatching:
            return "Contacting responders..."
        case .active(_, let count, _):
            return count > 0 ? "\(count) responders being notified" : "Notifying nearby volunteers"
        case .queuedOffline:
            return "SOS queued — will send when online"
        case .cancelled:
            return "SOS cancelled"
        case .error(let msg, let queued):
            return queued ? "Error, but SOS queued for retry" : "Error: \(msg)"
        }
    }

    /// Subtitle text for additional context.
    var subtitleText: String {
        switch triggerState {
        case .active(_, _, let radius):
            return "Search radius: \(Int(radius / 1000))km"
        case .queuedOffline:
            return "Priority: CRITICAL — sends first on reconnect"
        case .dispatching:
            return "Sending your location to nearby volunteers"
        default:
            return ""
        }
    }

    /// Server-assigned request ID if available.
    var requestId: String? {
        if case .active(let id, _, _) = triggerState {
            return id
        }
        return sosTriggerService.lastRequestId
    }

    /// Responder count from server response.
    var responderCount: Int {
        if case .active(_, let count, _) = triggerState {
            return count
        }
        return 0
    }

    /// Search radius from server response.
    var radiusMeters: Double {
        if case .active(_, _, let radius) = triggerState {
            return radius
        }
        return 0
    }

    // MARK: - Dependencies

    private let sosTriggerService = SosTriggerService.shared
    private var cancellables = Set<AnyCancellable>()
    private var autoDismissTask: Task<Void, Never>?

    // MARK: - Init

    init() {
        // Observe state changes from SosTriggerService
        sosTriggerService.statePublisher
            .receive(on: DispatchQueue.main)
            .sink { [weak self] newState in
                self?.triggerState = newState
                self?.handleStateChange(newState)
            }
            .store(in: &cancellables)
    }

    // MARK: - Actions

    /// Start the SOS trigger flow. Presents the countdown overlay.
    ///
    /// - Parameters:
    ///   - source: What initiated the SOS
    ///   - description: Optional description
    ///   - scope: Response scope (defaults to CheckIn)
    ///   - skipCountdown: True for silent/duress triggers
    func startSOS(
        source: SosTriggerSource = .manualButton,
        description: String? = nil,
        scope: SOSResponseScope = .checkIn,
        skipCountdown: Bool = false
    ) {
        isPresented = true
        sosTriggerService.trigger(
            source: source,
            description: description,
            responseScope: scope,
            skipCountdown: skipCountdown
        )
    }

    /// Cancel the SOS during countdown.
    func cancel() {
        sosTriggerService.cancel()
    }

    /// Dismiss the overlay and reset state.
    func dismiss() {
        autoDismissTask?.cancel()
        isPresented = false
        sosTriggerService.reset()
    }

    // MARK: - Private

    private func handleStateChange(_ state: SosTriggerState) {
        autoDismissTask?.cancel()

        switch state {
        case .active:
            // Auto-dismiss after 5 seconds
            autoDismissTask = Task { @MainActor in
                try? await Task.sleep(nanoseconds: 5_000_000_000)
                if self.isActive {
                    self.isPresented = false
                }
            }

        case .cancelled:
            // Auto-dismiss after 1.5 seconds
            autoDismissTask = Task { @MainActor in
                try? await Task.sleep(nanoseconds: 1_500_000_000)
                if self.isCancelled {
                    self.isPresented = false
                    self.sosTriggerService.reset()
                }
            }

        default:
            break
        }
    }

    deinit {
        autoDismissTask?.cancel()
    }
}
