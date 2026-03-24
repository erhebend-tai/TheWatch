// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:         GuardianConsentView.swift
// Purpose:      SwiftUI view for the Guardian Consent step in the sign-up
//               flow. Shown when a user's date of birth indicates they are
//               under 18. Collects guardian information, sends a consent
//               request, and verifies the consent code. Integrates into
//               the existing multi-step SignUpView flow.
// Created:      2026-03-24
// Author:       Claude
// Dependencies: SwiftUI, GuardianConsentViewModel.swift,
//               GuardianConsentService.swift
// Related:      GuardianConsentViewModel.swift (view model),
//               SignUpView.swift (parent flow - inserts this step if minor),
//               AppRouter.swift (route: .guardianConsent)
//
// Usage Example:
//   let service = MockGuardianConsentService()
//   GuardianConsentView(
//       viewModel: GuardianConsentViewModel(consentService: service),
//       minorEmail: "kid@example.com",
//       onConsentVerified: { print("Consent granted, proceed with sign-up") }
//   )
//
// Potential Additions:
//   - In-person QR code scan for guardian verification
//   - Video call verification widget
//   - Document upload for legal guardianship proof
//   - Multi-language consent forms
// ============================================================================

import SwiftUI

struct GuardianConsentView: View {
    @Bindable var viewModel: GuardianConsentViewModel
    let minorEmail: String
    var onConsentVerified: (() -> Void)?

    var body: some View {
        ZStack {
            Color(red: 0.97, green: 0.97, blue: 0.97)
                .ignoresSafeArea()

            ScrollView {
                VStack(spacing: 20) {
                    // Header
                    headerSection

                    // Step indicator
                    stepIndicator

                    // Current step content
                    switch viewModel.currentStep {
                    case .guardianInfo:
                        guardianInfoForm
                    case .waitingForConsent:
                        waitingSection
                    case .enterCode:
                        codeEntrySection
                    case .verified:
                        verifiedSection
                    }

                    // Messages
                    messagesSection
                }
                .padding(16)
            }
        }
        .navigationTitle("Guardian Consent")
        .navigationBarTitleDisplayMode(.inline)
    }

    // MARK: - Header

    private var headerSection: some View {
        VStack(spacing: 8) {
            Image(systemName: "person.2.badge.gearshape")
                .font(.system(size: 40))
                .foregroundColor(Color(red: 0.9, green: 0.22, blue: 0.27))

            Text("Parental / Guardian Consent Required")
                .font(.headline)
                .multilineTextAlignment(.center)

            Text("Because you are under 18, a legal guardian must verify and consent to your account creation. This is required by law (COPPA/GDPR).")
                .font(.caption)
                .foregroundColor(.secondary)
                .multilineTextAlignment(.center)
        }
        .padding(.vertical, 8)
    }

    // MARK: - Step Indicator

    private var stepIndicator: some View {
        HStack(spacing: 0) {
            ForEach(GuardianConsentViewModel.ConsentStep.allCases, id: \.rawValue) { step in
                ZStack {
                    Circle()
                        .fill(
                            step.rawValue <= viewModel.currentStep.rawValue
                                ? Color(red: 0.9, green: 0.22, blue: 0.27)
                                : Color.gray.opacity(0.3)
                        )
                    Text("\(step.rawValue)")
                        .foregroundColor(.white)
                        .fontWeight(.bold)
                        .font(.caption)
                }
                .frame(width: 30, height: 30)

                if step.rawValue < GuardianConsentViewModel.ConsentStep.allCases.count {
                    Rectangle()
                        .fill(
                            step.rawValue < viewModel.currentStep.rawValue
                                ? Color(red: 0.9, green: 0.22, blue: 0.27)
                                : Color.gray.opacity(0.3)
                        )
                        .frame(height: 2)
                }
            }
        }
        .padding(.horizontal, 32)
    }

    // MARK: - Guardian Info Form

    private var guardianInfoForm: some View {
        VStack(spacing: 12) {
            Text("Guardian Information")
                .font(.subheadline.bold())

            TextField("Guardian's Full Name", text: $viewModel.guardianName)
                .textContentType(.name)
                .padding(12)
                .background(Color.white)
                .cornerRadius(8)
                .accessibilityLabel("Guardian's full name")

            TextField("Guardian's Email", text: $viewModel.guardianEmail)
                .textContentType(.emailAddress)
                .keyboardType(.emailAddress)
                .autocapitalization(.none)
                .padding(12)
                .background(Color.white)
                .cornerRadius(8)
                .accessibilityLabel("Guardian's email address")

            TextField("Guardian's Phone (optional)", text: $viewModel.guardianPhone)
                .textContentType(.telephoneNumber)
                .keyboardType(.phonePad)
                .padding(12)
                .background(Color.white)
                .cornerRadius(8)
                .accessibilityLabel("Guardian's phone number, optional")

            Picker("Relationship", selection: $viewModel.relationship) {
                ForEach(GuardianRelationship.allCases, id: \.self) { rel in
                    Text(rel.rawValue).tag(rel)
                }
            }
            .pickerStyle(.menu)
            .padding(12)
            .background(Color.white)
            .cornerRadius(8)
            .accessibilityLabel("Relationship to guardian")

            Button(action: {
                Task {
                    await viewModel.sendConsentRequest(minorEmail: minorEmail)
                }
            }) {
                if viewModel.isLoading {
                    HStack(spacing: 8) {
                        ProgressView()
                            .tint(.white)
                        Text("Sending...")
                    }
                } else {
                    Text("Send Consent Request")
                        .fontWeight(.semibold)
                }
            }
            .frame(maxWidth: .infinity)
            .padding(12)
            .background(
                viewModel.isGuardianInfoValid
                    ? Color(red: 0.9, green: 0.22, blue: 0.27)
                    : Color.gray
            )
            .foregroundColor(.white)
            .cornerRadius(8)
            .disabled(!viewModel.isGuardianInfoValid || viewModel.isLoading)
            .accessibilityLabel("Send consent request to guardian")
        }
    }

