// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:         SitrepViewModel.swift
// Purpose:      ViewModel for structured situation report submission.
//               Manages form state, validation, and evidence attachment.
// Created:      2026-03-24
// Author:       Claude
// Dependencies: Foundation, CoreLocation, EvidenceService.swift
//
// Usage Example:
//   @State private var viewModel = SitrepViewModel()
//   // viewModel.submitSitrep(incidentId: "inc-001")
// ============================================================================

import Foundation
import CoreLocation

@Observable
final class SitrepViewModel {

    var situationType: SituationType = .other
    var severity: SitrepSeverity = .medium
    var description: String = ""
    var attachedEvidenceIds: [UUID] = []
    var availableEvidence: [Evidence] = []
    var isSubmitting = false
    var errorMessage: String?
    var submittedEvidence: Evidence?
    var showSuccess = false

    private let evidenceService: EvidenceServiceProtocol
    private let locationManager: LocationManagerProtocol?

    init(
        evidenceService: EvidenceServiceProtocol = MockEvidenceService(),
        locationManager: LocationManagerProtocol? = nil
    ) {
        self.evidenceService = evidenceService
        self.locationManager = locationManager
    }

    func loadAvailableEvidence(incidentId: String) async {
        do {
            let items = try await evidenceService.getEvidenceForIncident(incidentId)
            availableEvidence = items.filter { $0.type == .photo || $0.type == .video }
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    func toggleAttachment(_ evidenceId: UUID) {
        if attachedEvidenceIds.contains(evidenceId) {
            attachedEvidenceIds.removeAll { $0 == evidenceId }
        } else {
            attachedEvidenceIds.append(evidenceId)
        }
    }

    func submitSitrep(incidentId: String) async {
        guard isValid else {
            errorMessage = "Please fill in all required fields"
            return
        }

        isSubmitting = true
        errorMessage = nil

        do {
            let location = locationManager?.userLocation
            let evidence = try await evidenceService.submitSitrep(
                incidentId: incidentId,
                situationType: situationType,
                severity: severity,
                description: description,
                attachedEvidenceIds: attachedEvidenceIds,
                location: location
            )
            submittedEvidence = evidence
            showSuccess = true
        } catch {
            errorMessage = error.localizedDescription
        }

        isSubmitting = false
    }

    var isValid: Bool {
        !description.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
    }

    var wordCount: Int {
        description.split(separator: " ").count
    }

    func reset() {
        situationType = .other
        severity = .medium
        description = ""
        attachedEvidenceIds = []
        submittedEvidence = nil
        showSuccess = false
        errorMessage = nil
    }
}
