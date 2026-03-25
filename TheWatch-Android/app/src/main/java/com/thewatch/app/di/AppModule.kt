package com.thewatch.app.di

import android.content.Context
import androidx.room.Room
import com.google.android.gms.location.FusedLocationProviderClient
import com.google.android.gms.location.LocationServices
import com.thewatch.app.data.local.AppDatabase
import com.thewatch.app.data.logging.LogSyncPort
import com.thewatch.app.data.logging.LoggingPort
import com.thewatch.app.data.logging.WatchLogger
import com.thewatch.app.data.logging.local.LogEntryDao
import com.thewatch.app.data.logging.mock.MockLogSyncAdapter
import com.thewatch.app.data.logging.mock.MockLoggingAdapter
import com.thewatch.app.data.repository.AlertRepository
import com.thewatch.app.data.repository.AuthRepository
import com.thewatch.app.data.repository.HistoryRepository
import com.thewatch.app.data.repository.LocationRepository
import com.thewatch.app.data.repository.LocationRepositoryImpl
import com.thewatch.app.data.repository.PhraseDetectionRepository
import com.thewatch.app.data.repository.PhraseDetectionRepositoryImpl
import com.thewatch.app.data.repository.UserRepository
import com.thewatch.app.data.repository.VolunteerRepository
import com.thewatch.app.data.api.WatchApiClient
import com.thewatch.app.data.repository.api.ApiAlertRepository
import com.thewatch.app.data.repository.api.ApiHistoryRepository
import com.thewatch.app.data.repository.api.ApiUserRepository
import com.thewatch.app.data.repository.api.ApiVolunteerRepository
import com.thewatch.app.data.repository.mock.MockAlertRepository
import com.thewatch.app.data.repository.mock.MockAuthRepository
import com.thewatch.app.data.repository.mock.MockHistoryRepository
import com.thewatch.app.data.repository.mock.MockUserRepository
import com.thewatch.app.data.repository.mock.MockVolunteerRepository
import com.thewatch.app.data.ble.BLEMeshPort
import com.thewatch.app.data.ble.mock.MockBLEMeshAdapter
import com.thewatch.app.data.emergency.ImplicitDetectionPort
import com.thewatch.app.data.emergency.mock.MockImplicitDetectionAdapter
import com.thewatch.app.data.health.HealthPort
import com.thewatch.app.data.health.mock.MockHealthAdapter
import com.thewatch.app.data.local.SyncLogDao
import com.thewatch.app.data.location.OfflineLocationQueuePort
import com.thewatch.app.data.location.mock.MockLocationQueueAdapter
import com.thewatch.app.data.sms.SMSFallbackPort
import com.thewatch.app.data.sms.mock.MockSMSFallbackAdapter
import com.thewatch.app.data.sync.ConnectivityMonitor
import com.thewatch.app.data.sync.LogSyncProvider
import com.thewatch.app.data.sync.MockSyncAdapter
import com.thewatch.app.data.sync.SyncDispatcher
import com.thewatch.app.data.sync.SyncEngine
import com.thewatch.app.data.sync.SyncPort
import com.thewatch.app.data.sync.SyncTaskDao
import com.thewatch.app.data.wearables.WearablePort
import com.thewatch.app.data.wearables.mock.MockWearableAdapter
import com.thewatch.app.data.adapters.AdapterRegistry
import com.thewatch.app.data.adapters.AdapterSlot
import com.thewatch.app.data.adapters.AdapterTier
import com.thewatch.app.data.adapters.SignalRAdapterSync
import com.thewatch.app.data.signalr.WatchHubConnection
import com.thewatch.app.data.signalr.HubEventSender as SignalRHubEventSender
import com.thewatch.app.data.signalr.HubMessageRouter as SignalRHubMessageRouter
import com.thewatch.app.data.signalr.SignalRTestRunnerBridge
import com.thewatch.app.data.sos.SosCorrelationManager
import com.thewatch.app.data.sos.SosTimelineBuilder
import com.thewatch.app.testing.HubEventSender
import com.thewatch.app.testing.HubMessageRouter
import com.thewatch.app.testing.MockHubEventSender
import com.thewatch.app.testing.MockHubMessageRouter
import com.thewatch.app.testing.TestRunnerService
import com.thewatch.app.testing.TestStepExecutorRegistry
import dagger.Binds
import dagger.Module
import dagger.Provides
import dagger.hilt.InstallIn
import dagger.hilt.android.qualifiers.ApplicationContext
import dagger.hilt.components.SingletonComponent
import javax.inject.Singleton

