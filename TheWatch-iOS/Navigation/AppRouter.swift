import SwiftUI

@Observable
class AppRouter {
    var navigationPath = NavigationPath()
    
    enum Destination: Hashable {
        case home
        case profile
        case health
        case notifications
        case history
        case historyDetail(UUID)
        case volunteering
        case evacuation
        case contacts
        case permissions
        case settings
        case signup
        case login
        case forgotPassword
        case resetPassword(String)
        case resetPasswordWithCode(email: String, otpCode: String)
        case eula
        case twoFactor
        case emailVerification(email: String)
    }
    
    @ViewBuilder
    func view(for destination: Destination) -> some View {
        switch destination {
        case .home:
            HomeView()
        case .profile:
            ProfileView()
        case .health:
            HealthView()
        case .notifications:
            NotificationsView()
        case .history:
            HistoryView()
        case .historyDetail(let id):
            HistoryDetailView(eventId: id)
        case .volunteering:
            VolunteeringView()
        case .evacuation:
            EvacuationView()
        case .contacts:
            ContactsView()
        case .permissions:
            PermissionsView()
        case .settings:
            SettingsView()
        case .signup:
            SignUpView()
        case .login:
            LoginView()
        case .forgotPassword:
            ForgotPasswordView()
        case .resetPassword(let email):
            ResetPasswordView(email: email)
        case .resetPasswordWithCode(let email, let otpCode):
            ResetPasswordView(email: email, otpCode: otpCode)
        case .eula:
            EULAView()
        case .twoFactor:
            TwoFactorView()
        case .emailVerification(let email):
            EmailVerifyView(email: email)
        }
    }
    
    func navigateTo(_ destination: Destination) {
        navigationPath.append(destination)
    }
    
    func popToRoot() {
        navigationPath = NavigationPath()
    }
    
    func goBack() {
        navigationPath.removeLast()
    }
}
