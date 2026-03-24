// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:         VideoCaptureView.swift
// Purpose:      SwiftUI view for video recording during incidents. Shows
//               recording indicator, elapsed/remaining timer, progress ring,
//               and auto-stops at max duration (60s).
// Created:      2026-03-24
// Author:       Claude
// Dependencies: SwiftUI, VideoCaptureViewModel.swift
//
// Usage Example:
//   VideoCaptureView(incidentId: "incident-001")
// ============================================================================

import SwiftUI

struct VideoCaptureView: View {
    let incidentId: String
    @State private var viewModel = VideoCaptureViewModel()
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        ZStack {
            Color.black.ignoresSafeArea()

            VStack(spacing: 0) {
                // Top bar
                HStack {
                    Button { dismiss() } label: {
                        Image(systemName: "xmark")
                            .font(.title3)
                            .foregroundStyle(.white)
                            .frame(width: 44, height: 44)
                    }
                    Spacer()

                    // Recording indicator
                    if viewModel.isRecording {
                        HStack(spacing: 6) {
                            Circle()
                                .fill(.red)
                                .frame(width: 10, height: 10)
                            Text("REC")
                                .font(.caption.bold())
                                .foregroundStyle(.red)
                            Text(viewModel.elapsedFormatted)
                                .font(.caption.monospaced())
                                .foregroundStyle(.white)
                        }
                        .padding(.horizontal, 12)
                        .padding(.vertical, 6)
                        .background(.black.opacity(0.6), in: Capsule())
                    }

                    Spacer()

                    Text(viewModel.remainingFormatted)
                        .font(.caption.monospaced())
                        .foregroundStyle(.white.opacity(0.6))
                        .frame(width: 44, alignment: .trailing)
                }
                .padding(.horizontal)

                Spacer()

                // Viewfinder
                ZStack {
                    RoundedRectangle(cornerRadius: 12)
                        .fill(Color(white: 0.15))

                    if viewModel.isSubmitting {
                        VStack(spacing: 12) {
                            ProgressView()
                                .tint(.white)
                            Text("Processing video...")
                                .font(.caption)
                                .foregroundStyle(.white.opacity(0.6))
                        }
                    } else if let evidence = viewModel.capturedEvidence {
                        VStack(spacing: 8) {
                            Image(systemName: "checkmark.circle.fill")
                                .font(.system(size: 48))
                                .foregroundStyle(.green)
                            Text("Video Recorded")
                                .font(.headline)
                                .foregroundStyle(.white)
                            if let duration = evidence.durationSeconds {
                                Text(String(format: "%.1f seconds", duration))
                                    .font(.caption)
                                    .foregroundStyle(.white.opacity(0.7))
                            }
                            Text("Hash: \(evidence.hash.prefix(24))...")
                                .font(.caption2.monospaced())
                                .foregroundStyle(.white.opacity(0.5))
                        }
                    } else {
                        VStack(spacing: 8) {
                            Image(systemName: "video.fill")
                                .font(.system(size: 48))
                                .foregroundStyle(.white.opacity(0.4))
                            Text(viewModel.isRecording ? "Recording..." : "Tap to Record")
                                .font(.caption)
                                .foregroundStyle(.white.opacity(0.4))
                        }
                    }

                    // Progress ring overlay when recording
                    if viewModel.isRecording {
                        VStack {
                            Spacer()
                            ProgressView(value: viewModel.progress)
                                .tint(.red)
                                .padding(.horizontal, 20)
                                .padding(.bottom, 12)
                        }
                    }
                }
                .padding(.horizontal, 20)
                .frame(maxHeight: .infinity)

                Spacer()

                // Bottom controls
                HStack(alignment: .center) {
                    Spacer()

                    if viewModel.capturedEvidence != nil {
                        Button { dismiss() } label: {
                            Text("Done")
                                .font(.headline)
                                .foregroundStyle(.white)
                                .padding(.horizontal, 32)
                                .padding(.vertical, 14)
                                .background(Color(red: 0.2, green: 0.2, blue: 0.4), in: Capsule())
                        }
                    } else {
                        // Record/Stop button
                        Button {
                            if viewModel.isRecording {
                                Task { await viewModel.stopRecording() }
                            } else {
                                viewModel.startRecording(incidentId: incidentId)
                            }
                        } label: {
                            ZStack {
                                Circle()
                                    .strokeBorder(.white, lineWidth: 4)
                                    .frame(width: 72, height: 72)

                                if viewModel.isRecording {
                                    RoundedRectangle(cornerRadius: 6)
                                        .fill(.red)
                                        .frame(width: 28, height: 28)
                                } else {
                                    Circle()
                                        .fill(.red)
                                        .frame(width: 60, height: 60)
                                }
                            }
                        }
                        .disabled(viewModel.isSubmitting)
                        .accessibilityLabel(viewModel.isRecording ? "Stop recording" : "Start recording")
                    }

                    Spacer()
                }
                .padding(.bottom, 30)
                .padding(.top, 16)
            }
        }
        .navigationBarBackButtonHidden(true)
    }
}