@Module
@InstallIn(SingletonComponent::class)
object AppModule {

    @Singleton
    @Provides
    fun provideAppDatabase(@ApplicationContext context: Context): AppDatabase {
        return Room.databaseBuilder(
            context,
            AppDatabase::class.java,
            "thewatch_database"
        ).fallbackToDestructiveMigration() // Dev only — production needs proper migration
        .build()
    }

    // ── Adapter Registry (backbone of three-tier architecture) ───
    // All adapter slots are resolved through the registry.
    // The MAUI dashboard can command tier switches at runtime via SignalR.
    @Singleton
    @Provides
    fun provideAdapterRegistry(logger: WatchLogger): AdapterRegistry = AdapterRegistry(logger)

    @Singleton
    @Provides
    fun provideSignalRAdapterSync(
        registry: AdapterRegistry,
        logger: WatchLogger
    ): SignalRAdapterSync = SignalRAdapterSync(registry, logger)

    // ── Logging (resolved through AdapterRegistry) ──────────────
    @Singleton
    @Provides
    fun provideLoggingPort(
        registry: AdapterRegistry,
        mock: MockLoggingAdapter
    ): LoggingPort {
        return when (registry.getTier(AdapterSlot.Logging)) {
            AdapterTier.Mock -> mock
            // AdapterTier.Native -> nativeLogging // Room-backed logger
            // AdapterTier.Live -> liveLogging     // Firestore-backed logger
            AdapterTier.Disabled -> mock           // Logging should never be fully disabled
            else -> mock
        }
    }

    @Singleton
    @Provides
    fun provideLogSyncPort(
        registry: AdapterRegistry,
        mock: MockLogSyncAdapter
    ): LogSyncPort {
        return when (registry.getTier(AdapterSlot.LogSync)) {
            AdapterTier.Mock -> mock
            // AdapterTier.Native -> nativeLogSync
            // AdapterTier.Live -> firestoreLogSync
            AdapterTier.Disabled -> mock
            else -> mock
        }
    }

    @Singleton
    @Provides
    fun provideLogEntryDao(db: AppDatabase): LogEntryDao = db.logEntryDao()

    @Singleton
    @Provides
    fun provideWatchLogger(port: LoggingPort): WatchLogger = WatchLogger(port)

    // ── SOS Correlation ─────────────────────────────────────────
    @Singleton
    @Provides
    fun provideSosCorrelationManager(logger: WatchLogger): SosCorrelationManager =
        SosCorrelationManager(logger)

    @Singleton
    @Provides
    fun provideSosTimelineBuilder(loggingPort: LoggingPort): SosTimelineBuilder =
        SosTimelineBuilder(loggingPort)

    // ── Repositories (resolved through AdapterRegistry) ─────────
    @Singleton
    @Provides
    fun provideAuthRepository(
        registry: AdapterRegistry,
        @ApplicationContext context: Context
    ): AuthRepository {
        return when (registry.getTier(AdapterSlot.Security)) {
            AdapterTier.Mock -> MockAuthRepository()
            // AdapterTier.Native -> NativeAuthRepository(context)  // Keystore-backed
            AdapterTier.Live -> com.thewatch.app.data.repository.firebase.FirebaseAuthRepository(context)
            else -> MockAuthRepository()
        }
    }

    // ── WatchApiClient (HTTP client for Dashboard.Api) ──────────
    @Singleton
    @Provides
    fun provideWatchApiClient(): WatchApiClient = WatchApiClient()

    @Singleton
    @Provides
    fun provideAlertRepository(
        registry: AdapterRegistry,
        apiClient: WatchApiClient,
        hubConnection: WatchHubConnection
    ): AlertRepository {
        return when (registry.getTier(AdapterSlot.SOS)) {
            AdapterTier.Mock -> MockAlertRepository()
            // AdapterTier.Native -> NativeAlertRepository()
            AdapterTier.Live -> ApiAlertRepository(apiClient, hubConnection)
            else -> MockAlertRepository()
        }
    }

    @Singleton
    @Provides
    fun provideUserRepository(registry: AdapterRegistry, apiClient: WatchApiClient): UserRepository {
        return when (registry.getTier(AdapterSlot.Contacts)) {
            AdapterTier.Mock -> MockUserRepository()
            // AdapterTier.Native -> NativeUserRepository()
            AdapterTier.Live -> ApiUserRepository(apiClient)
            else -> MockUserRepository()
        }
    }

