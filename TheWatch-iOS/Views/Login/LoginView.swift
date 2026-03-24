import SwiftUI
import Network

struct LoginView: View {
    @Environment(MockAuthService.self) var authService
    @State var router = AppRouter()
    @State var viewModel: LoginViewModel?
    @State private var pathMonitor = NWPathMonitor()
    @State private var isOnline = true

    var body: some View {
        NavigationStack(path: $router.navigationPath) {
            ZStack {
                Color(red: 0.0, green: 0.125, blue: 0.3)
                    .ignoresSafeArea()

                VStack(spacing: 0) {
                    if !isOnline {
                        OfflineBanner()
                    }

                    ScrollView {
                        VStack(spacing: 24) {
                            // Header
                            VStack(spacing: 8) {
                                Image(systemName: "shield.fill")
                                    .font(.system(size: 48))
                                    .foregroundColor(.white)

                                Text("TheWatch")
                                    .font(.system(size: 28, weight: .bold))
                                    .foregroundColor(.white)

                                Text("Life-Safety Emergency Response")
                                    .font(.caption)
                                    .foregroundColor(.white.opacity(0.8))
                            }
                            .frame(maxWidth: .infinity)
                            .padding(.vertical, 32)

                            // Form
                            VStack(spacing: 16) {
                                // Email/Phone
                                VStack(alignment: .leading, spacing: 8) {
                                    Text("Email or Phone")
                                        .font(.subheadline)
                                        .foregroundColor(.white)

                                    TextField("alex@example.com or +1-555-0123", text: $viewModel!.emailOrPhone)
                                        .textContentType(.emailAddress)
                                        .keyboardType(.emailAddress)
                                        .padding(12)
                                        .background(Color.white)
                                        .cornerRadius(8)
                                        .accessibilityLabel("Email or phone number")
                                        .accessibilityHint("Enter your email address or phone number")
                                }

                                // Password
                                VStack(alignment: .leading, spacing: 8) {
                                    Text("Password")
                                        .font(.subheadline)
                                        .foregroundColor(.white)

                                    HStack {
                                        if viewModel?.showPassword == true {
                                            TextField("Password", text: $viewModel!.password)
                                        } else {
                                            SecureField("Password", text: $viewModel!.password)
                                        }

                                        Button(action: { viewModel?.showPassword.toggle() }) {
                                            Image(systemName: (viewModel?.showPassword ?? false) ? "eye.slash.fill" : "eye.fill")
                                                .foregroundColor(.gray)
                                        }
                                        .accessibilityLabel("Toggle password visibility")
                                    }
                                    .padding(12)
                                    .background(Color.white)
                                    .cornerRadius(8)
                                    .accessibilityElement(children: .combine)
                                }

                                // Error message
                                if let error = viewModel?.errorMessage {
                                    Text(error)
                                        .font(.caption)
                                        .foregroundColor(.red)
                                        .padding(12)
                                        .background(Color.red.opacity(0.1))
                                        .cornerRadius(8)
                                }
                            }
                            .padding(.horizontal, 16)

                            // Login Button
                            Button(action: {
                                Task {
                                    await viewModel?.login()
                                }
                            }) {
                                if viewModel?.isLoading == true {
                                    HStack(spacing: 8) {
                                        ProgressView()
                                            .tint(.white)
                                        Text("Logging in...")
                                    }
                                } else {
                                    Text("Log In")
                                        .fontWeight(.semibold)
                                }
                            }
                            .frame(maxWidth: .infinity)
                            .padding(12)
                            .background(Color(red: 0.9, green: 0.22, blue: 0.27))
                            .foregroundColor(.white)
                            .cornerRadius(8)
                            .disabled(!(viewModel?.isFormValid ?? false) || viewModel?.isLoading == true)
                            .opacity((viewModel?.isFormValid ?? false) ? 1.0 : 0.6)
                            .padding(.horizontal, 16)
                            .accessibilityLabel("Log in button")
                            .accessibilityHint(!(viewModel?.isFormValid ?? false) ? "Email/phone and password required" : "")

                            // Biometric Login
                            if viewModel?.canBiometricLogin == true {
                                Button(action: {
                                    Task {
                                        await viewModel?.biometricLogin()
                                    }
                                }) {
                                    HStack(spacing: 8) {
                                        Image(systemName: "faceid")
                                        Text("Login with Face ID")
                                            .fontWeight(.semibold)
                                    }
                                }
                                .frame(maxWidth: .infinity)
                                .padding(12)
                                .background(Color.white.opacity(0.1))
                                .foregroundColor(.white)
                                .cornerRadius(8)
                                .padding(.horizontal, 16)
                                .accessibilityLabel("Login with Face ID")
                            }

                            // Links
                            VStack(spacing: 12) {
                                NavigationLink(value: AppRouter.Destination.forgotPassword) {
                                    Text("Forgot Password?")
                                        .font(.caption)
                                        .foregroundColor(.white)
                                }

                                NavigationLink(value: AppRouter.Destination.signup) {
                                    Text("Create New Account")
                                        .font(.caption)
                                        .foregroundColor(.white)
                                }
                            }
                            .padding(.horizontal, 16)

                            // Hardware SOS Bypass
                            Button(action: {}) {
                                Text("Hardware SOS Bypass (Testing)")
                                    .font(.caption2)
                                    .foregroundColor(.white.opacity(0.6))
                            }
                            .padding(.horizontal, 16)

                            Spacer()
                        }
                    }
                }
                .navigationDestination(for: AppRouter.Destination.self) { destination in
                    router.view(for: destination)
                }
            }
            .onAppear {
                if viewModel == nil {
                    viewModel = LoginViewModel(authService: authService)
                }

                let queue = DispatchQueue.global()
                let monitor = NWPathMonitor()
                monitor.start(queue: queue)
                pathMonitor = monitor

                pathMonitor.pathUpdateHandler = { path in
                    DispatchQueue.main.async {
                        isOnline = path.status == .satisfied
                    }
                }
            }
        }
    }
}

#Preview {
    LoginView()
        .environment(MockAuthService())
}
