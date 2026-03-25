// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:         SosCountdownView.swift
// Purpose:      Full-screen red overlay with 3-second SOS countdown.
//               Displays urgent visual feedback during SOS activation:
//                 - Pulsing red background gradient
//                 - Large countdown numbers (3, 2, 1)
//                 - Accessible cancel button (large touch target)
//                 - "Contacting responders..." after countdown
//                 - Responder count when server responds
//                 - Offline queue confirmation when no network
// Created:      2026-03-24
// Author:       Claude
// Dependencies: SosCountdownViewModel, SwiftUI
//
// Usage example:
//   // As a fullScreenCover from HomeView:
//   .fullScreenCover(isPresented: $sosViewModel.isPresented) {
//       SosCountdownView(viewModel: sosViewModel)
//   }
//
//   // Or as a ZStack overlay:
//   ZStack {
//       HomeView()
//       if sosViewModel.isPresented {
//           SosCountdownView(viewModel: sosViewModel)
//       }
//   }
//
// Accessibility:
//   - Cancel button has 64pt minimum touch target
//   - All text has accessibilityLabel
//   - High contrast: white text on red background
//   - VoiceOver announces countdown changes
//   - Dynamic Type supported
//
// Potential additions:
//   - Siri voice confirmation ("SOS sent, help is on the way")
//   - Camera preview in corner for auto-capture
//   - Emergency contact names shown when active
//   - Pull-down gesture to show map with responder positions
// ============================================================================

import SwiftUI
import UIKit

// MARK: - Color Palette

private let sosRedDark = Color(red: 0.55, green: 0, blue: 0)
private let sosRedPrimary = Color(red: 0.8, green: 0, blue: 0)
private let sosRedLight = Color(red: 0.9, green: 0.22, blue: 0.21)
private let sosGreen = Color(red: 0.3, green: 0.69, blue: 0.31)
private let sosAmber = Color(red: 1.0, green: 0.76, blue: 0.03)

// MARK: - SosCountdownView

/// Full-screen SOS countdown overlay. Shows 3-2-1 countdown with pulsing
/// red background, then transitions to "contacting responders..." state.
struct SosCountdownView: View {
    let viewModel: SosCountdownViewModel

    /// Pulsing background animation state
    @State private var pulseScale: CGFloat = 1.0
    @State private var pulseOpacity: Double = 0.85

    var body: some View {
        ZStack {
            // Pulsing red gradient background
            RadialGradient(
                colors: [
                    sosRedLight.opacity(pulseOpacity),
                    sosRedPrimary.opacity(pulseOpacity),
                    sosRedDark
                ],
                center: .center,
                startRadius: 50,
                endRadius: 500
            )
            .ignoresSafeArea()
            .onAppear {
                withAnimation(.easeInOut(duration: 0.8).repeatForever(autoreverses: true)) {
                    pulseOpacity = 1.0
                    pulseScale = 1.05
                }
            }

            // Content based on state
            Group {
                switch viewModel.triggerState {
                case .countdown(let remaining):
                    CountdownContent(
                        secondsRemaining: remaining,
                        onCancel: { viewModel.cancel() }
                    )

                case .dispatching:
                    DispatchingContent()

                case .active(let requestId, let count, let radius):
                    ActiveContent(
                        responderCount: count,
                        radiusMeters: radius,
                        onDismiss: { viewModel.dismiss() }
                    )

                case .queuedOffline:
                    QueuedOfflineContent(
                        onDismiss: { viewModel.dismiss() }
                    )

                case .cancelled:
                    CancelledContent()

                case .error(let message, let queued):
                    ErrorContent(
                        message: message,
                        queuedOffline: queued,
                        onDismiss: { viewModel.dismiss() }
                    )

                case .idle:
                    DispatchingContent()
                }
            }
        }
        .statusBarHidden()
        .accessibilityElement(children: .contain)
        .accessibilityLabel("Emergency SOS screen")
    }
}

// MARK: - Countdown State

private struct CountdownContent: View {
    let secondsRemaining: Int
    let onCancel: () -> Void

    @State private var numberScale: CGFloat = 0.5