    @Singleton
    @Provides
    fun provideHistoryRepository(registry: AdapterRegistry, apiClient: WatchApiClient): HistoryRepository {
        return when (registry.getTier(AdapterSlot.Evidence)) {
            AdapterTier.Mock -> MockHistoryRepository()
            // AdapterTier.Native -> RoomHistoryRepository()
            AdapterTier.Live -> ApiHistoryRepository(apiClient)
            else -> MockHistoryRepository()
        }
    }

    @Singleton
    @Provides
    fun provideVolunteerRepository(registry: AdapterRegistry, apiClient: WatchApiClient): VolunteerRepository {
        return when (registry.getTier(AdapterSlot.Contacts)) {
            AdapterTier.Mock -> MockVolunteerRepository()
            // AdapterTier.Native -> NativeVolunteerRepository()
            AdapterTier.Live -> ApiVolunteerRepository(apiClient)
            else -> MockVolunteerRepository()
        }
    }

    @Singleton
    @Provides
    fun provideFusedLocationProviderClient(@ApplicationContext context: Context): FusedLocationProviderClient {
        return LocationServices.getFusedLocationProviderClient(context)
    }

    @Singleton
    @Provides
    fun provideLocationRepository(
        impl: LocationRepositoryImpl
    ): LocationRepository = impl

    @Singleton
    @Provides
    fun providePhraseDetectionRepository(
        impl: PhraseDetectionRepositoryImpl
    ): PhraseDetectionRepository = impl

    // ── SyncLog DAO (legacy) ─────────────────────────────────────────
    @Singleton
    @Provides
    fun provideSyncLogDao(db: AppDatabase): SyncLogDao = db.syncLogDao()

    // ── Sync Engine (generalized offline-first sync) ─────────────────
    @Singleton
    @Provides
    fun provideSyncTaskDao(db: AppDatabase): SyncTaskDao = db.syncTaskDao()

    @Singleton
    @Provides
    fun provideSyncDispatcher(): SyncDispatcher = SyncDispatcher()

    @Singleton
    @Provides
    fun provideConnectivityMonitor(@ApplicationContext context: Context): ConnectivityMonitor =
        ConnectivityMonitor(context)

    @Singleton
    @Provides
    fun provideSyncEngine(
        @ApplicationContext context: Context,
        syncTaskDao: SyncTaskDao,
        syncDispatcher: SyncDispatcher,
        connectivityMonitor: ConnectivityMonitor,
        logger: WatchLogger,
        logSyncProvider: LogSyncProvider
    ): SyncEngine {
        val engine = SyncEngine(context, syncTaskDao, syncDispatcher, connectivityMonitor, logger)
        // Register the legacy logging sync as a provider
        engine.registerProvider(logSyncProvider)
        return engine
    }

    @Singleton
    @Provides
    fun provideLogSyncProvider(
        syncLogDao: SyncLogDao,
        logEntryDao: LogEntryDao
    ): LogSyncProvider = LogSyncProvider(syncLogDao, logEntryDao)

    // ── Sync Port (resolved through AdapterRegistry) ───────────────
    @Singleton
    @Provides
    fun provideSyncPort(registry: AdapterRegistry, mock: MockSyncAdapter): SyncPort {
        return when (registry.getTier(AdapterSlot.CloudMessaging)) {
            AdapterTier.Mock -> mock
            // AdapterTier.Native -> NativeSyncAdapter()
            // AdapterTier.Live -> FirestoreSyncAdapter()
            else -> mock
        }
    }

    // ── SMS Fallback (resolved through AdapterRegistry) ──────────────
    @Singleton
    @Provides
    fun provideSMSFallbackPort(registry: AdapterRegistry, mock: MockSMSFallbackAdapter): SMSFallbackPort {
        return when (registry.getTier(AdapterSlot.Telephony)) {
            AdapterTier.Mock -> mock
            // AdapterTier.Native -> NativeSMSFallbackAdapter()   // SmsManager
            // AdapterTier.Live -> TwilioSMSFallbackAdapter()     // Twilio API
            else -> mock
        }
    }

    // ── BLE Mesh (resolved through AdapterRegistry) ──────────────────
    @Singleton
    @Provides
    fun provideBLEMeshPort(registry: AdapterRegistry, mock: MockBLEMeshAdapter): BLEMeshPort {
        return when (registry.getTier(AdapterSlot.BLE)) {
            AdapterTier.Mock -> mock
            // AdapterTier.Native -> NativeBLEMeshAdapter()   // Android BLE stack
            // AdapterTier.Live -> mock                       // BLE is always on-device
            else -> mock
        }
    }

