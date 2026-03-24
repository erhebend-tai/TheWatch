// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:         EvidenceGalleryView.swift
// Purpose:      Grid view of all evidence for an incident. Filter by type,
//               tap for detail, chain-of-custody verification badge.
// Created:      2026-03-24
// Author:       Claude
// Dependencies: SwiftUI, EvidenceGalleryViewModel.swift, Evidence.swift
//
// Usage Example:
//   EvidenceGalleryView(incidentId: "incident-001")
//
// Accessibility:
//   - VoiceOver labels on all items and controls
//   - Dynamic type support
//   - 44pt minimum touch targets
// ============================================================================

import SwiftUI

struct EvidenceGalleryView: View {
    let incidentId: String
    @State private var viewModel = EvidenceGalleryViewModel()
    @Environment(\.dismiss) private var dismiss

    private let columns = [
        GridItem(.flexible(), spacing: 12),
        GridItem(.flexible(), spacing: 12),
        GridItem(.flexible(), spacing: 12)
    ]

    var body: some View {
        NavigationStack {
            ZStack {
                Color(red: 0.97, green: 0.97, blue: 0.97)
                    .ignoresSafeArea()

                ScrollView {
                    VStack(spacing: 16) {
                        // Stats bar
                        statsBar

                        // Filter chips
                        filterChips

                        // Chain verification banner
                        if let verification = viewModel.chainVerification {
                            verificationBanner(verification)
                        }

                        // Evidence grid
                        if viewModel.isLoading {
                            ProgressView("Loading evidence...")
                                .padding(40)
                        } else if viewModel.filteredItems.isEmpty {
                            emptyState
                        } else {
                            LazyVGrid(columns: columns, spacing: 12) {
                                ForEach(viewModel.filteredItems) { item in
                                    evidenceCard(item)
                                }
                            }
                            .padding(.horizontal)
                        }
                    }
                    .padding(.vertical)
                }
            }
            .navigationTitle("Evidence")
            .navigationBarTitleDisplayMode(.large)
            .toolbar {
                ToolbarItem(placement: .topBarTrailing) {
                    Button {
                        Task { await viewModel.verifyChain() }
                    } label: {
                        Label("Verify Chain", systemImage: "checkmark.shield")
                    }
                    .disabled(viewModel.isVerifying)
                }
            }
            .task {
                await viewModel.loadEvidence(for: incidentId)
            }
            .alert("Delete Evidence?", isPresented: $viewModel.showDeleteConfirmation) {
                Button("Cancel", role: .cancel) {}
                Button("Delete", role: .destructive) {
                    Task { await viewModel.deleteConfirmed() }
                }
            } message: {
                Text("This action cannot be undone. The chain of custody will be updated.")
            }
        }
    }

    // MARK: - Stats Bar

    private var statsBar: some View {
        HStack(spacing: 16) {
            statBadge(icon: "camera.fill", count: viewModel.photoCount, label: "Photos")
            statBadge(icon: "video.fill", count: viewModel.videoCount, label: "Videos")
            statBadge(icon: "mic.fill", count: viewModel.audioCount, label: "Audio")
            statBadge(icon: "doc.text.fill", count: viewModel.sitrepCount, label: "Sitreps")

            Spacer()

            Text(viewModel.storageFormatted)
                .font(.caption)
                .foregroundStyle(.secondary)
        }
        .padding(.horizontal)
    }

    private func statBadge(icon: String, count: Int, label: String) -> some View {
        VStack(spacing: 2) {
            Image(systemName: icon)
                .font(.caption)
                .foregroundStyle(Color(red: 0.2, green: 0.2, blue: 0.4))
            Text("\(count)")
                .font(.caption2.bold())
        }
        .accessibilityElement(children: .combine)
        .accessibilityLabel("\(count) \(label)")
    }

    // MARK: - Filter Chips

