// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:         SitrepView.swift
// Purpose:      SwiftUI view for structured situation report submission.
//               Form with situation type picker, severity, description,
//               evidence attachment, and location. Submits to evidence chain.
// Created:      2026-03-24
// Author:       Claude
// Dependencies: SwiftUI, SitrepViewModel.swift
//
// Usage Example:
//   SitrepView(incidentId: "incident-001")
// ============================================================================

import SwiftUI

struct SitrepView: View {
    let incidentId: String
    @State private var viewModel = SitrepViewModel()
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationStack {
            ZStack {
                Color(red: 0.97, green: 0.97, blue: 0.97)
                    .ignoresSafeArea()

                ScrollView {
                    VStack(spacing: 16) {
                        // Situation Type
                        sectionCard(title: "Situation Type") {
                            LazyVGrid(columns: [
                                GridItem(.flexible()),
                                GridItem(.flexible())
                            ], spacing: 8) {
                                ForEach(SituationType.allCases, id: \.self) { type in
                                    situationTypeButton(type)
                                }
                            }
                        }

                        // Severity
                        sectionCard(title: "Severity Level") {
                            HStack(spacing: 8) {
                                ForEach(SitrepSeverity.allCases, id: \.self) { sev in
                                    severityButton(sev)
                                }
                            }
                        }

                        // Description
                        sectionCard(title: "Description") {
                            VStack(alignment: .trailing, spacing: 4) {
                                TextEditor(text: $viewModel.description)
                                    .frame(minHeight: 120)
                                    .padding(8)
                                    .background(
                                        RoundedRectangle(cornerRadius: 8)
                                            .fill(Color(red: 0.97, green: 0.97, blue: 0.97))
                                    )
                                    .accessibilityLabel("Situation description")

                                Text("\(viewModel.wordCount) words")
                                    .font(.caption2)
                                    .foregroundStyle(.secondary)
                            }
                        }

                        // Attached Evidence
                        if !viewModel.availableEvidence.isEmpty {
                            sectionCard(title: "Attach Evidence") {
                                ForEach(viewModel.availableEvidence) { item in
                                    attachmentRow(item)
                                }
                            }
                        }

                        // Submit
                        Button {
                            Task { await viewModel.submitSitrep(incidentId: incidentId) }
                        } label: {
                            HStack {
                                if viewModel.isSubmitting {
                                    ProgressView()
                                        .tint(.white)
                                } else {
                                    Image(systemName: "paperplane.fill")
                                }
                                Text("Submit Sitrep")
                                    .bold()
                            }
                            .foregroundStyle(.white)
                            .frame(maxWidth: .infinity)
                            .padding(.vertical, 16)
                            .background(
                                viewModel.isValid
                                ? Color(red: 0.9, green: 0.22, blue: 0.27)
                                : Color.gray,
                                in: RoundedRectangle(cornerRadius: 12)
                            )
                        }
                        .disabled(!viewModel.isValid || viewModel.isSubmitting)
                        .padding(.horizontal)
                        .accessibilityLabel("Submit situation report")

                        if let error = viewModel.errorMessage {
                            Text(error)
                                .font(.caption)
                                .foregroundStyle(.red)
                                .padding(.horizontal)
                        }
                    }
                    .padding(.vertical)
                }
            }
            .navigationTitle("Situation Report")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .topBarLeading) {
                    Button("Cancel") { dismiss() }
                }
            }
            .task {
                await viewModel.loadAvailableEvidence(incidentId: incidentId)
            }
            .alert("Sitrep Submitted", isPresented: $viewModel.showSuccess) {
                Button("OK") { dismiss() }
            } message: {
                Text("Your situation report has been added to the evidence chain.")
            }
        }
    }

    // MARK: - Components

    private func sectionCard(title: String, @ViewBuilder content: () -> some View) -> some View {
        VStack(alignment: .leading, spacing: 8) {
            Text(title)
                .font(.subheadline.bold())
                .foregroundStyle(Color(red: 0.2, green: 0.2, blue: 0.4))
            content()
        }
        .padding(16)
        .background(Color.white, in: RoundedRectangle(cornerRadius: 12))
        .shadow(radius: 1)
        .padding(.horizontal)
    }

    private func situationTypeButton(_ type: SituationType) -> some View {
        let isSelected = viewModel.situationType == type
        return Button {
            viewModel.situationType = type
        } label: {
            HStack(spacing: 6) {
                Image(systemName: type.systemImage)
                    .font(.caption)
                Text(type.displayName)
                    .font(.caption2)
                    .lineLimit(1)
            }
            .frame(maxWidth: .infinity)
            .padding(.vertical, 10)
            .background(
                isSelected
                ? Color(red: 0.2, green: 0.2, blue: 0.4)
                : Color(red: 0.97, green: 0.97, blue: 0.97),
                in: RoundedRectangle(cornerRadius: 8)
            )
            .foregroundStyle(isSelected ? .white : .primary)
        }
        .accessibilityLabel(type.displayName)
        .accessibilityAddTraits(isSelected ? .isSelected : [])
    }

    private func severityButton(_ sev: SitrepSeverity) -> some View {
        let isSelected = viewModel.severity == sev
        let color: Color = switch sev {
        case .low: .green
        case .medium: .yellow
        case .high: .orange
        case .critical: .red
        }

        return Button {
            viewModel.severity = sev
        } label: {
            VStack(spacing: 4) {
                Circle()
                    .fill(color)
                    .frame(width: 24, height: 24)
                    .overlay(
                        isSelected
                        ? Image(systemName: "checkmark").font(.caption2).foregroundStyle(.white)
                        : nil
                    )
                Text(sev.displayName)
                    .font(.caption2)
            }
            .frame(maxWidth: .infinity)
            .padding(.vertical, 8)
            .background(
                isSelected ? color.opacity(0.15) : Color.clear,
                in: RoundedRectangle(cornerRadius: 8)
            )
        }
        .accessibilityLabel("Severity: \(sev.displayName)")
        .accessibilityAddTraits(isSelected ? .isSelected : [])
    }

    private func attachmentRow(_ item: Evidence) -> some View {
        let isAttached = viewModel.attachedEvidenceIds.contains(item.id)
        return Button {
            viewModel.toggleAttachment(item.id)
        } label: {
            HStack(spacing: 8) {
                Image(systemName: item.type.systemImage)
                    .foregroundStyle(Color(red: 0.2, green: 0.2, blue: 0.4))
                    .frame(width: 32, height: 32)
                    .background(Color(red: 0.97, green: 0.97, blue: 0.97), in: RoundedRectangle(cornerRadius: 6))

                VStack(alignment: .leading, spacing: 2) {
                    Text(item.type.displayName)
                        .font(.caption.bold())
                    Text(item.timestamp, style: .relative)
                        .font(.caption2)
                        .foregroundStyle(.secondary)
                }

                Spacer()

                Image(systemName: isAttached ? "checkmark.circle.fill" : "circle")
                    .foregroundStyle(isAttached ? .green : .secondary)
            }
            .padding(.vertical, 4)
        }
        .accessibilityLabel("\(item.type.displayName), \(isAttached ? "attached" : "not attached")")
    }
}
