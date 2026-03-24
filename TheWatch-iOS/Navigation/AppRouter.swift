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
        case eula
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
        case .eula:
            EULAView()
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
