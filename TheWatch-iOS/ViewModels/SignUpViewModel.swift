import Foundation

@Observable
final class SignUpViewModel {
    // Step 1
    var firstName = ""
    var lastName = ""
    var email = ""
    var phone = ""
    var password = ""
    var confirmPassword = ""
    var dateOfBirth = Date()

    // Step 2
    var emergencyContacts: [EmergencyContact] = []

    // Step 3
    var acceptEULA = false

    var currentStep = 1
    var isLoading = false
    var errorMessage: String?

    private let authService: MockAuthService

    init(authService: MockAuthService) {
        self.authService = authService
    }

    var passwordStrength: PasswordStrength {
        calculatePasswordStrength(password)
    }

    var isStep1Valid: Bool {
        !firstName.isEmpty && !lastName.isEmpty && isValidEmail(email) &&
        isValidPhone(phone) && password.count >= 8 && password == confirmPassword
    }

    var isStep2Valid: Bool {
        emergencyContacts.count > 0 && emergencyContacts.count <= 3
    }

    var isStep3Valid: Bool {
        acceptEULA && isStep1Valid && isStep2Valid
    }

    func addEmergencyContact(_ contact: EmergencyContact) {
        if emergencyContacts.count < 3 {
            emergencyContacts.append(contact)
        }
    }

    func removeEmergencyContact(_ id: String) {
        emergencyContacts.removeAll { $0.id == id }
    }

    func nextStep() {
        if currentStep < 3 {
            currentStep += 1
        }
    }

    func previousStep() {
        if currentStep > 1 {
            currentStep -= 1
        }
    }

    func completeSignUp() async {
        isLoading = true
        errorMessage = nil

        let newUser = User(
            email: email,
            phone: phone,
            firstName: firstName,
            lastName: lastName,
            dateOfBirth: dateOfBirth
        )

        do {
            _ = try await authService.signup(user: newUser, password: password, emergencyContacts: emergencyContacts)
        } catch {
            errorMessage = "Sign up failed. Please try again."
        }

        isLoading = false
    }

    private func isValidEmail(_ email: String) -> Bool {
        let emailRegex = "[A-Z0-9a-z._%+-]+@[A-Za-z0-9.-]+\\.[A-Za-z]{2,}"
        let emailPredicate = NSPredicate(format: "SELF MATCHES %@", emailRegex)
        return emailPredicate.evaluate(with: email)
    }

    private func isValidPhone(_ phone: String) -> Bool {
        let phoneRegex = "^[+]?[0-9]{10,15}$"
        let phonePredicate = NSPredicate(format: "SELF MATCHES %@", phoneRegex)
        return phonePredicate.evaluate(with: phone.replacingOccurrences(of: "-", with: ""))
    }

    private func calculatePasswordStrength(_ password: String) -> PasswordStrength {
        var strength = 0

        if password.count >= 8 { strength += 1 }
        if password.count >= 12 { strength += 1 }
        if password.range(of: "[a-z]", options: .regularExpression) != nil { strength += 1 }
        if password.range(of: "[A-Z]", options: .regularExpression) != nil { strength += 1 }
        if password.range(of: "[0-9]", options: .regularExpression) != nil { strength += 1 }
        if password.range(of: "[^a-zA-Z0-9]", options: .regularExpression) != nil { strength += 1 }

        switch strength {
        case 0...2:
            return .weak
        case 3...4:
            return .moderate
        default:
            return .strong
        }
    }
}

enum PasswordStrength {
    case weak
    case moderate
    case strong

    var color: String {
        switch self {
        case .weak:
            return "red"
        case .moderate:
            return "orange"
        case .strong:
            return "green"
        }
    }

    var displayName: String {
        switch self {
        case .weak:
            return "Weak"
        case .moderate:
            return "Moderate"
        case .strong:
            return "Strong"
        }
    }
}
