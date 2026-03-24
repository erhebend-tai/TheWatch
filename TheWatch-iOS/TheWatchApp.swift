import SwiftUI
import SwiftData
import Combine

@main
struct TheWatchApp: App {
    @State private var authService = MockAuthService()
    @State private var alertService = MockAlertService()
    @State private var volunteerService = MockVolunteerService()
    @State private var networkMonitor = NetworkMonitor.shared
    @State private var locationManager = LocationManager.shared
    @State private var permissionManager = PermissionManager.shared

    private let backgroundTaskManager = BackgroundTaskManager.shared
    private let phraseDetectionCoordinator = PhraseDetectionCoordinator.shared
    private let locationCoordinator = LocationCoordinator.shared
    private let quickTapCoordinator = QuickTapCoordinator.shared
    private let notificationService = NotificationService.shared
    private let logger = WatchLogger.shared
    private let adapterRegistry = AdapterRegistry.shared
    private let signalRSync = SignalRAdapterSync.shared
    private let watchHub = WatchHubConnection.shared
    private let hubRouter = HubMessageRouter.shared
    private let hubSender = HubEventSender.shared
    private let testRunnerBridge = SignalRTestRunnerBridge.shared
    private let testRunner = TestRunnerService.shared
    let persistenceController = PersistenceController.shared

    @Environment(\.scenePhase) private var scenePhase

    init() {
        // ── Configure logging through AdapterRegistry ────────
        // The registry determines which tier is active for each slot.
        // When the MAUI dashboard switches Logging from Mock→Native via SignalR,
        // the logger.port is hot-swapped via the tier change listener below.
        //
        // Resolve logging adapter based on current registry tier:
        // switch adapterRegistry.getTier(.logging) {
        // case .mock:     logger.port = MockLoggingAdapter()
        // case .native:   logger.port = NativeLoggingAdapter(modelContainer: persistenceController.modelContainer)
        // case .live:     logger.port = FirestoreLoggingAdapter()
        // case .disabled: logger.port = MockLoggingAdapter() // Logging should never be fully disabled
        // }
        let mockLogging = MockLoggingAdapter()
        logger.port = mockLogging

        // Log initial tier assignments for WAL audit trail
        let tiers = adapterRegistry.toSerializableMap()
        let mockCount = tiers.values.filter { $0 == "Mock" }.count

        logger.information(
            source: "TheWatchApp",
            template: "Application launched on device {DeviceId}. AdapterRegistry initialized: {MockCount}/{SlotCount} slots on Mock.",
            properties: [
                "DeviceId": logger.deviceId,
                "SlotCount": "\(AdapterSlot.allCases.count)",
                "MockCount": "\(mockCount)"
            ]
        )

        // ── Initialize Test Runner via SignalR Bridge ──────
        // The bridge connects Agent 4's SignalR client to Agent 5's test runner.
        // Test steps from the MAUI dashboard flow through:
        //   DashboardHub → SignalR → HubMessageRouter → SignalRTestRunnerBridge → TestRunnerService
        // Results flow back through:
        //   TestRunnerService → SignalRTestRunnerBridge → HubEventSender → SignalR → DashboardHub
        let testContext = MockTestExecutionContext.create()
        testRunner.initialize(router: testRunnerBridge, sender: testRunnerBridge, context: testContext)
    }

    var body: some Scene {
        WindowGroup {
            ZStack {
                LoginView()
                    .environment(adapterRegistry)
                    .environment(authService)
                    .environment(alertService)
                    .environment(volunteerService)
                    .environment(permissionManager)
                    .modelContainer(persistenceController.modelContainer)

                if !networkMonitor.isOnline {
                    VStack {
                        OfflineBanner(isOnline: networkMonitor.isOnline)
                        Spacer()
                    }
                    .ignoresSafeArea()
                }
            }
        }
        .onChange(of: scenePhase) { _, newPhase in
            switch newPhase {
            case .background:
                backgroundTaskManager.applicationDidEnterBackground()
                // Flush logs before backgrounding
                logger.flush()
                // Disconnect SignalR to save battery/bandwidth in background
                Task { await watchHub.disconnect() }
                // Phrase detection continues in background via audio background mode
            case .active:
                backgroundTaskManager.applicationDidBecomeActive()
                permissionManager.refreshAllStatuses()
                // Restart phrase detection if user had it enabled
                phraseDetectionCoordinator.restartIfEnabled()
                // Configure push notifications on first active scene
                notificationService.configure()
                // Auto-connect SignalR on app foreground
                Task {
                    await watchHub.connect(
                        dashboardBaseUrl: "https://localhost:5001",
                        deviceId: logger.deviceId
                    )
                }
                logger.information(
                    source: "TheWatchApp",
                    template: "App became active"
                )
            case .inactive:
                break
            @unknown default:
                break
            }
        }
    }
}
