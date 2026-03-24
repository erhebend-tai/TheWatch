// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:         GuestSOSView.swift
// Purpose:      SwiftUI view for Guest (unauthenticated) emergency SOS.
//               Provides a large, prominent SOS button that bypasses all
//               authentication to send an emergency alert with the device's
//               current GPS location. Includes countdown display, cancellation,
//               and confirmation dialog.
// Created:      2026-03-24
// Author:       Claude
// Dependencies: SwiftUI, GuestSOSViewModel.swift, LocationManager.swift
// Related:      GuestSOSViewModel.swift (view model),
//               LoginView.swift (navigation source),
//               AppRouter.swift (route: .guestSOS)
//
// Usage Example:
//   NavigationLink(value: AppRouter.Destination.guestSOS) {
//       Text("Guest Emergency")
//   }
//   // Or directly:
//   GuestSOSView()
//
// Accessibility:
//   - SOS button has dynamic type support
//   - VoiceOver labels on all interactive elements
//   - High-contrast emergency red color scheme
//   - Haptic feedback on activation (via UIImpactFeedbackGenerator)
//
// Potential Additions:
//   - Siren audio playback toggle
//   - Flashlight strobe mode
//   - Voice memo recording
//   - Photo/video capture for evidence
//   - Shake-to-activate gesture
//   - Apple Watch companion SOS trigger
// ============================================================================

import SwiftUI

