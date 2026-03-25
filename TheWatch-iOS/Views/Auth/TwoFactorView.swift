// TwoFactorView.swift
// TheWatch-iOS
//
// Two-factor authentication verification screen.
// Presented after login when the user's account has MFA enabled.
//
// Features:
//   - 6-digit code input with individual TextFields that auto-advance on digit entry
//   - Method selector: TOTP (Authenticator App) / SMS / Backup Code
//   - "Verify" button calls backend POST /api/account/mfa/verify
//   - Countdown timer for SMS resend cooldown (60s, per NIST SP 800-63B)
//   - Haptic feedback on successful verification
//   - VoiceOver accessibility labels on every interactive element
//
// Example navigation:
//   NavigationLink(value: AppRouter.Destination.twoFactor) {
//       // triggered after login detects MFA is enabled
//   }
//
// Design: Dark navy background (#002040) consistent with LoginView,
// TheWatch brand red (#E63845) for primary actions.

import SwiftUI

struct TwoFactorView: View {
    @Environment(MockAuthService.self) var authService
    @State private var viewModel: TwoFactorViewModel?
    @State private var focusedFieldIndex: Int? = 0
    @Environment(\.dismiss) var dismiss

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
                    .accessibilityLabel("Go back to login")
                    Spacer()
                }
                .padding(16)

                ScrollView {
                    VStack(spacing: 28) {
                        // Icon and title
                        VStack(spacing: 12) {
                            Image(systemName: "lock.shield.fill")
                                .font(.system(size: 48))
                                .foregroundColor(.white)

                            Text("Two-Factor Authentication")
                                .font(.title2)
                                .fontWeight(.bold)
                                .foregroundColor(.white)

                            Text("Enter the verification code to continue")
                                .font(.subheadline)
                                .foregroundColor(.white.opacity(0.7))
                        }
                        .frame(maxWidth: .infinity)
                        .padding(.top, 16)

                        // Method selector
                        if let vm = viewModel {
                            methodSelector(vm: vm)

                            // Code entry
                            codeEntryFields(vm: vm)

                            // Error message
                            if let error = vm.errorMessage {
                                Text(error)
                                    .font(.caption)
                                    .foregroundColor(.red)
                                    .padding(12)
                                    .background(Color.red.opacity(0.15))
                                    .cornerRadius(8)
                                    .padding(.horizontal, 16)
                            }

                            // Verify button
                            Button(action: {
                                Task { await vm.verify() }
                            }) {
                                if vm.isLoading {
                                    HStack(spacing: 8) {
                                        ProgressView()
                                            .tint(.white)
                                        Text("Verifying...")
                                    }
                                } else {
                                    Text("Verify")
                                        .fontWeight(.semibold)
                                }
                            }
                            .frame(maxWidth: .infinity)
                            .padding(14)
                            .background(Color(red: 0.9, green: 0.22, blue: 0.27))
                            .foregroundColor(.white)
                            .cornerRadius(8)
                            .disabled(!vm.isCodeComplete || vm.isLoading)
                            .opacity(vm.isCodeComplete ? 1.0 : 0.6)
                            .padding(.horizontal, 16)
                            .accessibilityLabel("Verify code")
                            .accessibilityHint(vm.isCodeComplete ? "Tap to verify your code" : "Enter all digits first")

                            // SMS resend button (only for SMS method)
                            if vm.selectedMethod == .sms {
                                smsResendButton(vm: vm)
                            }

                            Spacer()
                        }
                    }
                }
            }
        }
        .navigationBarBackButtonHidden(true)
        .onAppear {
            if viewModel == nil {
                viewModel = TwoFactorViewModel(authService: authService)
            }
        }
    }

    // MARK: - Method Selector

    @ViewBuilder
    private func methodSelector(vm: TwoFactorViewModel) -> some View {
        VStack(spacing: 8) {
            Text("Verification Method")
                .font(.caption)
                .foregroundColor(.white.opacity(0.6))
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.horizontal, 16)

            HStack(spacing: 8) {
                ForEach(TwoFactorViewModel.MfaMethod.allCases) { method in
                    Button(action: {
                        vm.switchMethod(to: method)
                        focusedFieldIndex = 0
                    }) {
                        VStack(spacing: 6) {
                            Image(systemName: method.iconName)
                                .font(.system(size: 18))
                            Text(method.rawValue)
                                .font(.caption2)
                                .lineLimit(1)
                                .minimumScaleFactor(0.8)
                        }
                        .frame(maxWidth: .infinity)
                        .padding(.vertical, 10)
                        .background(
                            vm.selectedMethod == method
                                ? Color(red: 0.9, green: 0.22, blue: 0.27)
                                : Color.white.opacity(0.1)
                        )
                        .foregroundColor(.white)
                        .cornerRadius(8)
                    }
                    .accessibilityLabel("Use \(method.rawValue)")
                    .accessibilityAddTraits(vm.selectedMethod == method ? .isSelected : [])
                }
            }
            .padding(.horizontal, 16)
        }
    }

    // MARK: - Code Entry Fields

    @ViewBuilder
    private func codeEntryFields(vm: TwoFactorViewModel) -> some View {
        VStack(spacing: 12) {
            Text(vm.selectedMethod == .backup ? "Enter backup code" : "Enter 6-digit code")
                .font(.caption)
                .foregroundColor(.white.opacity(0.6))
                .frame(maxWidth: .infinity, alignment: .leading)

            HStack(spacing: 8) {
                ForEach(0..<vm.digits.count, id: \.self) { index in
                    SingleDigitField(
                        text: Binding(
                            get: { vm.digits[index] },
                            set: { newValue in
                                if let nextIndex = vm.handleDigitEntry(at: index, newValue: newValue) {
                                    focusedFieldIndex = nextIndex
                                }
                            }
                        ),
                        isFocused: focusedFieldIndex == index,
                        isBackupCode: vm.selectedMethod == .backup,
                        onTap: {
                            focusedFieldIndex = index
                        },
                        onBackspace: {
                            if let prevIndex = vm.handleBackspace(at: index) {
                                focusedFieldIndex = prevIndex
                            }
                        }
                    )
                    .accessibilityLabel("Digit \(index + 1)")

                    // Add a dash separator in the middle for 6-digit codes
                    if vm.selectedMethod != .backup && index == 2 {
                        Text("-")
                            .foregroundColor(.white.opacity(0.4))
                            .font(.title3)
                    }
                }
            }
        }
        .padding(.horizontal, 16)
    }

    // MARK: - SMS Resend Button

    @ViewBuilder
    private func smsResendButton(vm: TwoFactorViewModel) -> some View {
        VStack(spacing: 4) {
            Button(action: {
                Task { await vm.resendSmsCode() }
            }) {
                if vm.isResendDisabled {
                    Text("Resend code in \(vm.resendCooldownSeconds)s")
                        .font(.caption)
                        .foregroundColor(.white.opacity(0.4))
                } else {
                    Text("Resend SMS Code")
                        .font(.caption)
                        .foregroundColor(Color(red: 0.9, green: 0.22, blue: 0.27))
                }
            }
            .disabled(vm.isResendDisabled || vm.isLoading)
            .padding(.horizontal, 16)
            .accessibilityLabel(vm.isResendDisabled
                ? "Resend available in \(vm.resendCooldownSeconds) seconds"
                : "Resend SMS code"
            )
        }
        .padding(.top, 8)
    }
}

