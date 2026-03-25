// TwoFactorViewModel.swift
// TheWatch-iOS
//
// ViewModel for the two-factor authentication verification screen.
// Shown after login when the user's account has MFA enabled.
//
// Supports three MFA methods:
//   - TOTP (authenticator app such as Google Authenticator, Authy, 1Password)
//   - SMS  (6-digit code sent to the user's registered phone number)
//   - Backup code (one-time 8-character recovery code)
//
// Example flow:
//   1. User logs in with email/password.
//   2. Login succeeds but backend returns isMfaEnabled = true.
//   3. App navigates to TwoFactorView with this ViewModel.
//   4. User enters 6-digit code from their authenticator or SMS.
//   5. ViewModel calls authService.verifyMfaCode().
//   6. On success, navigation proceeds to HomeView.
//
// The SMS resend cooldown prevents users from spamming the resend button.
// Default cooldown is 60 seconds, matching industry standard (NIST SP 800-63B).
//
// Write-Ahead Log: Every verification attempt (success or failure) should be
// logged by the auth service for audit trail compliance.

import Foundation
import Combine

@MainActor
@Observable
final class TwoFactorViewModel {

    // ── Input ────────────────────────────────────────────────────────
    /// Individual digits for the 6-digit code entry.
    /// Each element corresponds to one TextField in the UI.
    var digits: [String] = Array(repeating: "", count: 6)

    /// The currently selected MFA method.
    var selectedMethod: MfaMethod = .totp

    // ── State ────────────────────────────────────────────────────────
    var isLoading = false
    var errorMessage: String?
    var isVerified = false

    /// Seconds remaining before the user can resend an SMS code.
    var resendCooldownSeconds: Int = 0

    /// True when the SMS resend cooldown is active.
    var isResendDisabled: Bool {
        resendCooldownSeconds > 0
    }

    // ── Dependencies ─────────────────────────────────────────────────
    private let authService: any AuthServiceProtocol
    private var cooldownTimer: Timer?

    // MARK: - MFA Method Enum

    enum MfaMethod: String, CaseIterable, Identifiable {
        case totp = "Authenticator App"
        case sms = "SMS"
        case backup = "Backup Code"

        var id: String { rawValue }

        /// The raw string expected by the backend API.
        var apiValue: String {
            switch self {
            case .totp: return "totp"
            case .sms: return "sms"
            case .backup: return "backup"
            }
        }

        /// SF Symbol name for the method icon.
        var iconName: String {
            switch self {
            case .totp: return "lock.shield"
            case .sms: return "message"
            case .backup: return "key"
            }
        }

        /// Maximum code length for this method.
        var codeLength: Int {
            switch self {
            case .totp, .sms: return 6
            case .backup: return 8
            }
        }
    }

    // MARK: - Computed

    /// The full code assembled from individual digit fields.
    var fullCode: String {
        digits.joined()
    }

    /// True when enough digits have been entered for the selected method.
    var isCodeComplete: Bool {
        let required = selectedMethod.codeLength
        let code = fullCode.trimmingCharacters(in: .whitespaces)
        return code.count >= required
    }

    // MARK: - Init

    init(authService: any AuthServiceProtocol) {
        self.authService = authService
    }

    deinit {
        cooldownTimer?.invalidate()
    }

    // MARK: - Actions

    /// Verify the entered code against the backend.
    func verify() async {
        guard isCodeComplete else { return }

        isLoading = true
        errorMessage = nil

        do {
            let result = try await authService.verifyMfaCode(
                code: fullCode,
                method: selectedMethod.apiValue
            )

            if result {
                isVerified = true
            } else {
                errorMessage = "Invalid code. Please try again."
                clearCode()
            }
        } catch {
            errorMessage = error.localizedDescription
            clearCode()
        }

        isLoading = false
    }

    /// Resend an SMS verification code.
    /// Only applicable when selectedMethod == .sms.
    func resendSmsCode() async {
        guard selectedMethod == .sms, !isResendDisabled else { return }

        isLoading = true
        errorMessage = nil

        do {
            // The backend resend endpoint is the same as MFA verify with a "resend" flag.
            // We call verifyMfaCode with a special empty code to trigger resend.
            // Alternatively, the enrollMfa method can be used to re-trigger SMS.
            _ = try await authService.enrollMfa(method: "sms", phoneNumber: nil)
            startResendCooldown()
        } catch {
            errorMessage = "Failed to resend code. Please try again."
        }

        isLoading = false
    }

    /// Switch the MFA method and reset the code entry.
    func switchMethod(to method: MfaMethod) {
        selectedMethod = method
        clearCode()
        errorMessage = nil

        // Adjust digit count for backup codes
        if method == .backup {
            digits = Array(repeating: "", count: 8)
        } else {
            digits = Array(repeating: "", count: 6)
        }
    }

    /// Clear all entered digits.
    func clearCode() {
        let count = selectedMethod == .backup ? 8 : 6
        digits = Array(repeating: "", count: count)
    }

    /// Handle digit entry with auto-advance.
    /// Called when a digit field changes. Trims to 1 character and advances focus.
    /// - Returns: The index of the next field to focus, or nil if this was the last.
    func handleDigitEntry(at index: Int, newValue: String) -> Int? {
        // Only keep the last character entered (handles paste of full code)
        if newValue.count > 1 {
            // User pasted a full code
            let code = newValue.prefix(digits.count)
            for (i, char) in code.enumerated() {
                digits[i] = String(char)
            }
            return min(code.count, digits.count - 1)
        }

        // Filter non-alphanumeric for backup codes, non-digit for TOTP/SMS
        if selectedMethod == .backup {
            digits[index] = String(newValue.prefix(1))
        } else {
            let filtered = newValue.filter { $0.isNumber }
            digits[index] = String(filtered.prefix(1))
        }

        // Auto-advance to next field
        if !digits[index].isEmpty && index < digits.count - 1 {
            return index + 1
        }

        return nil
    }

    /// Handle backspace: if current field is empty, move focus back.
    /// - Returns: The index of the previous field to focus, or nil.
    func handleBackspace(at index: Int) -> Int? {
        if digits[index].isEmpty && index > 0 {
            digits[index - 1] = ""
            return index - 1
        }
        return nil
    }

    // MARK: - Private

    private func startResendCooldown() {
        resendCooldownSeconds = 60

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
