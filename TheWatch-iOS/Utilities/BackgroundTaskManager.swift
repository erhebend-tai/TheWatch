import Foundation
import BackgroundTasks
import UIKit

// MARK: - Background Task Identifiers
enum BackgroundTaskIdentifier {
    static let locationSync = "com.thewatch.location-sync"
    static let offlineFlush = "com.thewatch.offline-flush"
}

// MARK: - Background Task Manager
@Observable
final class BackgroundTaskManager {
    // MARK: - Properties
    private var locationSyncTaskRequest: BGAppRefreshTaskRequest?
    private var offlineFlushTaskRequest: BGProcessingTaskRequest?
    private var isConfigured = false

    static let shared = BackgroundTaskManager()

    // MARK: - Initialization
    init() {
        registerBackgroundTasks()
    }

    // MARK: - Register Background Tasks
    /// Register background tasks with the system
    /// This should be called at app launch (in app delegate or scene delegate)
    func registerBackgroundTasks() {
        print("[BackgroundTaskManager] Registering background tasks...")

        // Register location sync task (15-minute refresh)
        registerLocationSyncTask()

        // Register offline queue flush task (on-demand processing)
        registerOfflineFlushTask()

        isConfigured = true
        print("[BackgroundTaskManager] Background tasks registered")
    }

    // MARK: - Location Sync Task
    private func registerLocationSyncTask() {
        BGTaskScheduler.shared.register(
            forTaskWithIdentifier: BackgroundTaskIdentifier.locationSync,
            using: nil
        ) { [weak self] task in
            self?.handleLocationSyncTask(task as! BGAppRefreshTask)
        }

        print("[BackgroundTaskManager] Location sync task registered")
    }

    func scheduleLocationSyncTask(minimumInterval: TimeInterval = 15 * 60) {
        let request = BGAppRefreshTaskRequest(identifier: BackgroundTaskIdentifier.locationSync)
        request.earliestBeginDate = Date(timeIntervalSinceNow: minimumInterval)

        do {
            try BGTaskScheduler.shared.submit(request)
            print("[BackgroundTaskManager] Location sync task scheduled (interval: \(minimumInterval/60)min)")
        } catch {
            print("[BackgroundTaskManager] Failed to schedule location sync: \(error)")
        }
    }

    private func handleLocationSyncTask(_ task: BGAppRefreshTask) {
        print("[BackgroundTaskManager] Executing location sync task...")

        // Schedule the next refresh immediately
        scheduleLocationSyncTask(minimumInterval: 15 * 60)

        // Create a background task to ensure completion
        var backgroundTaskID: UIBackgroundTaskIdentifier = .invalid
        backgroundTaskID = UIApplication.shared.beginBackgroundTask { [weak self] in
            UIApplication.shared.endBackgroundTask(backgroundTaskID)
            task.setTaskComplete(success: false)
            self?.scheduleLocationSyncTask(minimumInterval: 15 * 60)
        }

        // Perform the location sync
        Task {
            do {
                // Sync location data here
                // Example: await locationSyncService.syncLocation()
                print("[BackgroundTaskManager] Location sync completed successfully")

                task.setTaskComplete(success: true)

                // Schedule next task
                self.scheduleLocationSyncTask(minimumInterval: 15 * 60)
            } catch {
                print("[BackgroundTaskManager] Location sync failed: \(error)")
                task.setTaskComplete(success: false)

                // Reschedule sooner if failed
                self.scheduleLocationSyncTask(minimumInterval: 5 * 60)
            }

            UIApplication.shared.endBackgroundTask(backgroundTaskID)
        }

        // Provide expiration handler
        task.expirationHandler = {
            print("[BackgroundTaskManager] Location sync task expired")
            task.setTaskComplete(success: false)

            // Reschedule
            self.scheduleLocationSyncTask(minimumInterval: 15 * 60)
        }
    }

    // MARK: - Offline Queue Flush Task
    private func registerOfflineFlushTask() {
        BGTaskScheduler.shared.register(
            forTaskWithIdentifier: BackgroundTaskIdentifier.offlineFlush,
            using: nil
        ) { [weak self] task in
            self?.handleOfflineFlushTask(task as! BGProcessingTask)
        }

        print("[BackgroundTaskManager] Offline flush task registered")
    }

    func scheduleOfflineFlushTask() {
        let request = BGProcessingTaskRequest(identifier: BackgroundTaskIdentifier.offlineFlush)
        request.requiresNetworkConnectivity = true
        request.requiresExternalPower = false

        do {
            try BGTaskScheduler.shared.submit(request)
            print("[BackgroundTaskManager] Offline flush task scheduled")
        } catch {
            print("[BackgroundTaskManager] Failed to schedule offline flush: \(error)")
        }
    }

    private func handleOfflineFlushTask(_ task: BGProcessingTask) {
        print("[BackgroundTaskManager] Executing offline flush task...")

        var backgroundTaskID: UIBackgroundTaskIdentifier = .invalid
        backgroundTaskID = UIApplication.shared.beginBackgroundTask { [weak self] in
            UIApplication.shared.endBackgroundTask(backgroundTaskID)
            task.setTaskComplete(success: false)
            self?.scheduleOfflineFlushTask()
        }

        // Perform the offline queue flush
        Task {
            do {
                // Flush offline data here
                // Example: await offlineQueueService.flushQueue()
                print("[BackgroundTaskManager] Offline flush completed successfully")

                task.setTaskComplete(success: true)

                // Reschedule for future use
                self.scheduleOfflineFlushTask()
            } catch {
                print("[BackgroundTaskManager] Offline flush failed: \(error)")
                task.setTaskComplete(success: false)

                // Reschedule
                self.scheduleOfflineFlushTask()
            }

            UIApplication.shared.endBackgroundTask(backgroundTaskID)
        }

        // Provide expiration handler
        task.expirationHandler = {
            print("[BackgroundTaskManager] Offline flush task expired")
            task.setTaskComplete(success: false)

            // Reschedule
            self.scheduleOfflineFlushTask()
        }
    }

    // MARK: - Application Lifecycle Integration
    /// Call this when app enters background
    func applicationDidEnterBackground() {
        print("[BackgroundTaskManager] App entered background")

        // Schedule location sync if not already scheduled
        scheduleLocationSyncTask(minimumInterval: 15 * 60)

        // Schedule offline flush if needed
        scheduleOfflineFlushTask()
    }

    /// Call this when app becomes active
    func applicationDidBecomeActive() {
        print("[BackgroundTaskManager] App became active")

        // Cancel any pending background tasks if needed
        // BGTaskScheduler will handle this automatically when app is in foreground
    }

    // MARK: - Task Management
    /// Cancel all scheduled background tasks
    func cancelAllTasks() {
        BGTaskScheduler.shared.cancel(taskRequestWithIdentifier: BackgroundTaskIdentifier.locationSync)
        BGTaskScheduler.shared.cancel(taskRequestWithIdentifier: BackgroundTaskIdentifier.offlineFlush)

        print("[BackgroundTaskManager] All background tasks cancelled")
    }

    /// Get the status of background tasks
    func getTaskStatus() -> String {
        return """
        [BackgroundTaskManager Status]
        Configured: \(isConfigured)
        Location Sync: Registered
        Offline Flush: Registered
        """
    }
}
