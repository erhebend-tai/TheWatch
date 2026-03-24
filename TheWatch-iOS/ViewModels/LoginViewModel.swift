import Foundation
import LocalAuthentication

@Observable
final class LoginViewModel {
    var emailOrPhone = ""
    var password = ""
    var showPassword = false
    var isLoading = false
    var errorMessage: String?
    var canBiometricLogin = false

    private let authService: MockAuthService

    init(authService: MockAuthService) {
        self.authService = authService
        checkBiometricAvailability()
    }

    var isFormValid: Bool {
        !emailOrPhone.isEmpty && !password.isEmpty && isValidEmailOrPhone
    }

    var isValidEmailOrPhone: Bool {
        let emailRegex = "[A-Z0-9a-z._%+-]+@[A-Za-z0-9.-]+\\.[A-Za-z]{2,}"
        let emailPredicate = NSPredicate(format: "SELF MATCHES %@", emailRegex)
        let isEmail = emailPredicate.evaluate(with: emailOrPhone)

        if isEmail {
            return true
        }

        let phoneRegex = "^[+]?[0-9]{10,15}$"
        let phonePredicate = NSPredicate(format: "SELF MATCHES %@", phoneRegex)
        return phonePredicate.evaluate(with: emailOrPhone.replacingOccurrences(of: "-", with: ""))
    }

    func login() async {
        isLoading = true
        errorMessage = nil

        do {
            _ = try await authService.login(emailOrPhone: emailOrPhone, password: password)
        } catch {
            errorMessage = "Login failed. Please check your credentials."
        }

        isLoading = false
    }

    func biometricLogin() async {
        let context = LAContext()
        var error: NSError?

        guard context.canEvaluatePolicy(.deviceOwnerAuthenticationWithBiometrics, error: &error) else {
            errorMessage = "Biometric authentication not available"
            return
        }

        isLoading = true
        do {
            try await context.evaluatePolicy(.deviceOwnerAuthenticationWithBiometrics, localizedReason: "Authenticate to log in to TheWatch")
            _ = try await authService.biometricLogin()
        } catch {
            errorMessage = "Biometric authentication failed"
        }
        isLoading = false
    }

    private func checkBiometricAvailability() {
        let context = LAContext()
        var error: NSError?
        canBiometricLogin = context.canEvaluatePolicy(.deviceOwnerAuthenticationWithBiometrics, error: &error)
    }
}