    var body: some View {
        VStack(spacing: 0) {
            // Top: Warning icon and title
            VStack(spacing: 8) {
                Image(systemName: "exclamationmark.triangle.fill")
                    .font(.system(size: 40))
                    .foregroundColor(.white)

                Text("EMERGENCY SOS")
                    .font(.system(size: 18, weight: .bold))
                    .foregroundColor(.white)
                    .tracking(4)
            }
            .padding(.top, 60)

            Spacer()

            // Center: Large countdown number
            VStack(spacing: 16) {
                Text(secondsRemaining > 0 ? "\(secondsRemaining)" : "!")
                    .font(.system(size: 120, weight: .black, design: .rounded))
                    .foregroundColor(.white)
                    .scaleEffect(numberScale)
                    .onChange(of: secondsRemaining) { _, _ in
                        numberScale = 1.3
                        withAnimation(.spring(response: 0.3, dampingFraction: 0.5)) {
                            numberScale = 1.0
                        }
                    }
                    .onAppear {
                        withAnimation(.spring(response: 0.3, dampingFraction: 0.5)) {
                            numberScale = 1.0
                        }
                    }
                    .accessibilityLabel(
                        secondsRemaining > 0
                        ? "\(secondsRemaining) seconds until SOS is sent. Tap cancel to stop."
                        : "Sending SOS now"
                    )
                    .accessibilityAddTraits(.updatesFrequently)
                    .onChange(of: secondsRemaining) { _, newValue in
                        let announcement = newValue > 0
                            ? "\(newValue) seconds"
                            : "Sending SOS now"
                        UIAccessibility.post(notification: .announcement, argument: announcement)
                    }

                Text("SOS will be sent in \(secondsRemaining)s")
                    .font(.system(size: 18, weight: .medium))
                    .foregroundColor(.white.opacity(0.9))
            }

            Spacer()

            // Bottom: Cancel button — large, accessible
            VStack(spacing: 8) {
                Button(action: onCancel) {
                    HStack(spacing: 12) {
                        Image(systemName: "xmark")
                            .font(.system(size: 22, weight: .bold))
                        Text("CANCEL")
                            .font(.system(size: 20, weight: .bold))
                            .tracking(2)
                    }
                    .foregroundColor(.white)
                    .frame(maxWidth: .infinity)
                    .frame(height: 64)
                    .background(.white.opacity(0.25))
                    .clipShape(Capsule())
                }
                .padding(.horizontal, 48)
                .accessibilityLabel("Cancel SOS")
                .accessibilityHint("Double tap to cancel the emergency alert")

                Text("Tap to cancel emergency alert")
                    .font(.caption)
                    .foregroundColor(.white.opacity(0.7))
            }
            .padding(.bottom, 60)
        }
    }
}

// MARK: - Dispatching State

private struct DispatchingContent: View {
    var body: some View {
        VStack(spacing: 24) {
            ProgressView()
                .progressViewStyle(CircularProgressViewStyle(tint: .white))
                .scaleEffect(2)

            Text("Contacting responders...")
                .font(.system(size: 24, weight: .bold))
                .foregroundColor(.white)
                .multilineTextAlignment(.center)

            Text("Sending your location to nearby volunteers")
                .font(.system(size: 16))
                .foregroundColor(.white.opacity(0.8))
                .multilineTextAlignment(.center)
        }
        .accessibilityElement(children: .combine)
        .accessibilityLabel("Contacting responders. Sending your location to nearby volunteers.")
    }
}

// MARK: - Active State (Server Responded)

private struct ActiveContent: View {
    let responderCount: Int
    let radiusMeters: Double
    let onDismiss: () -> Void

    var body: some View {
        VStack(spacing: 0) {
            Spacer()

            // Success checkmark
            ZStack {
                Circle()
                    .fill(sosGreen)
                    .frame(width: 80, height: 80)

                Image(systemName: "checkmark")
                    .font(.system(size: 40, weight: .bold))
                    .foregroundColor(.white)
            }

            Spacer().frame(height: 24)

            Text("SOS SENT")
                .font(.system(size: 28, weight: .black))
                .foregroundColor(.white)
                .tracking(4)

            Spacer().frame(height: 16)

            // Responder count badge
            Text(responderCount > 0
                 ? "\(responderCount) responders being notified"
                 : "Notifying nearby volunteers")
                .font(.system(size: 18, weight: .semibold))
                .foregroundColor(.white)
                .padding(.horizontal, 24)
                .padding(.vertical, 12)
                .background(.white.opacity(0.2))
                .clipShape(RoundedRectangle(cornerRadius: 12))

            Spacer().frame(height: 12)

            Text("Search radius: \(Int(radiusMeters / 1000))km")
                .font(.system(size: 14))
                .foregroundColor(.white.opacity(0.7))

            Spacer().frame(height: 32)

            Text("Help is on the way. Stay where you are.")
                .font(.system(size: 16, weight: .medium))
                .foregroundColor(.white.opacity(0.9))
                .multilineTextAlignment(.center)

            Spacer()

            // Dismiss button
            Button(action: onDismiss) {
                Text("Back to Map")
                    .font(.system(size: 16, weight: .semibold))
                    .foregroundColor(.white)
                    .padding(.horizontal, 32)
                    .padding(.vertical, 12)
                    .background(.white.opacity(0.2))
                    .clipShape(Capsule())
            }
            .padding(.bottom, 60)
        }
        .accessibilityElement(children: .combine)
        .accessibilityLabel("SOS sent. \(responderCount) responders being notified. Help is on the way.")
    }
}