    // ── Health Connect (resolved through AdapterRegistry) ────────────
    @Singleton
    @Provides
    fun provideHealthPort(registry: AdapterRegistry, mock: MockHealthAdapter): HealthPort {
        return when (registry.getTier(AdapterSlot.Health)) {
            AdapterTier.Mock -> mock
            // AdapterTier.Native -> HealthConnectAdapter()   // Health Connect API
            // AdapterTier.Live -> mock                       // Health data stays on-device
            else -> mock
        }
    }

    // ── Wearable Management (resolved through AdapterRegistry) ───────
    @Singleton
    @Provides
    fun provideWearablePort(registry: AdapterRegistry, mock: MockWearableAdapter): WearablePort {
        return when (registry.getTier(AdapterSlot.BLE)) {
            AdapterTier.Mock -> mock
            // AdapterTier.Native -> NativeWearableAdapter()
            // AdapterTier.Live -> mock
            else -> mock
        }
    }

    // ── Implicit Emergency Detection (resolved through AdapterRegistry) ──
    @Singleton
    @Provides
    fun provideImplicitDetectionPort(registry: AdapterRegistry, mock: MockImplicitDetectionAdapter): ImplicitDetectionPort {
        return when (registry.getTier(AdapterSlot.SOS)) {
            AdapterTier.Mock -> mock
            // AdapterTier.Native -> NativeImplicitDetectionAdapter()  // Accelerometer + ML
            // AdapterTier.Live -> mock                                 // Detection is always on-device
            else -> mock
        }
    }

    // ── Offline Location Queue (resolved through AdapterRegistry) ────
    @Singleton
    @Provides
    fun provideOfflineLocationQueuePort(registry: AdapterRegistry, mock: MockLocationQueueAdapter): OfflineLocationQueuePort {
        return when (registry.getTier(AdapterSlot.Location)) {
            AdapterTier.Mock -> mock
            // AdapterTier.Native -> RoomLocationQueueAdapter()   // Room-backed queue
            // AdapterTier.Live -> FirestoreLocationQueueAdapter()
            else -> mock
        }
    }

    // ── SignalR Hub Connection (Agent 4) ────────────────────────────
    // Central hub connection singleton. Auto-connects on app foreground,
    // disconnects on background. Joins device_* and user_* groups.
    @Singleton
    @Provides
    fun provideWatchHubConnection(logger: WatchLogger): WatchHubConnection =
        WatchHubConnection(logger)

    @Singleton
    @Provides
    fun provideSignalRHubEventSender(
        hubConnection: WatchHubConnection,
        logger: WatchLogger
    ): SignalRHubEventSender = SignalRHubEventSender(hubConnection, logger)

    @Singleton
    @Provides
    fun provideSignalRHubMessageRouter(
        hubConnection: WatchHubConnection,
        adapterSync: SignalRAdapterSync,
        eventSender: SignalRHubEventSender,
        logger: WatchLogger
    ): SignalRHubMessageRouter = SignalRHubMessageRouter(hubConnection, adapterSync, eventSender, logger)

    // ── SignalR ↔ Test Runner Bridge (Agent 4 + Agent 5) ─────────
    // Bridges the real SignalR client to the test runner's port interfaces.
    // When SignalR is connected, test steps flow through the hub.
    // Falls back to mock if SignalR is not available.
    @Singleton
    @Provides
    fun provideSignalRTestRunnerBridge(
        router: SignalRHubMessageRouter,
        sender: SignalRHubEventSender,
        logger: WatchLogger
    ): SignalRTestRunnerBridge = SignalRTestRunnerBridge(router, sender, logger)

    // ── Test Runner (Agent 5) ────────────────────────────────────
    // Receives test steps from MAUI dashboard via SignalR (through bridge)
    // or via mock router for local development.
    @Singleton
    @Provides
    fun provideHubMessageRouter(
        bridge: SignalRTestRunnerBridge,
        mock: MockHubMessageRouter
    ): HubMessageRouter {
        // Use the bridge when SignalR is available, mock for development
        // TODO: Switch based on AdapterRegistry tier for CloudMessaging
        return bridge
    }

    @Singleton
    @Provides
    fun provideHubEventSender(
        bridge: SignalRTestRunnerBridge,
        mock: MockHubEventSender
    ): HubEventSender {
        // Use the bridge when SignalR is available, mock for development
        return bridge
    }

    @Singleton
    @Provides
    fun provideTestRunnerService(
        executorRegistry: TestStepExecutorRegistry,
        logger: WatchLogger
    ): TestRunnerService = TestRunnerService(executorRegistry, logger)
}
