// EmailVerifyView.swift
// TheWatch-iOS
//
// Email verification screen shown after signup when the user's email
// has not yet been verified via the Firebase verification link.
//
// Features:
//   - Displays the email address that needs verification
//   - "Resend Verification Email" button with 30-second cooldown
//   - "I've Verified My Email" button that reloads the Firebase user
//     and checks isEmailVerified
//   - Auto-check when the app returns to foreground (user tapped the
//     email link in Safari/Mail and returned to the app)
//   - Success/error state messaging
//   - Animated mail icon
//
// Design: Dark navy background (#002040), TheWatch brand red (#E63845).
//
// Example navigation:
//   NavigationLink(value: AppRouter.Destination.emailVerification(email: user.email)) {
//       // shown after signup completes
//   }

import SwiftUI

struct EmailVerifyView: View {
    @Environment(MockAuthService.self) var authService
    @State private var viewModel: EmailVerifyViewModel?
    @Environment(\.dismiss) var dismiss

    /// The email address to verify. Passed from the signup flow.
    let email: String

    var body: some View {
        ZStack {
            Color(red: 0.0, green: 0.125, blue: 0.3)
                .ignoresSafeArea()

            VStack(spacing: 0) {
                // Header
                HStack {
                    Button(action: { dismiss() }) {
                        HStack(spacing: 4) {
                            Image(systemName: "chevron.left")
                            Text("Back")
                        }
                        .foregroundColor(.white)
                    }
                    .accessibilityLabel("Go back")
                    Spacer()
                }
                .padding(16)

                ScrollView {
                    VStack(spacing: 28) {
                        // Mail icon
                        VStack(spacing: 16) {
                            ZStack {
                                Circle()
                                    .fill(Color.white.opacity(0.1))
                                    .frame(width: 100, height: 100)

                                Image(systemName: "envelope.badge")
                                    .font(.system(size: 44))
                                    .foregroundColor(.white)
                            }

                            Text("Verify Your Email")
                                .font(.title2)
                                .fontWeight(.bold)
                                .foregroundColor(.white)

                            Text("We've sent a verification link to:")
                                .font(.subheadline)
                                .foregroundColor(.white.opacity(0.7))

                            Text(email)
                                .font(.body)
                                .fontWeight(.semibold)
                                .foregroundColor(Color(red: 0.9, green: 0.22, blue: 0.27))
                        }
                        .frame(maxWidth: .infinity)
                        .padding(.top, 24)

                        // Instructions
                        VStack(alignment: .leading, spacing: 12) {
                            instructionRow(number: "1", text: "Open your email inbox")
                            instructionRow(number: "2", text: "Find the email from TheWatch")
                            instructionRow(number: "3", text: "Tap the verification link")
                            instructionRow(number: "4", text: "Return here and tap \"I've Verified\"")
                        }
                        .padding(16)
                        .background(Color.white.opacity(0.06))
                        .cornerRadius(12)
                        .padding(.horizontal, 16)

                        if let vm = viewModel {
                            // Success message
                            if let success = vm.resendSuccessMessage {
                                HStack(spacing: 8) {
                                    Image(systemName: "checkmark.circle.fill")
                                        .foregroundColor(.green)
                                    Text(success)
                                        .font(.caption)
                                        .foregroundColor(.green)
                                }
                                .padding(12)
                                .background(Color.green.opacity(0.1))
                                .cornerRadius(8)
                                .padding(.horizontal, 16)
                            }

                            // Error message
                            if let error = vm.errorMessage {
                                HStack(spacing: 8) {
                                    Image(systemName: "exclamationmark.triangle.fill")
                                        .foregroundColor(.red)
                                    Text(error)
                                        .font(.caption)
                                        .foregroundColor(.red)
                                }
                                .padding(12)
                                .background(Color.red.opacity(0.1))
                                .cornerRadius(8)
                                .padding(.horizontal, 16)
                            }

                            // "I've Verified My Email" button
                            Button(action: {
                                Task { await vm.checkVerification() }
                            }) {
                                if vm.isLoading {
                                    HStack(spacing: 8) {
                                        ProgressView()
                                            .tint(.white)
                                        Text("Checking...")
                                    }
                                } else {
                                    HStack(spacing: 8) {
                                        Image(systemName: "checkmark.circle")
                                        Text("I've Verified My Email")
                                            .fontWeight(.semibold)
                                    }
                                }
                            }
                            .frame(maxWidth: .infinity)
                            .padding(14)
                            .background(Color(red: 0.9, green: 0.22, blue: 0.27))
                            .foregroundColor(.white)
                            .cornerRadius(8)
                            .disabled(vm.isLoading)
                            .padding(.horizontal, 16)
                            .accessibilityLabel("Confirm email verification")
                            .accessibilityHint("Checks if you have verified your email")

                            // Resend button
                            Button(action: {
                                Task { await vm.resendVerificationEmail() }
                            }) {
                                if vm.isResendDisabled {
                                    Text("Resend email in \(vm.resendCooldownSeconds)s")
                                        .font(.subheadline)
                                        .foregroundColor(.white.opacity(0.4))
                                } else {
                                    HStack(spacing: 6) {
                                        Image(systemName: "envelope.arrow.triangle.branch")
                                        Text("Resend Verification Email")
                                            .font(.subheadline)
                                    }
                                    .foregroundColor(.white)
                                }
                            }
                            .frame(maxWidth: .infinity)
                            .padding(14)
                            .background(Color.white.opacity(0.1))
                            .cornerRadius(8)
                            .disabled(vm.isResendDisabled || vm.isLoading)
                            .opacity(vm.isResendDisabled ? 0.5 : 1.0)
                            .padding(.horizontal, 16)
                            .accessibilityLabel(vm.isResendDisabled
                                ? "Resend available in \(vm.resendCooldownSeconds) seconds"
                                : "Resend verification email"
                            )

                            // Help text
                            VStack(spacing: 8) {
                                Text("Didn't receive the email?")
                                    .font(.caption)
                                    .foregroundColor(.white.opacity(0.5))

                                Text("Check your spam folder or try a different email address.")
                                    .font(.caption2)
                                    .foregroundColor(.white.opacity(0.4))
                                    .multilineTextAlignment(.center)
                            }
                            .padding(.top, 8)
                            .padding(.horizontal, 16)
                        }

                        Spacer()
                    }
                }
            }
        }
        .navigationBarBackButtonHidden(true)
        .onAppear {
            if viewModel == nil {
                viewModel = EmailVerifyViewModel(authService: authService, email: email)
            }
            viewModel?.startObservingForeground()
        }
        .onDisappear {
            viewModel?.stopObservingForeground()
        }
    }

    // MARK: - Instruction Row

    @ViewBuilder
    private func instructionRow(number: String, text: String) -> some View {
        HStack(alignment: .top, spacing: 12) {
            ZStack {
                Circle()
                    .fill(Color(red: 0.9, green: 0.22, blue: 0.27))
                    .frame(width: 24, height: 24)

                Text(number)
                    .font(.caption2)
                    .fontWeight(.bold)
                    .foregroundColor(.white)
            }

            Text(text)
                .font(.subheadline)
                .foregroundColor(.white.opacity(0.8))

            Spacer()
        }
    }
}

// MARK: - Preview

#Preview {
    NavigationStack {
        EmailVerifyView(email: "alex@example.com")
            .environment(MockAuthService())
    }
}
