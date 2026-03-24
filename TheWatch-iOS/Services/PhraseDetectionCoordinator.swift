// PhraseDetectionCoordinator — app-level coordinator for phrase detection.
// Runs independently of any screen or ViewModel. Active as long as the user
// has phrase detection enabled, regardless of which view is visible.
// Subscribes to PhraseDetectionService match results and dispatches to SOS pipeline.

import Foundation
import Combine

/// App-scoped coordinator that bridges phrase detection → SOS trigger.
/// This is NOT a ViewModel — it's a singleton that lives at the App level.
/// It runs as long as the app process is alive, independent of any view.
@Observable
final class PhraseDetectionCoordinator {
    static let shared = PhraseDetectionCoordinator()

    /// Whether phrase detection is enabled by the user (persisted preference).
    var isEnabled: Bool {
        get { UserDefaults.standard.bool(forKey: "phraseDetectionEnabled") }
        set {
            UserDefaults.standard.set(newValue, forKey: "phraseDetectionEnabled")
            if newValue { enable() } else { disable() }
        }
    }

    /// Whether the service is currently listening.
    var isListening: Bool { phraseDetectionService.isListening }

    /// Whether on-device recognition is available.
    var isAvailable: Bool { phraseDetectionService.isAvailable }

    /// Last match result — observable from any view.
    private(set) var lastMatch: PhraseMatchResult?

    /// Whether there's an active SOS triggered by phrase detection.
    private(set) var isSOSActive = false

    private let phraseDetectionService = PhraseDetectionService.shared
    private let locationCoordinator = LocationCoordinator.shared
    private var cancellables = Set<AnyCancellable>()

    private init() {
        // Subscribe to match results at app scope — works regardless of which screen is active
        phraseDetectionService.matchResultPublisher
            .receive(on: DispatchQueue.main)
            .sink { [weak self] result in
                self?.handlePhraseMatch(result)
            }
            .store(in: &cancellables)

        // Auto-start if user previously enabled
        if UserDefaults.standard.bool(forKey: "phraseDetectionEnabled") {
            Task {
                let authorized = await phraseDetectionService.requestAuthorization()
                if authorized {
                    phraseDetectionService.startListening()
                }
            }
        }
    }

    // MARK: - Control

    func enable() {
        Task {
            let authorized = await phraseDetectionService.requestAuthorization()
            guard authorized else {
                print("[PhraseCoordinator] Authorization denied — cannot enable")
                return
            }
            phraseDetectionService.startListening()
            print("[PhraseCoordinator] Enabled — listening started")
        }
    }

    func disable() {
        phraseDetectionService.stopListening()
        print("[PhraseCoordinator] Disabled — listening stopped")
    }

    /// Restart if user had it enabled — call on app becoming active.
    func restartIfEnabled() {
        if UserDefaults.standard.bool(forKey: "phraseDetectionEnabled") {
            if !phraseDetectionService.isListening {
                phraseDetectionService.startListening()
            }
        }
    }

    // MARK: - Match Routing

    /// Route phrase match to the appropriate SOS action.
    /// This runs regardless of which screen is active.
    private func handlePhraseMatch(_ result: PhraseMatchResult) {
        lastMatch = result
        guard let phrase = result.matchedPhrase else { return }

        print("[PhraseCoordinator] MATCH: type=\(phrase.type), phrase=\"\(phrase.phraseText)\", confidence=\(result.confidence)")

        switch phrase.type {
        case .duress:
            // Silent SOS — no visible UI, no countdown, no haptics
            isSOSActive = true
            locationCoordinator.escalateToEmergency()
            triggerSilentSOS(description: "Duress phrase detected — silent SOS activated")

        case .clearWord:
            // Cancel active SOS — user confirmed safe
            if isSOSActive {
                isSOSActive = false
                locationCoordinator.deescalateToNormal()
                cancelActiveSOS()
                print("[PhraseCoordinator] Clear word detected — SOS cancelled")
            }

        case .custom:
            // Standard SOS trigger
            isSOSActive = true
            locationCoordinator.escalateToEmergency()
            triggerStandardSOS(description: "Emergency phrase detected")
        }
    }

    // MARK: - SOS Actions

    /// These call into the existing alert service infrastructure.
    /// In production, these would post notifications or call a shared SOSManager.
    /// For now, they post NotificationCenter notifications that any active
    /// ViewModel can observe.

    private func triggerSilentSOS(description: String) {
        NotificationCenter.default.post(
            name: .phraseDetectedSOS,
            object: nil,
            userInfo: [
                "type": "duress",
                "description": description,
                "silent": true,
                "confidence": lastMatch?.confidence ?? 0
            ]
        )
    }

    private func triggerStandardSOS(description: String) {
        NotificationCenter.default.post(
            name: .phraseDetectedSOS,
            object: nil,
            userInfo: [
                "type": "custom",
                "description": description,
                "silent": false,
                "confidence": lastMatch?.confidence ?? 0
            ]
        )
    }

    private func cancelActiveSOS() {
        NotificationCenter.default.post(
            name: .phraseDetectedClearWord,
            object: nil,
            userInfo: [
                "description": "Clear word detected — user confirmed safe"
            ]
        )
    }
}

// MARK: - Notification Names

extension Notification.Name {
    /// Posted when phrase detection triggers an SOS (duress or custom).
    static let phraseDetectedSOS = Notification.Name("com.thewatch.phraseDetectedSOS")
    /// Posted when a clear word cancels an active SOS.
    static let phraseDetectedClearWord = Notification.Name("com.thewatch.phraseDetectedClearWord")
}