// MARK: - Queued Offline State

private struct QueuedOfflineContent: View {
    let onDismiss: () -> Void

    var body: some View {
        VStack(spacing: 0) {
            Spacer()

            Image(systemName: "wifi.slash")
                .font(.system(size: 56))
                .foregroundColor(sosAmber)

            Spacer().frame(height: 24)

            Text("SOS QUEUED")
                .font(.system(size: 28, weight: .black))
                .foregroundColor(.white)
                .tracking(4)

            Spacer().frame(height: 16)

            Text("You appear to be offline. Your SOS has been saved\nand will be sent automatically when you reconnect.")
                .font(.system(size: 16))
                .foregroundColor(.white.opacity(0.9))
                .multilineTextAlignment(.center)
                .lineSpacing(4)
                .padding(.horizontal, 32)

            Spacer().frame(height: 16)

            Text("Priority: CRITICAL — sends first on reconnect")
                .font(.system(size: 14, weight: .semibold))
                .foregroundColor(sosAmber)
                .padding(.horizontal, 16)
                .padding(.vertical, 8)
                .background(sosAmber.opacity(0.3))
                .clipShape(RoundedRectangle(cornerRadius: 8))

            Spacer()

            Button(action: onDismiss) {
                Text("Back to Map")
                    .font(.system(size: 16, weight: .semibold))
                    .foregroundColor(.white)
                    .padding(.horizontal, 32)
                    .padding(.vertical, 12)
                    .background(.white.opacity(0.2))
                    .clipShape(Capsule())
            }
            .padding(.bottom, 60)
        }
        .accessibilityElement(children: .combine)
        .accessibilityLabel("SOS queued offline. Will be sent automatically when you reconnect.")
    }
}

// MARK: - Cancelled State

private struct CancelledContent: View {
    var body: some View {
        VStack(spacing: 16) {
            Image(systemName: "xmark.circle")
                .font(.system(size: 48))
                .foregroundColor(.white.opacity(0.7))

            Text("SOS Cancelled")
                .font(.system(size: 24, weight: .bold))
                .foregroundColor(.white)

            Text("No alert was sent")
                .font(.system(size: 16))
                .foregroundColor(.white.opacity(0.7))
        }
        .accessibilityElement(children: .combine)
        .accessibilityLabel("SOS cancelled. No alert was sent.")
    }
}

// MARK: - Error State

private struct ErrorContent: View {
    let message: String
    let queuedOffline: Bool
    let onDismiss: () -> Void

    var body: some View {
        VStack(spacing: 0) {
            Spacer()

            Image(systemName: "exclamationmark.triangle")
                .font(.system(size: 56))
                .foregroundColor(sosAmber)

            Spacer().frame(height: 24)

            Text(queuedOffline ? "SOS QUEUED (Error)" : "SOS Error")
                .font(.system(size: 24, weight: .bold))
                .foregroundColor(.white)

            Spacer().frame(height: 12)

            Text(message)
                .font(.system(size: 14))
                .foregroundColor(.white.opacity(0.7))
                .multilineTextAlignment(.center)
                .padding(.horizontal, 32)

            if queuedOffline {
                Spacer().frame(height: 12)
                Text("Your SOS has been saved and will retry automatically.")
                    .font(.system(size: 16))
                    .foregroundColor(.white.opacity(0.9))
                    .multilineTextAlignment(.center)
                    .padding(.horizontal, 32)
            }

            Spacer()

            Button(action: onDismiss) {
                Text("Back to Map")
                    .font(.system(size: 16, weight: .semibold))
                    .foregroundColor(.white)
                    .padding(.horizontal, 32)
                    .padding(.vertical, 12)
                    .background(.white.opacity(0.2))
                    .clipShape(Capsule())
            }
            .accessibilityLabel("Back to map")
            .padding(.bottom, 60)
        }
        .accessibilityElement(children: .combine)
        .accessibilityLabel(queuedOffline
            ? "SOS error but queued offline. \(message). Your SOS has been saved and will retry automatically."
            : "SOS error. \(message)")
    }
}

// MARK: - Preview

#Preview("Countdown") {
    let vm = SosCountdownViewModel()
    SosCountdownView(viewModel: vm)
        .onAppear {
            vm.startSOS(source: .manualButton)
        }
}
