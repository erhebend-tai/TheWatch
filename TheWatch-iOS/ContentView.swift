import SwiftUI

struct ContentView: View {
    @Environment(MockAuthService.self) var authService
    @State var appRouter = AppRouter()
    @State private var isCheckingAuth = true

    var body: some View {
        ZStack {
            if isCheckingAuth {
                SplashScreenView()
            } else {
                NavigationStack(path: $appRouter.navigationPath) {
                    Group {
                        if authService.isAuthenticated {
                            HomeView()
                                .navigationDestination(for: AppRouter.Destination.self) { destination in
                                    appRouter.view(for: destination)
                                }
                        } else {
                            LoginView()
                                .navigationDestination(for: AppRouter.Destination.self) { destination in
                                    appRouter.view(for: destination)
                                }
                        }
                    }
                }
                .preferredColorScheme(.light)
            }
        }
        .onAppear {
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.5) {
                isCheckingAuth = false
            }
        }
    }
}

struct SplashScreenView: View {
    var body: some View {
        ZStack {
            Color(red: 0.0, green: 0.125, blue: 0.3)
                .ignoresSafeArea()

            VStack(spacing: 20) {
                Image(systemName: "shield.fill")
                    .font(.system(size: 64))
                    .foregroundColor(.white)

                Text("TheWatch")
                    .font(.system(size: 32, weight: .bold, design: .default))
                    .foregroundColor(.white)

                Text("Life-Safety Response")
                    .font(.caption)
                    .foregroundColor(.white.opacity(0.8))
            }
        }
    }
}

#Preview {
    ContentView()
        .environment(MockAuthService())
}
