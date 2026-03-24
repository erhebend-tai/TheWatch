// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:         VideoCaptureViewModel.swift
// Purpose:      ViewModel for video capture during incidents. Manages recording
//               state, countdown timer, max duration enforcement, and submission.
// Created:      2026-03-24
// Author:       Claude
// Dependencies: Foundation, CoreLocation, EvidenceService.swift
//
// Usage Example:
//   @State private var viewModel = VideoCaptureViewModel()
//   // viewModel.startRecording(incidentId: "inc-001")
//   // viewModel.stopRecording()
// ============================================================================

import Foundation
import CoreLocation

@Observable
final class VideoCaptureViewModel {

    var isRecording = false
    var isSubmitting = false
    var elapsedSeconds: TimeInterval = 0
    var maxDuration: TimeInterval = 60
    var errorMessage: String?
    var capturedEvidence: Evidence?

    private let evidenceService: EvidenceServiceProtocol
    private let locationManager: LocationManagerProtocol?
    private var recordingTimer: Timer?
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
        errorMessage = nil
        capturedEvidence = nil

        recordingTimer = Timer.scheduledTimer(withTimeInterval: 1.0, repeats: true) { [weak self] _ in
            guard let self else { return }
            self.elapsedSeconds += 1
            if self.elapsedSeconds >= self.maxDuration {
                Task { await self.stopRecording() }
            }
        }
    }

    func stopRecording() async {
        recordingTimer?.invalidate()
        recordingTimer = nil
        guard isRecording, let incidentId = currentIncidentId else { return }
        isRecording = false
        isSubmitting = true

        do {
            let location = locationManager?.userLocation
            let evidence = try await evidenceService.captureVideo(
                incidentId: incidentId,
                location: location,
                maxDuration: maxDuration
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

    var remainingFormatted: String {
        let remaining = max(0, maxDuration - elapsedSeconds)
        let mins = Int(remaining) / 60
        let secs = Int(remaining) % 60
        return String(format: "%02d:%02d", mins, secs)
    }

    var progress: Double {
        guard maxDuration > 0 else { return 0 }
        return elapsedSeconds / maxDuration
    }

    func reset() {
        recordingTimer?.invalidate()
        recordingTimer = nil
        isRecording = false
        isSubmitting = false
        elapsedSeconds = 0
        capturedEvidence = nil
        errorMessage = nil
    }
}
