import Foundation
import MapKit
import Combine

@Observable
final class HomeViewModel {
    var userLocation: CLLocationCoordinate2D = CLLocationCoordinate2D(latitude: 37.7749, longitude: -122.4194)
    var nearbyResponders: [Responder] = []
    var communityAlerts: [CommunityAlert] = []
    var activeAlert: Alert?
    var isLoading = false
    var errorMessage: String?
    var sosCountdown = 0
    var isSOS = false
    var showNavigationDrawer = false
    var isPhraseDetectionActive = false
    var lastPhraseMatch: PhraseMatchResult?

    private let alertService: MockAlertService
    private let volunteerService: MockVolunteerService
    private let phraseDetectionService = PhraseDetectionService.shared
    private var sosTimer: Timer?
    private var cancellables = Set<AnyCancellable>()

    init(
        alertService: MockAlertService,
        volunteerService: MockVolunteerService
    ) {
        self.alertService = alertService
        self.volunteerService = volunteerService

        // Subscribe to phrase match results — route to SOS pipeline
        phraseDetectionService.matchResultPublisher
            .receive(on: DispatchQueue.main)
            .sink { [weak self] result in
                self?.handlePhraseMatch(result)
            }
            .store(in: &cancellables)
    }

    // MARK: - Phrase Detection

    func startPhraseDetection() {
        Task {
            let authorized = await phraseDetectionService.requestAuthorization()
            guard authorized else {
                errorMessage = "Speech recognition permission required for phrase detection"
                return
            }
            phraseDetectionService.startListening()
            isPhraseDetectionActive = true
        }
    }

    func stopPhraseDetection() {
        phraseDetectionService.stopListening()
        isPhraseDetectionActive = false
    }

    /// Route phrase match results to the appropriate action:
    /// - Duress → silent SOS (no visible alert on screen)
    /// - ClearWord → cancel active alert
    /// - Custom → standard SOS with countdown
    private func handlePhraseMatch(_ result: PhraseMatchResult) {
        lastPhraseMatch = result
        guard let phrase = result.matchedPhrase else { return }

        print("[HomeViewModel] Phrase match: type=\(phrase.type), text=\"\(phrase.phraseText)\", confidence=\(result.confidence)")

        switch phrase.type {
        case .duress:
            // Silent SOS — skip countdown, no visible UI change
            triggerSilentSOS(description: "Duress phrase detected: silent SOS")

        case .clearWord:
            // Cancel active alert — user confirmed safe
            if activeAlert != nil {
                cancelSOS()
                activeAlert = nil
            }

        case .custom:
            // Standard SOS trigger
            triggerSOSAlert()
        }
    }

    private func triggerSilentSOS(description: String) {
        Task {
            do {
                let alert = try await alertService.createAlert(
                    userId: "user-001",
                    severity: .critical,
                    type: .duress,
                    latitude: userLocation.latitude,
                    longitude: userLocation.longitude,
                    description: description
                )
                self.activeAlert = alert
                // No haptic feedback, no visible countdown — silent
            } catch {
                errorMessage = "Failed to send silent SOS"
            }
        }
    }

    func loadNearbyData() async {
        isLoading = true
        errorMessage = nil

        async let responders = volunteerService.getNearbyResponders(
            latitude: userLocation.latitude,
            longitude: userLocation.longitude,
            radiusMeters: 5000
        )

        async let alerts = alertService.getNearbyAlerts(
            latitude: userLocation.latitude,
            longitude: userLocation.longitude,
            radiusMeters: 5000
        )

        do {
            self.nearbyResponders = try await responders
            self.communityAlerts = try await alerts
        } catch {
            errorMessage = "Failed to load nearby data"
        }

        isLoading = false
    }

    func initiateSOS() {
        sosCountdown = 3
        isSOS = true

        sosTimer = Timer.scheduledTimer(withTimeInterval: 1.0, repeats: true) { [weak self] _ in
            self?.sosCountdown -= 1
            if self?.sosCountdown == 0 {
                self?.sosTimer?.invalidate()
                self?.triggerSOSAlert()
            }
        }

        // Haptic feedback
        let impact = UIImpactFeedbackGenerator(style: .heavy)
        impact.impactOccurred()
    }

    func cancelSOS() {
        sosTimer?.invalidate()
        sosCountdown = 0
        isSOS = false

        let impact = UIImpactFeedbackGenerator(style: .medium)
        impact.impactOccurred()
    }

    private func triggerSOSAlert() {
        Task {
            do {
                let alert = try await alertService.createAlert(
                    userId: "user-001",
                    severity: .critical,
                    type: .sos,
                    latitude: userLocation.latitude,
                    longitude: userLocation.longitude,
                    description: "SOS Button Activated"
                )
                self.activeAlert = alert
                self.isSOS = false

                // Additional haptic pulses
                for _ in 0..<3 {
                    try await Task.sleep(nanoseconds: 200_000_000)
                    let feedback = UINotificationFeedbackGenerator()
                    feedback.notificationOccurred(.Success)
                }
            } catch {
                errorMessage = "Failed to send SOS alert"
            }
        }
    }

    func triggerSOS() async {
        triggerSOSAlert()
    }

    func toggleNavigationDrawer() {
        showNavigationDrawer.toggle()
    }

    deinit {
        sosTimer?.invalidate()
    }
}
