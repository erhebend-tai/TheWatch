// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:         AudioRecordViewModel.swift
// Purpose:      ViewModel for audio recording during incidents. Manages
//               recording state, waveform levels, and submission.
// Created:      2026-03-24
// Author:       Claude
// Dependencies: Foundation, CoreLocation, EvidenceService.swift
//
// Usage Example:
//   @State private var viewModel = AudioRecordViewModel()
//   // viewModel.startRecording(incidentId: "inc-001")
//   // viewModel.stopRecording()
// ============================================================================

import Foundation
import CoreLocation

@Observable
final class AudioRecordViewModel {

    var isRecording = false
    var isSubmitting = false
    var elapsedSeconds: TimeInterval = 0
    var audioLevels: [Float] = []
    var errorMessage: String?
    var capturedEvidence: Evidence?

    private let evidenceService: EvidenceServiceProtocol
    private let locationManager: LocationManagerProtocol?
    private var recordingTimer: Timer?
    private var levelTimer: Timer?
    private var currentIncidentId: String?

    init(
        evidenceService: EvidenceServiceProtocol = MockEvidenceService(),
        locationManager: LocationManagerProtocol? = nil
    ) {
        self.evidenceService = evidenceService
        self.locationManager = locationManager
    }

    func startRecording(incidentId: String) {
        guard !isRecording else { return }
        currentIncidentId = incidentId
        isRecording = true
        elapsedSeconds = 0
        audioLevels = []
        errorMessage = nil
        capturedEvidence = nil

        recordingTimer = Timer.scheduledTimer(withTimeInterval: 1.0, repeats: true) { [weak self] _ in
            self?.elapsedSeconds += 1
        }

        // Simulate waveform levels
        levelTimer = Timer.scheduledTimer(withTimeInterval: 0.1, repeats: true) { [weak self] _ in
            let level = Float.random(in: 0.05...0.95)
            self?.audioLevels.append(level)
            if (self?.audioLevels.count ?? 0) > 100 {
                self?.audioLevels.removeFirst()
            }
        }
    }

    func stopRecording() async {
        recordingTimer?.invalidate()
        recordingTimer = nil
        levelTimer?.invalidate()
        levelTimer = nil
        guard isRecording, let incidentId = currentIncidentId else { return }
        isRecording = false
        isSubmitting = true

        do {
            let location = locationManager?.userLocation
            let evidence = try await evidenceService.recordAudio(
                incidentId: incidentId,
                location: location
            )
            capturedEvidence = evidence
        } catch {
            errorMessage = error.localizedDescription
        }

        isSubmitting = false
    }

    var elapsedFormatted: String {
        let mins = Int(elapsedSeconds) / 60
        let secs = Int(elapsedSeconds) % 60
        return String(format: "%02d:%02d", mins, secs)
    }

    func reset() {
        recordingTimer?.invalidate()
        levelTimer?.invalidate()
        recordingTimer = nil
        levelTimer = nil
        isRecording = false
        isSubmitting = false
        elapsedSeconds = 0
        audioLevels = []
        capturedEvidence = nil
        errorMessage = nil
    }
}