    // MARK: - Waiting Section

    private var waitingSection: some View {
        VStack(spacing: 16) {
            ProgressView()
                .scaleEffect(1.5)

            Text("Waiting for guardian response...")
                .font(.subheadline)
                .foregroundColor(.secondary)

            Button("Check Status") {
                Task { await viewModel.checkStatus() }
            }
            .font(.subheadline.bold())
            .foregroundColor(Color(red: 0.9, green: 0.22, blue: 0.27))
        }
        .padding(.vertical, 24)
    }

    // MARK: - Code Entry

    private var codeEntrySection: some View {
        VStack(spacing: 12) {
            Text("Enter Verification Code")
                .font(.subheadline.bold())

            Text("Your guardian received a 6-digit code at \(viewModel.guardianEmail). Enter it below.")
                .font(.caption)
                .foregroundColor(.secondary)
                .multilineTextAlignment(.center)

            TextField("000000", text: $viewModel.verificationCode)
                .keyboardType(.numberPad)
                .multilineTextAlignment(.center)
                .font(.system(size: 32, weight: .bold, design: .monospaced))
                .padding(12)
                .background(Color.white)
                .cornerRadius(8)
                .frame(maxWidth: 200)
                .accessibilityLabel("6-digit verification code")

            Button(action: {
                Task { await viewModel.verifyCode() }
            }) {
                if viewModel.isLoading {
                    HStack(spacing: 8) {
                        ProgressView()
                            .tint(.white)
                        Text("Verifying...")
                    }
                } else {
                    Text("Verify Code")
                        .fontWeight(.semibold)
                }
            }
            .frame(maxWidth: .infinity)
            .padding(12)
            .background(
                viewModel.isCodeValid
                    ? Color(red: 0.9, green: 0.22, blue: 0.27)
                    : Color.gray
            )
            .foregroundColor(.white)
            .cornerRadius(8)
            .disabled(!viewModel.isCodeValid || viewModel.isLoading)
            .accessibilityLabel("Verify consent code")

            Button("Resend Code") {
                Task { await viewModel.resendRequest() }
            }
            .font(.caption.bold())
            .foregroundColor(.blue)
            .padding(.top, 8)
            .accessibilityLabel("Resend verification code to guardian")
        }
    }

    // MARK: - Verified Section

    private var verifiedSection: some View {
        VStack(spacing: 16) {
            Image(systemName: "checkmark.seal.fill")
                .font(.system(size: 60))
                .foregroundColor(.green)

            Text("Guardian Consent Verified")
                .font(.title3.bold())
                .foregroundColor(.green)

            Text("You may now continue with account creation.")
                .font(.subheadline)
                .foregroundColor(.secondary)

            if let onConsentVerified {
                Button(action: onConsentVerified) {
                    Text("Continue Sign-Up")
                        .fontWeight(.semibold)
                        .frame(maxWidth: .infinity)
                        .padding(12)
                        .background(Color(red: 0.9, green: 0.22, blue: 0.27))
                        .foregroundColor(.white)
                        .cornerRadius(8)
                }
                .accessibilityLabel("Continue to next sign-up step")
            }
        }
        .padding(.vertical, 24)
    }

    // MARK: - Messages

    private var messagesSection: some View {
        VStack(spacing: 8) {
            if let error = viewModel.errorMessage {
                Text(error)
                    .font(.caption)
                    .foregroundColor(.red)
                    .padding(12)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .background(Color.red.opacity(0.1))
                    .cornerRadius(8)
            }

            if let success = viewModel.successMessage {
                Text(success)
                    .font(.caption)
                    .foregroundColor(.green)
                    .padding(12)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .background(Color.green.opacity(0.1))
                    .cornerRadius(8)
            }
        }
    }
}

#Preview {
    NavigationStack {
        GuardianConsentView(
            viewModel: GuardianConsentViewModel(
                consentService: MockGuardianConsentService()
            ),
            minorEmail: "kid@example.com",
            onConsentVerified: { print("Consent verified") }
        )
    }
}
