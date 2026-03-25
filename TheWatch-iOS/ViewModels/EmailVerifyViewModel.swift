// EmailVerifyViewModel.swift
// TheWatch-iOS
//
// ViewModel for the email verification screen shown after signup
// when the user's email has not yet been verified.
//
// Verification flow:
//   1. User signs up -> Firebase sends a verification email automatically.
//   2. This screen is shown with a "Resend" button and an "I've Verified" button.
//   3. When the user taps the email link (in Safari/Mail), the Firebase user record
//      is updated server-side.
//   4. The user returns to the app. On foreground re-entry, this ViewModel
//      automatically calls refreshUser() to check isEmailVerified.
//   5. If verified, isVerified becomes true and the parent navigates to HomeView.
//
// Auto-check behavior:
//   - Every time the app comes back to foreground (via NotificationCenter
//     .willEnterForeground), the ViewModel reloads the Firebase user.
//   - A manual "I've Verified My Email" button also triggers a reload.
//   - A resend cooldown of 30 seconds prevents email spam.
//
// Write-Ahead Log: The resend and verification-check events are logged
// by the auth service layer for audit trail compliance.
//
// Example:
//   @State var emailVerifyVM = EmailVerifyViewModel(authService: firebaseAuth)
//   EmailVerifyView(viewModel: emailVerifyVM)

import Foundation
import Combine

@MainActor
@Observable
final class EmailVerifyViewModel {

    // ── State ────────────────────────────────────────────────────────
    var isLoading = false
    var errorMessage: String?
    var isVerified = false
    var resendCooldownSeconds: Int = 0
    var resendSuccessMessage: String?

    /// The email address to display on screen.
    let email: String

    /// True when the resend cooldown is active.
    var isResendDisabled: Bool {
        resendCooldownSeconds > 0
    }

    // ── Dependencies ─────────────────────────────────────────────────
    private let authService: any AuthServiceProtocol
    private var cooldownTimer: Timer?
    private var foregroundObserver: Any?

    // MARK: - Init

    /// - Parameters:
    ///   - authService: The auth service to use for verification operations.
    ///   - email: The email address that needs verification.
    init(authService: any AuthServiceProtocol, email: String) {
        self.authService = authService
        self.email = email
    }

    deinit {
        cooldownTimer?.invalidate()
        if let observer = foregroundObserver {
            NotificationCenter.default.removeObserver(observer)
        }
    }

    // MARK: - Lifecycle

    /// Start observing foreground transitions to auto-check verification.
    /// Call this from the View's .onAppear modifier.
    func startObservingForeground() {
        foregroundObserver = NotificationCenter.default.addObserver(
            forName: UIApplication.willEnterForegroundNotification,
            object: nil,
            queue: .main
        ) { [weak self] _ in
            guard let self else { return }
            Task { @MainActor in
                await self.checkVerification()
            }
        }
    }

    /// Stop observing foreground transitions.
    /// Call this from the View's .onDisappear modifier.
    func stopObservingForeground() {
        if let observer = foregroundObserver {
            NotificationCenter.default.removeObserver(observer)
            foregroundObserver = nil
        }
    }

    // MARK: - Actions

    /// Resend the verification email.
    func resendVerificationEmail() async {
        guard !isResendDisabled else { return }

        isLoading = true
        errorMessage = nil
        resendSuccessMessage = nil

        do {
            try await authService.sendEmailVerification()
            resendSuccessMessage = "Verification email sent to \(email)"
            startResendCooldown()
        } catch {
            errorMessage = "Failed to resend verification email. Please try again."
        }

        isLoading = false
    }

    /// Reload the Firebase user and check if the email is now verified.
    /// Called by the "I've Verified My Email" button and by the foreground observer.
    func checkVerification() async {
        isLoading = true
        errorMessage = nil

        do {
            let user = try await authService.refreshUser()
            // The auth service updates isEmailVerified internally.
            // We check the service's property directly.
            if authService.isEmailVerified {
                isVerified = true
            } else {
                errorMessage = "Email not yet verified. Check your inbox and tap the verification link."
            }
        } catch {
            errorMessage = "Could not check verification status. Please try again."
        }

        isLoading = false
    }

    // MARK: - Private

    private func startResendCooldown() {
        resendCooldownSeconds = 30

        cooldownTimer?.invalidate()
        cooldownTimer = Timer.scheduledTimer(withTimeInterval: 1.0, repeats: true) { [weak self] timer in
            Task { @MainActor in
                guard let self else {
                    timer.invalidate()
                    return
                }
                self.resendCooldownSeconds -= 1
                if self.resendCooldownSeconds <= 0 {
                    timer.invalidate()
                    self.resendCooldownSeconds = 0
                }
            }
        }
    }
}
