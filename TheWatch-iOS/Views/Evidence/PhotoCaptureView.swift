// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:         PhotoCaptureView.swift
// Purpose:      SwiftUI view for capturing photos during incidents. Full-screen
//               camera viewfinder with annotation, flash toggle, camera flip,
//               geotag overlay, and submission to evidence chain.
// Created:      2026-03-24
// Author:       Claude
// Dependencies: SwiftUI, PhotoCaptureViewModel.swift
//
// Usage Example:
//   PhotoCaptureView(incidentId: "incident-001")
//
// Note: Uses mock camera output. Real AVCaptureSession integration is done
//       via UIViewRepresentable in production adapter.
// ============================================================================

import SwiftUI

struct PhotoCaptureView: View {
    let incidentId: String
    @State private var viewModel = PhotoCaptureViewModel()
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        ZStack {
            Color.black.ignoresSafeArea()

            if viewModel.isCaptured {
                capturedPreview
            } else {
                cameraViewfinder
            }
        }
        .navigationBarBackButtonHidden(true)
        .toolbar(.hidden, for: .tabBar)
        .alert("Error", isPresented: .constant(viewModel.errorMessage != nil)) {
            Button("OK") { viewModel.errorMessage = nil }
        } message: {
            Text(viewModel.errorMessage ?? "")
        }
    }

    // MARK: - Camera Viewfinder

    private var cameraViewfinder: some View {
        VStack(spacing: 0) {
            // Top controls
            HStack {
                Button { dismiss() } label: {
                    Image(systemName: "xmark")
                        .font(.title3)
                        .foregroundStyle(.white)
                        .frame(width: 44, height: 44)
                }
                .accessibilityLabel("Close camera")

                Spacer()

                Button { viewModel.toggleFlash() } label: {
                    Image(systemName: viewModel.flashMode.systemImage)
                        .font(.title3)
                        .foregroundStyle(.yellow)
                        .frame(width: 44, height: 44)
                }
                .accessibilityLabel("Flash: \(viewModel.flashMode.rawValue)")
            }
            .padding(.horizontal)

            Spacer()

            // Mock viewfinder
            ZStack {
                RoundedRectangle(cornerRadius: 12)
                    .fill(Color(white: 0.15))
                    .overlay(
                        RoundedRectangle(cornerRadius: 12)
                            .strokeBorder(Color.white.opacity(0.3), lineWidth: 1)
                    )

                VStack(spacing: 8) {
                    Image(systemName: "camera.viewfinder")
                        .font(.system(size: 64))
                        .foregroundStyle(.white.opacity(0.4))
                    Text("Camera Viewfinder")
                        .font(.caption)
                        .foregroundStyle(.white.opacity(0.4))
                }

                // Grid overlay
                gridOverlay
            }
            .padding(.horizontal, 20)
            .frame(maxHeight: .infinity)

            Spacer()

            // Annotation field
            HStack {
                Image(systemName: "text.bubble")
                    .foregroundStyle(.white.opacity(0.6))
                TextField("Add annotation...", text: $viewModel.annotation)
                    .foregroundStyle(.white)
                    .font(.caption)
            }
            .padding(.horizontal, 16)
            .padding(.vertical, 10)
            .background(Color.white.opacity(0.15), in: RoundedRectangle(cornerRadius: 8))
            .padding(.horizontal, 20)

            // Bottom controls
            HStack(alignment: .center) {
                // Gallery button (placeholder)
                Circle()
                    .fill(Color.white.opacity(0.2))
                    .frame(width: 44, height: 44)
                    .overlay(
                        Image(systemName: "photo")
                            .foregroundStyle(.white)
                    )

                Spacer()

                // Capture button
                Button {
                    Task { await viewModel.capturePhoto(incidentId: incidentId) }
                } label: {
                    ZStack {
                        Circle()
                            .strokeBorder(.white, lineWidth: 4)
                            .frame(width: 72, height: 72)
                        Circle()
                            .fill(.white)
                            .frame(width: 60, height: 60)
                    }
                }
                .disabled(viewModel.isSubmitting)
                .accessibilityLabel("Capture photo")

                Spacer()

                // Flip camera
                Button { viewModel.toggleCamera() } label: {
                    Circle()
                        .fill(Color.white.opacity(0.2))
                        .frame(width: 44, height: 44)
                        .overlay(
                            Image(systemName: "camera.rotate")
                                .foregroundStyle(.white)
                        )
                }
                .accessibilityLabel("Switch camera")
            }
            .padding(.horizontal, 40)
            .padding(.bottom, 30)
            .padding(.top, 16)
        }
    }

    // MARK: - Grid Overlay

    private var gridOverlay: some View {
        GeometryReader { geo in
            let w = geo.size.width
            let h = geo.size.height
            Path { path in
                path.move(to: CGPoint(x: w / 3, y: 0))
                path.addLine(to: CGPoint(x: w / 3, y: h))
                path.move(to: CGPoint(x: 2 * w / 3, y: 0))
                path.addLine(to: CGPoint(x: 2 * w / 3, y: h))
                path.move(to: CGPoint(x: 0, y: h / 3))
                path.addLine(to: CGPoint(x: w, y: h / 3))
                path.move(to: CGPoint(x: 0, y: 2 * h / 3))
                path.addLine(to: CGPoint(x: w, y: 2 * h / 3))
            }
            .stroke(Color.white.opacity(0.2), lineWidth: 0.5)
        }
    }

    // MARK: - Captured Preview

    private var capturedPreview: some View {
        VStack(spacing: 0) {
            HStack {
                Button {
                    viewModel.reset()
                } label: {
                    Text("Retake")
                        .foregroundStyle(.white)
                        .frame(height: 44)
                }
                .accessibilityLabel("Retake photo")

                Spacer()

                Button { dismiss() } label: {
                    Text("Done")
                        .bold()
                        .foregroundStyle(.white)
                        .frame(height: 44)
                }
                .accessibilityLabel("Done, save photo")
            }
            .padding(.horizontal)

            Spacer()

            // Mock captured image
            ZStack {
                RoundedRectangle(cornerRadius: 12)
                    .fill(Color(red: 0.2, green: 0.2, blue: 0.4))

                VStack(spacing: 8) {
                    Image(systemName: "checkmark.circle.fill")
                        .font(.system(size: 48))
                        .foregroundStyle(.green)
                    Text("Photo Captured")
                        .font(.headline)
                        .foregroundStyle(.white)
                    if let annotation = viewModel.capturedEvidence?.annotation {
                        Text(annotation)
                            .font(.caption)
                            .foregroundStyle(.white.opacity(0.7))
                    }
                    if let evidence = viewModel.capturedEvidence {
                        Text("Hash: \(evidence.hash.prefix(24))...")
                            .font(.caption2.monospaced())
                            .foregroundStyle(.white.opacity(0.5))
                    }
                }
            }
            .padding(.horizontal, 20)
            .frame(maxHeight: .infinity)

            Spacer()
        }
    }
}
