import SwiftUI
import LocalAuthentication

struct LoginView: View {
    @Environment(MockAuthService.self) var authService
    @State var router = AppRouter()
    @State private var email = ""
    @State private var password = ""
    @State private var isLoading = false
    @State private var errorMessage: String?
    @State private var isBiometricAvailable = false
    @State private var showPassword = false
    @Environment(\\.dismiss) var dismiss
    
    var body: some View {
        NavigationStack(path: $router.navigationPath) {
            ZStack {
                Color(red: 0.97, green: 0.97, blue: 0.97)
                    .ignoresSafeArea()
                
                VStack(spacing: 0) {
                    // Header
                    VStack(spacing: 8) {
                        Text("TheWatch")
                            .font(.title)
                            .fontWeight(.bold)
                            .foregroundColor(Color(red: 0.9, green: 0.22, blue: 0.27))
                        
                        Text("Life-Safety Emergency Response")
                            .font(.caption)
                            .foregroundColor(.gray)
                    }
                    .padding(.vertical, 32)
                    
                    ScrollView {
                        VStack(spacing: 20) {
                            // Email field
                            VStack(alignment: .leading, spacing: 8) {
                                Text("Email or Phone")
                                    .font(.subheadline)
                                    .fontWeight(.semibold)
                                
                                TextField("Email or phone number", text: $email)
                                    .textContentType(.emailAddress)
                                    .keyboardType(.emailAddress)
                                    .padding(12)
                                    .background(Color.white)
                                    .cornerRadius(8)
                                    .accessibilityLabel("Email or phone number")
                            }
                            
                            // Password field
                            VStack(alignment: .leading, spacing: 8) {
                                Text("Password")
                                    .font(.subheadline)
                                    .fontWeight(.semibold)
                                
                                HStack {
                                    if showPassword {
                                        TextField("Password", text: $password)
                                    } else {
                                        SecureField("Password", text: $password)
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
                            
                            // Error message
                            if let error = errorMessage {
                                Text(error)
                                    .font(.caption)
                                    .foregroundColor(.red)
                                    .padding(12)
                                    .background(Color.red.opacity(0.1))
                                    .cornerRadius(8)
                            }
                            
                            // Login button
                            Button(action: {
                                Task {
                                    isLoading = true
                                    errorMessage = nil
                                    do {
                                        try await authService.login(emailOrPhone: email, password: password)
                                        router.navigationPath.append(AppRouter.Destination.home)
                                    } catch {
                                        errorMessage = "Invalid email or password"
                                    }
                                    isLoading = false
                                }
                            }) {
                                if isLoading {
                                    HStack(spacing: 8) {
                                        ProgressView()
                                            .tint(.white)
                                        Text("Signing in...")
                                    }
                                } else {
                                    Text("Sign In")
                                        .fontWeight(.semibold)
                                }
                            }
                            .frame(maxWidth: .infinity)
                            .padding(12)
                            .background(Color(red: 0.9, green: 0.22, blue: 0.27))
                            .foregroundColor(.white)
                            .cornerRadius(8)
                            .disabled(email.isEmpty || password.isEmpty || isLoading)
                            .opacity(email.isEmpty || password.isEmpty ? 0.6 : 1.0)
                            .accessibilityLabel("Sign in")
                            
                            Divider()
                                .padding(.vertical, 8)
                            
                            // Biometric login
                            if isBiometricAvailable {
                                Button(action: {
                                    Task {
                                        await performBiometricLogin()
                                    }
                                }) {
                                    HStack(spacing: 8) {
                                        Image(systemName: "faceid")
                                        Text("Sign in with Face ID")
                                            .fontWeight(.semibold)
                                    }
                                }
                                .frame(maxWidth: .infinity)
                                .padding(12)
                                .background(Color.gray.opacity(0.1))
                                .foregroundColor(.black)
                                .cornerRadius(8)
                                .accessibilityLabel("Sign in with Face ID")
                            }
                            
                            // Forgot password & Sign up
                            HStack(spacing: 8) {
                                NavigationLink(value: AppRouter.Destination.forgotPassword) {
                                    Text("Forgot Password?")
                                        .font(.caption)
                                        .foregroundColor(Color(red: 0.9, green: 0.22, blue: 0.27))
                                }
                                
                                Spacer()
                                
                                NavigationLink(value: AppRouter.Destination.signup) {
                                    Text("Create Account")
                                        .font(.caption)
                                        .fontWeight(.semibold)
                                        .foregroundColor(Color(red: 0.9, green: 0.22, blue: 0.27))
                                }
                            }
                            
                            Spacer()
                        }
                        .padding(16)
                    }
                }
                
                .navigationDestination(for: AppRouter.Destination.self) { destination in
                    router.view(for: destination)
                }
            }
            .onAppear {
                checkBiometricAvailability()
            }
        }
    }
    
    private func checkBiometricAvailability() {
        let context = LAContext()
        var error: NSError?
        isBiometricAvailable = context.canEvaluatePolicy(.deviceOwnerAuthenticationWithBiometrics, error: &error)
    }
    
    private func performBiometricLogin() async {
        let context = LAContext()
        context.localizedFallbackTitle = "Use passcode"
        
        do {
            let success = try await context.evaluatePolicy(
                .deviceOwnerAuthenticationWithBiometrics,
                localizedReason: "Sign in to TheWatch"
            )
            
            if success {
                try await authService.biometricLogin()
                router.navigationPath.append(AppRouter.Destination.home)
            }
        } catch {
            errorMessage = "Biometric authentication failed"
        }
    }
}

#Preview {
    LoginView()
        .environment(MockAuthService())
}
