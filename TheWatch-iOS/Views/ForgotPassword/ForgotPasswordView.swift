import SwiftUI

struct ForgotPasswordView: View {
    @State private var emailOrPhone = ""
    @State private var otpCode = ""
    @State private var showOTPEntry = false
    @State private var isLoading = false
    @State private var errorMessage: String?
    @State private var navigateToReset = false
    @Environment(MockAuthService.self) var authService
    @Environment(\.dismiss) var dismiss

    var body: some View {
        ZStack {
            Color(red: 0.97, green: 0.97, blue: 0.97)
                .ignoresSafeArea()

            VStack(spacing: 0) {
                // Header
                HStack {
                    Button(action: { dismiss() }) {
                        HStack(spacing: 4) {
                            Image(systemName: "chevron.left")
                            Text("Back")
                        }
                        .foregroundColor(Color(red: 0.9, green: 0.22, blue: 0.27))
                    }
                    Spacer()
                }
                .padding(16)

                ScrollView {
                    VStack(spacing: 24) {
                        // Title
                        VStack(spacing: 8) {
                            Text("Forgot Password")
                                .font(.title2)
                                .fontWeight(.bold)

                            Text("Enter your email or phone number to reset your password")
                                .font(.caption)
                                .foregroundColor(.gray)
                        }
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(.horizontal, 16)

                        if !showOTPEntry {
                            // Email/Phone input
                            VStack(spacing: 16) {
                                TextField("Email or Phone", text: $emailOrPhone)
                                    .textContentType(.emailAddress)
                                    .keyboardType(.emailAddress)
                                    .padding(12)
                                    .background(Color.white)
                                    .cornerRadius(8)
                                    .accessibilityLabel("Email or phone number")

                                if let error = errorMessage {
                                    Text(error)
                                        .font(.caption)
                                        .foregroundColor(.red)
                                        .padding(12)
                                        .background(Color.red.opacity(0.1))
                                        .cornerRadius(8)
                                }

                                Button(action: {
                                    Task {
                                        isLoading = true
                                        errorMessage = nil
                                        do {
                                            _ = try await authService.forgotPassword(emailOrPhone: emailOrPhone)
                                            showOTPEntry = true
                                        } catch {
                                            errorMessage = "Failed to send reset code"
                                        }
                                        isLoading = false
                                    }
                                }) {
                                    if isLoading {
                                        HStack(spacing: 8) {
                                            ProgressView()
                                                .tint(.white)
                                            Text("Sending...")
                                        }
                                    } else {
                                        Text("Send Reset Code")
                                            .fontWeight(.semibold)
                                    }
                                }
                                .frame(maxWidth: .infinity)
                                .padding(12)
                                .background(Color(red: 0.9, green: 0.22, blue: 0.27))
                                .foregroundColor(.white)
                                .cornerRadius(8)
                                .disabled(emailOrPhone.isEmpty || isLoading)
                                .opacity(emailOrPhone.isEmpty ? 0.6 : 1.0)
                                .accessibilityLabel("Send reset code")
                            }
                            .padding(.horizontal, 16)
                        } else {
                            // OTP Entry
                            VStack(spacing: 16) {
                                Text("We've sent a 6-digit code to \(emailOrPhone)")
                                    .font(.caption)
                                    .foregroundColor(.gray)
                                    .frame(maxWidth: .infinity, alignment: .leading)

                                TextField("000000", text: $otpCode)
                                    .keyboardType(.numberPad)
                                    .tracking(4)
                                    .font(.title2.monospacedDigit())
                                    .padding(12)
                                    .background(Color.white)
                                    .cornerRadius(8)
                                    .accessibilityLabel("6-digit code")

                                Button(action: {
                                    guard otpCode.count == 6 else { return }
                                    navigateToReset = true
                                }) {
                                    if isLoading {
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
                                .background(Color(red: 0.9, green: 0.22, blue: 0.27))
                                .foregroundColor(.white)
                                .cornerRadius(8)
                                .disabled(otpCode.count != 6 || isLoading)
                                .opacity(otpCode.count == 6 ? 1.0 : 0.6)
                                .accessibilityLabel("Verify code")

                                Button(action: {
                                    showOTPEntry = false
                                    otpCode = ""
                                    errorMessage = nil
                                }) {
                                    Text("Back")
                                        .frame(maxWidth: .infinity)
                                        .padding(12)
                                        .background(Color.gray.opacity(0.1))
                                        .foregroundColor(.black)
                                        .cornerRadius(8)
                                }
                                .accessibilityLabel("Go back")
                            }
                            .padding(.horizontal, 16)
                        }

                        Spacer()
                    }
                    .padding(.vertical)
                }
            }
        }
        .navigationDestination(isPresented: $navigateToReset) {
            ResetPasswordView(email: emailOrPhone, otpCode: otpCode)
        }
    }
}

#Preview {
    NavigationStack {
        ForgotPasswordView()
            .environment(MockAuthService())
    }
}