    private var filterChips: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 8) {
                filterChip(label: "All", type: nil)
                ForEach(EvidenceType.allCases, id: \.self) { type in
                    filterChip(label: type.displayName, type: type, icon: type.systemImage)
                }
            }
            .padding(.horizontal)
        }
    }

    private func filterChip(label: String, type: EvidenceType?, icon: String? = nil) -> some View {
        let isSelected = viewModel.selectedFilter == type
        return Button {
            viewModel.setFilter(type)
        } label: {
            HStack(spacing: 4) {
                if let icon {
                    Image(systemName: icon)
                        .font(.caption2)
                }
                Text(label)
                    .font(.caption.bold())
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 8)
            .background(isSelected ? Color(red: 0.2, green: 0.2, blue: 0.4) : Color.white)
            .foregroundStyle(isSelected ? .white : .primary)
            .clipShape(Capsule())
            .shadow(radius: 1)
        }
        .accessibilityLabel("Filter: \(label)")
        .accessibilityAddTraits(isSelected ? .isSelected : [])
    }

    // MARK: - Evidence Card

    private func evidenceCard(_ evidence: Evidence) -> some View {
        VStack(spacing: 0) {
            // Thumbnail area
            ZStack {
                RoundedRectangle(cornerRadius: 8)
                    .fill(thumbnailBackground(for: evidence.type))
                    .frame(height: 100)

                Image(systemName: evidence.type.systemImage)
                    .font(.title)
                    .foregroundStyle(.white.opacity(0.8))

                // Verification badge
                VStack {
                    HStack {
                        Spacer()
                        Image(systemName: evidence.verified ? "checkmark.seal.fill" : "exclamationmark.triangle.fill")
                            .font(.caption2)
                            .foregroundStyle(evidence.verified ? .green : .red)
                            .padding(4)
                            .background(.ultraThinMaterial, in: Circle())
                    }
                    Spacer()
                }
                .padding(4)

                // Duration badge for video/audio
                if let duration = evidence.durationSeconds {
                    VStack {
                        Spacer()
                        HStack {
                            Spacer()
                            Text(formatDuration(duration))
                                .font(.caption2.bold())
                                .foregroundStyle(.white)
                                .padding(.horizontal, 6)
                                .padding(.vertical, 2)
                                .background(.black.opacity(0.6), in: Capsule())
                        }
                    }
                    .padding(4)
                }
            }

            // Info area
            VStack(alignment: .leading, spacing: 2) {
                Text(evidence.type.displayName)
                    .font(.caption2.bold())
                    .foregroundStyle(Color(red: 0.2, green: 0.2, blue: 0.4))

                Text(evidence.timestamp, style: .relative)
                    .font(.caption2)
                    .foregroundStyle(.secondary)

                if let annotation = evidence.annotation {
                    Text(annotation)
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                        .lineLimit(1)
                }
            }
            .padding(6)
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .background(Color.white)
        .clipShape(RoundedRectangle(cornerRadius: 8))
        .shadow(radius: 2)
        .contextMenu {
            Button(role: .destructive) {
                viewModel.confirmDelete(evidence)
            } label: {
                Label("Delete", systemImage: "trash")
            }
        }
        .accessibilityElement(children: .combine)
        .accessibilityLabel("\(evidence.type.displayName), \(evidence.verified ? "verified" : "unverified")")
    }

    private func thumbnailBackground(for type: EvidenceType) -> Color {
        switch type {
        case .photo: return Color(red: 0.2, green: 0.2, blue: 0.4)
        case .video: return Color(red: 0.9, green: 0.22, blue: 0.27)
        case .audio: return Color(red: 0.15, green: 0.55, blue: 0.35)
        case .sitrep: return Color(red: 0.15, green: 0.39, blue: 0.92)
        }
    }

    // MARK: - Verification Banner

    private func verificationBanner(_ result: VerificationResult) -> some View {
        HStack(spacing: 8) {
            Image(systemName: result.isValid ? "checkmark.shield.fill" : "xmark.shield.fill")
                .foregroundStyle(result.isValid ? .green : .red)

            VStack(alignment: .leading, spacing: 2) {
                Text(result.isValid ? "Chain of Custody Verified" : "Chain Integrity Broken")
                    .font(.caption.bold())
                Text(result.message)
                    .font(.caption2)
                    .foregroundStyle(.secondary)
            }

            Spacer()

            Text("\(result.itemsVerified) items")
                .font(.caption2)
                .foregroundStyle(.secondary)
        }
        .padding(12)
        .background(
            RoundedRectangle(cornerRadius: 8)
                .fill(result.isValid ? Color.green.opacity(0.1) : Color.red.opacity(0.1))
        )
        .padding(.horizontal)
    }

    // MARK: - Empty State

    private var emptyState: some View {
        VStack(spacing: 12) {
            Image(systemName: "photo.on.rectangle.angled")
                .font(.system(size: 48))
                .foregroundStyle(.secondary)
            Text("No Evidence")
                .font(.headline)
            Text("Capture photos, video, audio, or submit a sitrep to document this incident.")
                .font(.caption)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
        }
        .padding(40)
    }

    // MARK: - Helpers

    private func formatDuration(_ seconds: TimeInterval) -> String {
        let mins = Int(seconds) / 60
        let secs = Int(seconds) % 60
        return String(format: "%d:%02d", mins, secs)
    }
}
