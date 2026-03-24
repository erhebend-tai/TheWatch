import SwiftUI

struct ResetPasswordView: View {
    let email: String
    @State private var newPassword = ""
    @State private var confirmPassword = ""
    @State private var isLoading = false
    @State private var errorMessage: String?
    @State private var showPassword = false
    @State private var passwordStrength: PasswordStrength = .weak
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
                            Text("Create New Password")
                                .font(.title2)
                                .fontWeight(.bold)

                            Text("Create a strong password for your account")
                                .font(.caption)
                                .foregroundColor(.gray)
                        }
                        .frame(maxWidth: .infinity, alignment: .leading)
                        .padding(.horizontal, 16)

                        VStack(spacing: 16) {
                            // New Password
                            VStack(alignment: .leading, spacing: 8) {
                                Text("New Password")
                                    .font(.subheadline)
                                    .fontWeight(.semibold)

                                HStack {
                                    if showPassword {
                                        TextField("Password", text: $newPassword)
                                            .onChange(of: newPassword) { _, _ in
                                                updatePasswordStrength()
                                            }
                                    } else {
                                        SecureField("Password", text: $newPassword)
                                            .onChange(of: newPassword) { _, _ in
                                                updatePasswordStrength()
                                            }
                                    }

                                    Button(action: { showPassword.toggle() }) {
                                        Image(systemName: showPassword ? "eye.slash.fill" : "eye.fill")
                                            .foregroundColor(.gray)
                                    }
                                    .accessibilityLabel("Toggle password visibility")
                                }
                                .padding(12)
                                .background(Color.white)
                                .cornerRadius(8)
                            }

                            // Password Strength
                            PasswordStrengthMeter(strength: passwordStrength)

                            // Confirm Password
                            VStack(alignment: .leading, spacing: 8) {
                                Text("Confirm Password")
                                    .font(.subheadline)
                                    .fontWeight(.semibold)

                                SecureField("Confirm Password", text: $confirmPassword)
                                    .padding(12)
                                    .background(Color.white)
                                    .cornerRadius(8)
                            }

                            // Password match indicator
                            if !confirmPassword.isEmpty && newPassword != confirmPassword {
                                HStack(spacing: 8) {
                                    Image(systemName: "xmark.circle.fill")
                                        .foregroundColor(.red)
                                    Text("Passwords do not match")
                                        .font(.caption)
                                        .foregroundColor(.red)
                                }
                                .padding(12)
                                .background(Color.red.opacity(0.1))
                                .cornerRadius(8)
                            }

                            // Error message
                            if let error = errorMessage {
                                Text(error)
                                    .font(.caption)
                                    .foregroundColor(.red)
                                    .padding(12)
                                    .background(Color.red.opacity(0.1))
                                    .cornerRadius(8)
                            }

                            // Reset Button
                            Button(action: {
                                Task {
                                    isLoading = true
                                    errorMessage = nil
                                    do {
                                        try await authService.resetPassword(
                                            emailOrPhone: email,
                                            code: "123456",
                                            newPassword: newPassword
                                        )
                                        // Navigate back on success
                                        dismiss()
                                    } catch {
                                        errorMessage = "Failed to reset password"
                                    }
                                    isLoading = false
                                }
                            }) {
                                if isLoading {
                                    HStack(spacing: 8) {
                                        ProgressView()
                                            .tint(.white)
                                        Text("Resetting...")
                                    }
                                } else {
                                    Text("Reset Password")
                                        .fontWeight(.semibold)
                                }
                            }
                            .frame(maxWidth: .infinity)
                            .padding(12)
                            .background(Color(red: 0.9, green: 0.22, blue: 0.27))
                            .foregroundColor(.white)
                            .cornerRadius(8)
                            .disabled(!isFormValid || isLoading)
                            .opacity(isFormValid ? 1.0 : 0.6)
                            .accessibilityLabel("Reset password")
                        }
                        .padding(.horizontal, 16)

                        Spacer()
                    }
                    .padding(.vertical)
                }
            }
        }
    }

    private var isFormValid: Bool {
        newPassword.count >= 8 && newPassword == confirmPassword
    }

    private func updatePasswordStrength() {
        var strength = 0
        if newPassword.count >= 8 { strength += 1 }
        if newPassword.count >= 12 { strength += 1 }
        if newPassword.range(of: "[a-z]", options: .regularExpression) != nil { strength += 1 }
        if newPassword.range(of: "[A-Z]", options: .regularExpression) != nil { strength += 1 }
        if newPassword.range(of: "[0-9]", options: .regularExpression) != nil { strength += 1 }
        if newPassword.range(of: "[^a-zA-Z0-9]", options: .regularExpression) != nil { strength += 1 }

        switch strength {
        case 0...2:
            passwordStrength = .weak
        case 3...4:
            passwordStrength = .moderate
        default:
            passwordStrength = .strong
        }
    }
}

#Preview {
    ResetPasswordView(email: "alex@example.com")
        .environment(MockAuthService())
}
