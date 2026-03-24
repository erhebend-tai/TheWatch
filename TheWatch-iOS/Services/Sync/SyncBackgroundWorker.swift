/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         SyncBackgroundWorker.swift                             │
 * │ Purpose:      BGProcessingTask handler for periodic background sync. │
 * │               Mirrors Android's SyncWorker.kt (WorkManager).         │
 * │               Schedules periodic background sync every 15 minutes    │
 * │               (subject to iOS system throttling).                     │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: BackgroundTasks framework, SyncEngine                  │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   // In AppDelegate.application(_:didFinishLaunchingWithOptions:):   │
 * │   SyncBackgroundWorker.registerBackgroundTask()                      │
 * │                                                                      │
 * │   // In SceneDelegate.sceneDidEnterBackground(_:):                   │
 * │   SyncBackgroundWorker.scheduleBackgroundSync()                      │
 * │                                                                      │
 * │ Info.plist requirement:                                              │
 * │   BGTaskSchedulerPermittedIdentifiers:                               │
 * │     - com.thewatch.sync.processing                                  │
 * │     - com.thewatch.sync.refresh                                     │
 * │                                                                      │
 * │ NOTE: iOS may delay or skip background tasks based on device state,  │
 * │ battery level, and usage patterns. The BGProcessingTask gets more    │
 * │ time (minutes) than BGAppRefreshTask (seconds). We register both:   │
 * │ - Processing: full queue flush, handles large backlogs                │
 * │ - Refresh: lightweight check for critical (SOS) tasks only           │
 * │                                                                      │
 * │ Testing: Use `e -l objc -- (void)[[BGTaskScheduler sharedScheduler]  │
 * │ _simulateLaunchForTaskWithIdentifier:                                │
 * │ @"com.thewatch.sync.processing"]` in lldb debugger.                  │
 * └──────────────────────────────────────────────────────────────────────┘
 */

import Foundation
import BackgroundTasks
import os.log

enum SyncBackgroundWorker {

    // MARK: - Task Identifiers

    static let processingTaskIdentifier = "com.thewatch.sync.processing"
    static let refreshTaskIdentifier = "com.thewatch.sync.refresh"

    private static let logger = Logger(subsystem: "com.thewatch.app", category: "SyncBGWorker")

    // MARK: - Registration

    /// Register background task handlers. Call once in application(_:didFinishLaunchingWithOptions:).
    static func registerBackgroundTask() {
        // Full processing task — gets several minutes of runtime
        BGTaskScheduler.shared.register(
            forTaskWithIdentifier: processingTaskIdentifier,
            using: nil
        ) { task in
            guard let processingTask = task as? BGProcessingTask else { return }
            handleProcessingTask(processingTask)
        }

        // App refresh task — lightweight, runs frequently but briefly
        BGTaskScheduler.shared.register(
            forTaskWithIdentifier: refreshTaskIdentifier,
            using: nil
        ) { task in
            guard let refreshTask = task as? BGAppRefreshTask else { return }
            handleRefreshTask(refreshTask)
        }

        logger.info("Background sync tasks registered")
    }

    // MARK: - Scheduling

    /// Schedule the next background sync. Call when app enters background.
    static func scheduleBackgroundSync() {
        // Processing task: full queue flush
        let processingRequest = BGProcessingTaskRequest(identifier: processingTaskIdentifier)
        processingRequest.requiresNetworkConnectivity = true
        processingRequest.requiresExternalPower = false
        processingRequest.earliestBeginDate = Date(timeIntervalSinceNow: 15 * 60) // 15 min

        do {
            try BGTaskScheduler.shared.submit(processingRequest)
            logger.info("Scheduled processing background sync")
        } catch {
            logger.error("Failed to schedule processing task: \(error.localizedDescription)")
        }

        // Refresh task: critical-only flush (SOS tasks)
        let refreshRequest = BGAppRefreshTaskRequest(identifier: refreshTaskIdentifier)
        refreshRequest.earliestBeginDate = Date(timeIntervalSinceNow: 5 * 60) // 5 min

        do {
            try BGTaskScheduler.shared.submit(refreshRequest)
            logger.info("Scheduled refresh background sync")
        } catch {
            logger.error("Failed to schedule refresh task: \(error.localizedDescription)")
        }
    }

    /// Cancel all scheduled background sync (e.g., on logout).
    static func cancelAll() {
        BGTaskScheduler.shared.cancel(taskRequestWithIdentifier: processingTaskIdentifier)
        BGTaskScheduler.shared.cancel(taskRequestWithIdentifier: refreshTaskIdentifier)
        logger.info("Background sync tasks cancelled")
    }

    // MARK: - Task Handlers

    /// Handle the processing task: full queue flush.
    private static func handleProcessingTask(_ task: BGProcessingTask) {
        logger.info("Processing task started")

        // Schedule the next one immediately
        scheduleBackgroundSync()

        let flushTask = Task {
            let result = await SyncEngine.shared.flush()
            logger.info("Processing flush complete: synced=\(result.success), failed=\(result.failed)")
            task.setTaskCompleted(success: result.failed == 0 || result.success > 0)
        }

        // Handle expiration: cancel the flush if iOS reclaims our time
        task.expirationHandler = {
            logger.warning("Processing task expired by system")
            flushTask.cancel()
            task.setTaskCompleted(success: false)
        }
    }

    /// Handle the refresh task: flush only critical (SOS) tasks.
    private static func handleRefreshTask(_ task: BGAppRefreshTask) {
        logger.info("Refresh task started")

        // Schedule the next one
        scheduleBackgroundSync()

        let flushTask = Task {
            // Only flush SOS events — they're critical and time-sensitive
            let result = await SyncEngine.shared.flushEntityType(.sosEvent)
            logger.info("Refresh flush complete: synced=\(result.success), failed=\(result.failed)")
            task.setTaskCompleted(success: true)
        }

        task.expirationHandler = {
            logger.warning("Refresh task expired by system")
            flushTask.cancel()
            task.setTaskCompleted(success: false)
        }
    }
}
