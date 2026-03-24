// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:         EvidenceGalleryViewModel.swift
// Purpose:      ViewModel for the evidence gallery view. Manages evidence list,
//               filtering by type, chain verification, and deletion.
// Created:      2026-03-24
// Author:       Claude
// Dependencies: Foundation, Combine, Evidence.swift, EvidenceService.swift
//
// Usage Example:
//   @State private var viewModel = EvidenceGalleryViewModel()
//   // In .task: await viewModel.loadEvidence(for: "incident-001")
// ============================================================================

import Foundation
import Combine

@Observable
final class EvidenceGalleryViewModel {

    // MARK: - Published State

    var evidenceItems: [Evidence] = []
    var filteredItems: [Evidence] = []
    var selectedFilter: EvidenceType? = nil
    var isLoading = false
    var errorMessage: String?
    var chainVerification: VerificationResult?
    var isVerifying = false
    var storageUsedBytes: Int64 = 0
    var showDeleteConfirmation = false
    var itemToDelete: Evidence?

    // MARK: - Dependencies

    private let evidenceService: EvidenceServiceProtocol
    private var incidentId: String = ""
    private var cancellables = Set<AnyCancellable>()

    init(evidenceService: EvidenceServiceProtocol = MockEvidenceService()) {
        self.evidenceService = evidenceService
    }

    // MARK: - Load

    func loadEvidence(for incidentId: String) async {
        self.incidentId = incidentId
        isLoading = true
        errorMessage = nil

        do {
            evidenceItems = try await evidenceService.getEvidenceForIncident(incidentId)
            applyFilter()
            storageUsedBytes = try await evidenceService.getStorageUsed(incidentId: incidentId)
        } catch {
            errorMessage = error.localizedDescription
        }

        isLoading = false
    }

    // MARK: - Filter

    func setFilter(_ type: EvidenceType?) {
        selectedFilter = type
        applyFilter()
    }

    private func applyFilter() {
        if let filter = selectedFilter {
            filteredItems = evidenceItems.filter { $0.type == filter }
        } else {
            filteredItems = evidenceItems
        }
    }

    // MARK: - Verify Chain

    func verifyChain() async {
        isVerifying = true
        do {
            chainVerification = try await evidenceService.verifyChain(incidentId: incidentId)
        } catch {
            chainVerification = VerificationResult(
                isValid: false,
                message: "Verification error: \(error.localizedDescription)"
            )
        }
        isVerifying = false
    }

    // MARK: - Delete

    func confirmDelete(_ evidence: Evidence) {
        itemToDelete = evidence
        showDeleteConfirmation = true
    }

    func deleteConfirmed() async {
        guard let item = itemToDelete else { return }
        do {
            try await evidenceService.deleteEvidence(item.id)
            await loadEvidence(for: incidentId)
        } catch {
            errorMessage = error.localizedDescription
        }
        itemToDelete = nil
    }

    // MARK: - Computed

    var photoCount: Int { evidenceItems.filter { $0.type == .photo }.count }
    var videoCount: Int { evidenceItems.filter { $0.type == .video }.count }
    var audioCount: Int { evidenceItems.filter { $0.type == .audio }.count }
    var sitrepCount: Int { evidenceItems.filter { $0.type == .sitrep }.count }

    var storageFormatted: String {
        ByteCountFormatter.string(fromByteCount: storageUsedBytes, countStyle: .file)
    }
}
