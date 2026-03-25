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

    private let alertService: any AlertServiceProtocol
    private let volunteerService: any VolunteerServiceProtocol
    private let apiClient = WatchApiClient.shared
    private let hubConnection = WatchHubConnection.shared
    private let phraseDetectionService = PhraseDetectionService.shared
    private var sosTimer: Timer?
    private var cancellables = Set<AnyCancellable>()
    private var responderPollingTask: Task<Void, Never>?

    init(
        alertService: any AlertServiceProtocol,
        volunteerService: any VolunteerServiceProtocol
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

                // Start polling for responder positions (real-time via SignalR
                // when hub is live; polling as fallback in mock mode)
                startResponderPolling(requestId: alert.id)

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

    /// Start polling for responder locations during an active alert.
    /// This is a fallback when SignalR is in mock mode; the real hub
    /// provides instant ResponderLocationUpdated events.
    func startResponderPolling(requestId: String) {
        responderPollingTask?.cancel()
        responderPollingTask = Task { [weak self] in
            while !Task.isCancelled {
                guard let self else { break }
                do {
                    let situation = try await self.apiClient.getSituation(requestId: requestId)
                    let responders = (situation.responders ?? []).map { ack in
                        Responder(
                            id: ack.responderId ?? "",
                            name: ack.responderName ?? "",
                            role: ResponderRole(rawValue: ack.responderRole ?? "Volunteer") ?? .volunteer,
                            latitude: ack.latitude ?? 0,
                            longitude: ack.longitude ?? 0,
                            distance: ack.distanceMeters ?? 0,
                            skills: [],
                            isVerified: true,
                            responseTime: nil,
                            status: .onCall,
                            availability: .available,
                            hasVehicle: ack.hasVehicle ?? false
                        )
                    }
                    await MainActor.run {
                        self.nearbyResponders = responders
                    }
                } catch {
                    // Polling failure is non-fatal; try again next cycle
                }
                try? await Task.sleep(nanoseconds: 5_000_000_000) // 5 seconds
            }
        }
    }

    func stopResponderPolling() {
        responderPollingTask?.cancel()
        responderPollingTask = nil
    }

    deinit {
        sosTimer?.invalidate()
        responderPollingTask?.cancel()
    }
}