struct GuestSOSView: View {
    @State private var viewModel = GuestSOSViewModel()
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        ZStack {
            // Background: dark for urgency
            Color(red: 0.08, green: 0.08, blue: 0.12)
                .ignoresSafeArea()

            VStack(spacing: 24) {
                // Header
                headerSection

                Spacer()

                // SOS Button or Countdown
                if viewModel.isCountingDown {
                    countdownSection
                } else if viewModel.sosDispatched {
                    dispatchedSection
                } else {
                    sosButtonSection
                }

                Spacer()

                // Location info
                locationSection

                // Cancel / Back
                bottomActions
            }
            .padding(24)
        }
        .navigationBarBackButtonHidden(viewModel.isCountingDown)
        .alert("Confirm Emergency SOS", isPresented: $viewModel.showConfirmation) {
            Button("Send SOS", role: .destructive) {
                viewModel.confirmSOS()
            }
            Button("Cancel", role: .cancel) {
                viewModel.cancelSOS()
            }
        } message: {
            Text("This will send an emergency alert with your location to nearby responders. Are you sure?")
        }
        .accessibilityElement(children: .contain)
    }

    // MARK: - Header

    private var headerSection: some View {
        VStack(spacing: 8) {
            Image(systemName: "sos")
                .font(.system(size: 36, weight: .bold))
                .foregroundColor(.red)

            Text("Guest Emergency")
                .font(.title2.bold())
                .foregroundColor(.white)

            Text("No account required. Your location will be shared with nearby responders.")
                .font(.caption)
                .foregroundColor(.white.opacity(0.7))
                .multilineTextAlignment(.center)
        }
    }

    // MARK: - SOS Button

    private var sosButtonSection: some View {
        VStack(spacing: 16) {
            Button(action: {
                // Haptic feedback
                let generator = UIImpactFeedbackGenerator(style: .heavy)
                generator.impactOccurred()
                viewModel.activateSOS()
            }) {
                ZStack {
                    Circle()
                        .fill(
                            RadialGradient(
                                colors: [
                                    Color(red: 0.9, green: 0.1, blue: 0.1),
                                    Color(red: 0.7, green: 0.0, blue: 0.0)
                                ],
                                center: .center,
                                startRadius: 20,
                                endRadius: 100
                            )
                        )
                        .frame(width: 200, height: 200)
                        .shadow(color: .red.opacity(0.6), radius: 20, x: 0, y: 0)

                    VStack(spacing: 4) {
                        Text("SOS")
                            .font(.system(size: 48, weight: .black))
                            .foregroundColor(.white)

                        Text("PRESS FOR HELP")
                            .font(.system(size: 12, weight: .bold))
                            .foregroundColor(.white.opacity(0.9))
                    }
                }
            }
            .accessibilityLabel("Emergency SOS button")
            .accessibilityHint("Double tap to begin emergency SOS countdown")

            Text(viewModel.statusMessage)
                .font(.subheadline)
                .foregroundColor(.white.opacity(0.8))
                .multilineTextAlignment(.center)

            if let error = viewModel.errorMessage {
                Text(error)
                    .font(.caption)
                    .foregroundColor(.orange)
                    .multilineTextAlignment(.center)
            }
        }
    }

    // MARK: - Countdown

    private var countdownSection: some View {
        VStack(spacing: 24) {
            ZStack {
                Circle()
                    .stroke(Color.red.opacity(0.3), lineWidth: 8)
                    .frame(width: 200, height: 200)

                Circle()
                    .trim(
                        from: 0,
                        to: CGFloat(viewModel.countdownSeconds)
                            / CGFloat(GuestSOSViewModel.defaultCountdownDuration)
                    )
                    .stroke(Color.red, style: StrokeStyle(lineWidth: 8, lineCap: .round))
                    .frame(width: 200, height: 200)
                    .rotationEffect(.degrees(-90))
                    .animation(.linear(duration: 1), value: viewModel.countdownSeconds)

                VStack(spacing: 4) {
                    Text("\(viewModel.countdownSeconds)")
                        .font(.system(size: 72, weight: .black, design: .monospaced))
                        .foregroundColor(.red)
                        .contentTransition(.numericText())

                    Text("SENDING...")
                        .font(.caption.bold())
                        .foregroundColor(.red.opacity(0.8))
                }
            }
            .accessibilityLabel("SOS countdown: \(viewModel.countdownSeconds) seconds remaining")

            Text(viewModel.statusMessage)
                .font(.subheadline)
                .foregroundColor(.white.opacity(0.8))
                .multilineTextAlignment(.center)

            Button(action: {
                let generator = UINotificationFeedbackGenerator()
                generator.notificationOccurred(.warning)
                viewModel.cancelSOS()
            }) {
                Text("CANCEL SOS")
                    .font(.headline.bold())
                    .foregroundColor(.white)
                    .frame(maxWidth: .infinity)
                    .padding(16)
                    .background(Color.gray.opacity(0.4))
                    .cornerRadius(12)
            }
            .accessibilityLabel("Cancel SOS")
            .accessibilityHint("Stops the emergency alert before it is sent")
        }
    }

    // MARK: - Dispatched

    private var dispatchedSection: some View {
        VStack(spacing: 24) {
            Image(systemName: "checkmark.shield.fill")
                .font(.system(size: 80))
                .foregroundColor(.green)
                .accessibilityHidden(true)

            Text("SOS Sent")
                .font(.title.bold())
                .foregroundColor(.green)

            Text(viewModel.statusMessage)
                .font(.subheadline)
                .foregroundColor(.white.opacity(0.8))
                .multilineTextAlignment(.center)

            Text("Stay where you are if safe. Help is being notified.")
                .font(.caption)
                .foregroundColor(.white.opacity(0.6))
                .multilineTextAlignment(.center)

            Button(action: {
                viewModel.reset()
            }) {
                Text("Send Another SOS")
                    .font(.subheadline.bold())
                    .foregroundColor(.white)
                    .padding(12)
                    .background(Color.red.opacity(0.6))
                    .cornerRadius(8)
            }
            .accessibilityLabel("Send another SOS alert")
        }
    }

    // MARK: - Location Section

    private var locationSection: some View {
        HStack(spacing: 8) {
            Image(systemName: "location.fill")
                .foregroundColor(viewModel.latitude != nil ? .green : .orange)

            if viewModel.isAcquiringLocation {
                ProgressView()
                    .tint(.white)
                Text("Acquiring location...")
                    .font(.caption)
                    .foregroundColor(.white.opacity(0.6))
            } else if let lat = viewModel.latitude, let lon = viewModel.longitude {
                Text(String(format: "%.4f, %.4f", lat, lon))
                    .font(.caption.monospaced())
                    .foregroundColor(.white.opacity(0.7))
            } else {
                Text("Location unavailable")
                    .font(.caption)
                    .foregroundColor(.orange)

                Button("Retry") {
                    viewModel.refreshLocation()
                }
                .font(.caption.bold())
                .foregroundColor(.blue)
            }
        }
        .padding(12)
        .background(Color.white.opacity(0.05))
        .cornerRadius(8)
        .accessibilityElement(children: .combine)
        .accessibilityLabel(
            viewModel.latitude != nil
                ? "Location acquired"
                : "Location not yet available"
        )
    }

    // MARK: - Bottom Actions

    private var bottomActions: some View {
        Group {
            if !viewModel.isCountingDown {
                Button(action: { dismiss() }) {
                    Text("Back to Login")
                        .font(.subheadline)
                        .foregroundColor(.white.opacity(0.5))
                }
                .accessibilityLabel("Return to login screen")
            }
        }
    }
}

#Preview {
    NavigationStack {
        GuestSOSView()
    }
}
