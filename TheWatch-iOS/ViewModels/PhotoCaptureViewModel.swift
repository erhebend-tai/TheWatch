// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:         PhotoCaptureViewModel.swift
// Purpose:      ViewModel for photo capture during incidents. Manages camera
//               state, annotation, geotag, and submission to evidence chain.
// Created:      2026-03-24
// Author:       Claude
// Dependencies: Foundation, CoreLocation, EvidenceService.swift
//
// Usage Example:
//   @State private var viewModel = PhotoCaptureViewModel()
//   // viewModel.capturePhoto(incidentId: "inc-001")
// ============================================================================

import Foundation
import CoreLocation

@Observable
final class PhotoCaptureViewModel {

    var annotation: String = ""
    var isCaptured = false
    var isSubmitting = false
    var errorMessage: String?
    var capturedEvidence: Evidence?
    var flashMode: FlashMode = .auto
    var cameraPosition: CameraPosition = .back

    enum FlashMode: String, CaseIterable {
        case auto, on, off
        var systemImage: String {
            switch self {
            case .auto: return "bolt.badge.automatic"
            case .on: return "bolt.fill"
            case .off: return "bolt.slash.fill"
            }
        }
    }

    enum CameraPosition: String, CaseIterable {
        case front, back
        var systemImage: String {
            self == .front ? "camera.rotate" : "camera"
        }
    }

    private let evidenceService: EvidenceServiceProtocol
    private let locationManager: LocationManagerProtocol?

    init(
        evidenceService: EvidenceServiceProtocol = MockEvidenceService(),
        locationManager: LocationManagerProtocol? = nil
    ) {
        self.evidenceService = evidenceService
        self.locationManager = locationManager
    }

    func capturePhoto(incidentId: String) async {
        isSubmitting = true
        errorMessage = nil

        do {
            let location = locationManager?.userLocation
            let evidence = try await evidenceService.capturePhoto(
                incidentId: incidentId,
                location: location,
                annotation: annotation.isEmpty ? nil : annotation
            )
            capturedEvidence = evidence
            isCaptured = true
        } catch {
            errorMessage = error.localizedDescription
        }

        isSubmitting = false
    }

    func toggleFlash() {
        flashMode = switch flashMode {
        case .auto: .on
        case .on: .off
        case .off: .auto
        }
    }

    func toggleCamera() {
        cameraPosition = cameraPosition == .back ? .front : .back
    }

    func reset() {
        annotation = ""
        isCaptured = false
        capturedEvidence = nil
        errorMessage = nil
    }
}

// Protocol to abstract LocationManager dependency
protocol LocationManagerProtocol {
    var userLocation: CLLocationCoordinate2D? { get }
}