// MARK: - Single Digit Field

/// A single character text field for code entry.
/// Styled as a rounded box that highlights when focused.
private struct SingleDigitField: View {
    @Binding var text: String
    let isFocused: Bool
    let isBackupCode: Bool
    let onTap: () -> Void
    let onBackspace: () -> Void

    var body: some View {
        TextField("", text: $text)
            .keyboardType(isBackupCode ? .asciiCapable : .numberPad)
            .textContentType(.oneTimeCode)
            .multilineTextAlignment(.center)
            .font(.title2.monospacedDigit().weight(.bold))
            .foregroundColor(.white)
            .frame(width: isBackupCode ? 34 : 44, height: 52)
            .background(
                RoundedRectangle(cornerRadius: 8)
                    .fill(Color.white.opacity(isFocused ? 0.2 : 0.08))
            )
            .overlay(
                RoundedRectangle(cornerRadius: 8)
                    .stroke(
                        isFocused
                            ? Color(red: 0.9, green: 0.22, blue: 0.27)
                            : Color.white.opacity(0.2),
                        lineWidth: isFocused ? 2 : 1
                    )
            )
            .onTapGesture { onTap() }
            .onChange(of: text) { oldValue, newValue in
                if newValue.isEmpty && !oldValue.isEmpty {
                    onBackspace()
                }
            }
    }
}

// MARK: - Preview

#Preview {
    NavigationStack {
        TwoFactorView()
            .environment(MockAuthService())
    }
}
