// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:         GuardianConsentViewModel.swift
// Purpose:      ViewModel for the Guardian Consent flow during sign-up for
//               users under 18. Manages the guardian info form, consent request
//               dispatch, verification code entry, and status polling. Follows
//               MVVM pattern with @Observable macro.
// Created:      2026-03-24
// Author:       Claude
// Dependencies: Foundation, GuardianConsentService.swift
// Related:      GuardianConsentView.swift (view),
//               GuardianConsentService.swift (service protocol + mock),
//               SignUpViewModel.swift (triggers this flow if DOB < 18)
//
// Usage Example:
//   let service = MockGuardianConsentService()
//   @State var vm = GuardianConsentViewModel(consentService: service)
//   // In view:
//   TextField("Guardian Email", text: $vm.guardianEmail)
//   Button("Send Request") { Task { await vm.sendConsentRequest(minorEmail: "kid@example.com") } }
//   TextField("Code", text: $vm.verificationCode)
//   Button("Verify") { Task { await vm.verifyCode() } }
//
// Potential Additions:
//   - QR code generation for in-person guardian verification
//   - Push notification integration for guardian approval
//   - Real-time status polling with WebSocket
//   - Document upload for court-appointed guardianship
// ============================================================================

import Foundation

/// ViewModel managing guardian consent flow state and interactions.
@Observable
final class GuardianConsentViewModel {

    // MARK: - Form Fields

    /// Guardian's full name
    var guardianName: String = ""

    /// Guardian's email address (required)
    var guardianEmail: String = ""

    /// Guardian's phone number (optional, for SMS)
    var guardianPhone: String = ""

    /// Relationship to the minor
    var relationship: GuardianRelationship = .parent

    /// Verification code entered by the user/guardian
    var verificationCode: String = ""

    // MARK: - State

    /// Current step in the consent flow
    var currentStep: ConsentStep = .guardianInfo

    /// Whether a network request is in progress
    var isLoading: Bool = false

    /// Error message to display
    var errorMessage: String?

    /// Success message to display
    var successMessage: String?

    /// The consent request ID returned after sending
    var consentRequestId: String?

    /// Current consent status
    var consentStatus: ConsentStatus?

    /// Whether the entire consent flow is complete and verified
    var isConsentVerified: Bool = false

    // MARK: - Steps

    enum ConsentStep: Int, CaseIterable {
        case guardianInfo = 1
        case waitingForConsent = 2
        case enterCode = 3
        case verified = 4
    }

    // MARK: - Validation

    var isGuardianInfoValid: Bool {
        !guardianName.trimmingCharacters(in: .whitespaces).isEmpty
        && isValidEmail(guardianEmail)
    }

    var isCodeValid: Bool {
        verificationCode.count == 6
        && verificationCode.allSatisfy(\.isNumber)
    }

    // MARK: - Private

    private let consentService: GuardianConsentServiceProtocol

    // MARK: - Init

    init(consentService: GuardianConsentServiceProtocol) {
        self.consentService = consentService
    }

    // MARK: - Actions

    /// Send a consent request to the guardian.
    /// - Parameter minorEmail: The email of the under-18 user signing up
    func sendConsentRequest(minorEmail: String) async {
        guard isGuardianInfoValid else {
            errorMessage = "Please provide the guardian's name and a valid email."
            return
        }

        isLoading = true
        errorMessage = nil
        successMessage = nil

        do {
            let requestId = try await consentService.sendConsentRequest(
                minorEmail: minorEmail,
                guardianEmail: guardianEmail,
                guardianPhone: guardianPhone.isEmpty ? nil : guardianPhone,
                guardianName: guardianName,
                relationship: relationship
            )

            consentRequestId = requestId
            consentStatus = .pending
            currentStep = .enterCode
            successMessage = "Consent request sent to \(guardianEmail). Ask your guardian to check their email for the verification code."
        } catch {
            errorMessage = error.localizedDescription
        }

        isLoading = false
    }

    /// Verify the consent code entered by the guardian/user.
    func verifyCode() async {
        guard let requestId = consentRequestId else {
            errorMessage = "No consent request found. Please send a request first."
            return
        }

        guard isCodeValid else {
            errorMessage = "Please enter a valid 6-digit verification code."
            return
        }

        isLoading = true
        errorMessage = nil

        do {
            let verified = try await consentService.verifyConsentCode(
                requestId: requestId,
                code: verificationCode
            )

            if verified {
                consentStatus = .verified
                isConsentVerified = true
                currentStep = .verified
                successMessage = "Guardian consent verified. You may now complete sign-up."
            }
        } catch {
            errorMessage = error.localizedDescription
        }

        isLoading = false
    }

    /// Check the current consent status (for polling).
    func checkStatus() async {
        guard let requestId = consentRequestId else { return }

        do {
            let status = try await consentService.checkConsentStatus(requestId: requestId)
            consentStatus = status

            if status == .verified {
                isConsentVerified = true
                currentStep = .verified
                successMessage = "Guardian consent verified."
            } else if status == .denied {
                errorMessage = "Guardian has denied consent."
            } else if status == .expired {
                errorMessage = "Consent request has expired. Please send a new one."
            }
        } catch {
            // Silent failure on status check
        }
    }

    /// Resend the consent request to the guardian.
    func resendRequest() async {
        guard let requestId = consentRequestId else { return }

        isLoading = true
        errorMessage = nil

        do {
            try await consentService.resendConsentRequest(requestId: requestId)
            successMessage = "Consent request resent to \(guardianEmail)."
        } catch {
            errorMessage = error.localizedDescription
        }

        isLoading = false
    }

    /// Reset the entire consent flow.
    func reset() {
        guardianName = ""
        guardianEmail = ""
        guardianPhone = ""
        relationship = .parent
        verificationCode = ""
        currentStep = .guardianInfo
        isLoading = false
        errorMessage = nil
        successMessage = nil
        consentRequestId = nil
        consentStatus = nil
        isConsentVerified = false
    }

    // MARK: - Helpers

    private func isValidEmail(_ email: String) -> Bool {
        let emailRegex = "[A-Z0-9a-z._%+-]+@[A-Za-z0-9.-]+\\.[A-Za-z]{2,}"
        let predicate = NSPredicate(format: "SELF MATCHES %@", emailRegex)
        return predicate.evaluate(with: email)
    }

    /// Determine if a date of birth indicates a minor (under 18).
    static func isMinor(dateOfBirth: Date) -> Bool {
        let calendar = Calendar.current
        let ageComponents = calendar.dateComponents([.year], from: dateOfBirth, to: Date())
        return (ageComponents.year ?? 0) < 18
    }
}
