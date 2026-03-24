// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:         AudioRecordView.swift
// Purpose:      SwiftUI view for audio recording during incidents. Shows
//               waveform visualization, elapsed time, and submission flow.
// Created:      2026-03-24
// Author:       Claude
// Dependencies: SwiftUI, AudioRecordViewModel.swift
//
// Usage Example:
//   AudioRecordView(incidentId: "incident-001")
// ============================================================================

import SwiftUI

struct AudioRecordView: View {
    let incidentId: String
    @State private var viewModel = AudioRecordViewModel()
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        ZStack {
            Color(red: 0.08, green: 0.08, blue: 0.12)
                .ignoresSafeArea()

            VStack(spacing: 24) {
                // Header
                HStack {
                    Button { dismiss() } label: {
                        Image(systemName: "xmark")
                            .font(.title3)
                            .foregroundStyle(.white)
                            .frame(width: 44, height: 44)
                    }
                    Spacer()
                    Text("Audio Recording")
                        .font(.headline)
                        .foregroundStyle(.white)
                    Spacer()
                    Color.clear.frame(width: 44, height: 44)
                }
                .padding(.horizontal)

                Spacer()

                // Timer
                Text(viewModel.elapsedFormatted)
                    .font(.system(size: 64, weight: .thin, design: .monospaced))
                    .foregroundStyle(.white)

                // Waveform visualization
                waveformView
                    .frame(height: 80)
                    .padding(.horizontal, 20)

                // Status
                if viewModel.isSubmitting {
                    HStack(spacing: 8) {
                        ProgressView()
                            .tint(.white)
                        Text("Processing...")
                            .font(.caption)
                            .foregroundStyle(.white.opacity(0.6))
                    }
                } else if let evidence = viewModel.capturedEvidence {
                    VStack(spacing: 4) {
                        Image(systemName: "checkmark.circle.fill")
                            .font(.title)
                            .foregroundStyle(.green)
                        Text("Audio Recorded")
                            .font(.caption.bold())
                            .foregroundStyle(.white)
                        if let duration = evidence.durationSeconds {
                            Text(String(format: "%.0f seconds", duration))
                                .font(.caption2)
                                .foregroundStyle(.white.opacity(0.6))
                        }
                        Text("Hash: \(evidence.hash.prefix(20))...")
                            .font(.caption2.monospaced())
                            .foregroundStyle(.white.opacity(0.4))
                    }
                }

                Spacer()

                // Controls
                if viewModel.capturedEvidence != nil {
                    Button { dismiss() } label: {
                        Text("Done")
                            .font(.headline)
                            .foregroundStyle(.white)
                            .frame(maxWidth: .infinity)
                            .padding(.vertical, 16)
                            .background(Color(red: 0.2, green: 0.2, blue: 0.4), in: RoundedRectangle(cornerRadius: 12))
                    }
                    .padding(.horizontal, 40)
                } else {
                    Button {
                        if viewModel.isRecording {
                            Task { await viewModel.stopRecording() }
                        } else {
                            viewModel.startRecording(incidentId: incidentId)
                        }
                    } label: {
                        ZStack {
                            Circle()
                                .fill(viewModel.isRecording ? Color.red.opacity(0.2) : Color.red.opacity(0.1))
                                .frame(width: 100, height: 100)

                            Circle()
                                .strokeBorder(viewModel.isRecording ? Color.red : Color.white, lineWidth: 3)
                                .frame(width: 80, height: 80)

                            Image(systemName: viewModel.isRecording ? "stop.fill" : "mic.fill")
                                .font(.title)
                                .foregroundStyle(viewModel.isRecording ? .red : .white)
                        }
                    }
                    .disabled(viewModel.isSubmitting)
                    .accessibilityLabel(viewModel.isRecording ? "Stop recording" : "Start recording")

                    Text(viewModel.isRecording ? "Tap to stop" : "Tap to record")
                        .font(.caption)
                        .foregroundStyle(.white.opacity(0.5))
                }

                Spacer()
                    .frame(height: 30)
            }
        }
        .navigationBarBackButtonHidden(true)
    }

    // MARK: - Waveform

    private var waveformView: some View {
        GeometryReader { geo in
            let barWidth: CGFloat = 3
            let spacing: CGFloat = 2
            let barCount = min(viewModel.audioLevels.count, Int(geo.size.width / (barWidth + spacing)))
            let levels = Array(viewModel.audioLevels.suffix(barCount))

            HStack(alignment: .center, spacing: spacing) {
                ForEach(Array(levels.enumerated()), id: \.offset) { _, level in
                    RoundedRectangle(cornerRadius: 1.5)
                        .fill(barColor(for: level))
                        .frame(width: barWidth, height: max(4, CGFloat(level) * geo.size.height))
                }
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
    }

    private func barColor(for level: Float) -> Color {
        if level > 0.8 { return .red }
        if level > 0.6 { return .orange }
        return Color(red: 0.15, green: 0.55, blue: 0.35)
    }
}
