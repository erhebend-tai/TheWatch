/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         SyncEngine.swift                                       │
 * │ Purpose:      Singleton coordinator for all offline-first sync       │
 * │               operations on iOS. Manages a priority queue persisted  │
 * │               via SyncTaskStore, dispatches through SyncDispatcher,  │
 * │               and handles task coalescing for duplicate updates.     │
 * │               Mirrors Android's SyncEngine.kt.                       │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: SyncTaskStore, SyncDispatcher, SyncConnectivityMonitor│
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   // Enqueue from anywhere:                                          │
 * │   await SyncEngine.shared.enqueue(                                   │
 * │       entityType: .sosEvent,                                         │
 * │       entityId: "sos-abc",                                           │
 * │       action: .create,                                               │
 * │       payload: sosJSON,                                              │
 * │       priority: .critical                                            │
 * │   )                                                                  │
 * │                                                                      │
 * │   // Engine persists to disk immediately (survives process death).   │
 * │   // If online, triggers immediate flush.                            │
 * │   // If offline, ConnectivityMonitor triggers flush on reconnect.    │
 * │                                                                      │
 * │ Provider registration:                                               │
 * │   SyncEngine.shared.registerProvider(logSyncProvider)                │
 * │   // Existing logging sync can plug in without breaking.             │
 * │                                                                      │
 * │ NOTE: The engine does NOT hold tasks in memory — SyncTaskStore       │
 * │ (file-backed actor) is the single source of truth.                   │
 * └──────────────────────────────────────────────────────────────────────┘
 */

import Foundation
import Combine
import os.log

/// Protocol for sync providers that register with the engine.
/// Mirrors Android's SyncProvider interface.
protocol SyncProviderProtocol {
    var entityType: SyncEntityType { get }

    /// Called before dispatch. Return true to let engine proceed, false if provider handled it.
    func onBeforeDispatch(_ task: SyncTask) async -> Bool

    /// Called after successful dispatch for post-sync bookkeeping.
    func onAfterDispatch(_ task: SyncTask, serverId: String?) async
}

@Observable
final class SyncEngine {

    // MARK: - Singleton

    static let shared = SyncEngine()

    // MARK: - Dependencies

    let store = SyncTaskStore()
    let dispatcher = SyncDispatcher()
    let connectivityMonitor = SyncConnectivityMonitor()

    // MARK: - Config

    private let maxRetries = 5
    private let baseBackoffSeconds: TimeInterval = 30
    private let maxBackoffSeconds: TimeInterval = 900
    private let batchSize = 50
    private let pruneCompletedHours: TimeInterval = 24 * 3600
    private let pruneDeadLetterDays: TimeInterval = 7 * 24 * 3600

    // MARK: - State

    private(set) var pendingCount: Int = 0
    private var providers: [SyncEntityType: SyncProviderProtocol] = [:]
    private var cancellables = Set<AnyCancellable>()
    private let logger = Logger(subsystem: "com.thewatch.app", category: "SyncEngine")

    // MARK: - Init

    private init() {
        // Wire connectivity monitor to trigger flush on reconnect
        connectivityMonitor.onReconnect = { [weak self] in
            Task {
                await self?.flush()
            }
        }

        // Observe pending count
        store.pendingCountPublisher
            .receive(on: DispatchQueue.main)
            .sink { [weak self] count in
                self?.pendingCount = count
            }
            .store(in: &cancellables)
    }

    // MARK: - Provider Registration

    func registerProvider(_ provider: SyncProviderProtocol) {
        providers[provider.entityType] = provider
        logger.info("Registered sync provider for \(provider.entityType.rawValue)")
    }

    // MARK: - Enqueue

    /// Enqueue a sync task. Persists immediately. Triggers expedited flush for high-priority tasks.
    @discardableResult
    func enqueue(
        entityType: SyncEntityType,
        entityId: String,
        action: SyncTaskAction,
        payload: String,
        priority: SyncPriority = .normal,
        userId: String = "",
        idempotencyKey: String? = nil
    ) async -> String {

        // Coalesce: if UPDATE for same entity is already queued, replace payload
        if action == .update {
            if let existing = await store.findPendingForEntity(entityType: entityType, entityId: entityId),
               existing.action == .update {
                var updated = existing
                updated.payload = payload
                updated.priority = min(existing.priority, priority)
                updated.createdAt = Date()
                try? await store.update(updated)
                logger.debug("Coalesced UPDATE for \(entityType.rawValue)/\(entityId)")
                triggerFlushIfNeeded(priority: priority)
                return existing.id
            }
        }

        let task = SyncTask(
            entityType: entityType,
            entityId: entityId,
            action: action,
            payload: payload,
            priority: priority,
            userId: userId,
            idempotencyKey: idempotencyKey
        )

        try? await store.insert(task)
        logger.info("Enqueued \(action.rawValue) for \(entityType.rawValue)/\(entityId) (priority=\(priority.rawValue))")

        triggerFlushIfNeeded(priority: priority)
        return task.id
    }

    /// Enqueue a batch of tasks.
    func enqueueBatch(_ tasks: [SyncTask]) async {
        try? await store.insertAll(tasks)
        logger.info("Enqueued batch of \(tasks.count) tasks")
        if let highestPriority = tasks.min(by: { $0.priority < $1.priority })?.priority {
            triggerFlushIfNeeded(priority: highestPriority)
        }
    }

    // MARK: - Flush

    /// Main flush cycle. Called by SyncWorker (BGProcessingTask) and on connectivity restored.
    @discardableResult
    func flush() async -> (success: Int, failed: Int) {
        logger.info("flush() started")

        // 1. Recover stuck tasks
        try? await store.resetStuckTasks()

        // 2. Check backend
        guard await dispatcher.isBackendReachable() else {
            logger.warning("Backend not reachable — skipping flush")
            return (0, 0)
        }

        // 3. Get pending tasks
        let tasks = await store.getPendingByPriority(maxRetries: maxRetries)
        guard !tasks.isEmpty else {
            logger.info("No pending tasks")
            return (0, 0)
        }

        logger.info("Processing \(tasks.count) pending tasks")

        var successCount = 0
        var failCount = 0

        // 4. Process in batches
        for batch in tasks.chunked(into: batchSize) {
            for task in batch {
                // Per-task exponential backoff
                if shouldDefer(task) {
                    logger.debug("Deferring task \(task.id) (retry \(task.retryCount))")
                    continue
                }

                try? await store.markInProgress(task.id)

                // Provider pre-dispatch hook
                let provider = providers[task.entityType]
                let shouldContinue = await (provider?.onBeforeDispatch(task) ?? true)

                if !shouldContinue {
                    try? await store.markCompleted(task.id)
                    successCount += 1
                    continue
                }

                // Dispatch
                let result = await dispatcher.dispatch(task)

                switch result {
                case .success(let serverId):
                    try? await store.markCompleted(task.id)
                    successCount += 1
                    await provider?.onAfterDispatch(task, serverId: serverId)
                    logger.debug("Synced \(task.entityType.rawValue)/\(task.entityId) -> \(serverId ?? "nil")")

                case .retryableFailure(let message, _):
                    let newRetryCount = task.retryCount + 1
                    if newRetryCount >= task.maxRetries {
                        try? await store.markDeadLetter(task.id)
                        logger.error("Task \(task.id) moved to dead-letter: \(message)")
                    } else {
                        try? await store.markFailed(task.id, error: message, retryCount: newRetryCount)
                        logger.warning("Retryable failure for \(task.id): \(message)")
                    }
                    failCount += 1

                    if dispatcher.isCircuitOpen() {
                        logger.warning("Circuit breaker open — stopping flush")
                        return (successCount, failCount)
                    }

                case .permanentFailure(let message, _):
                    try? await store.markDeadLetter(task.id)
                    failCount += 1
                    logger.error("Permanent failure for \(task.id): \(message)")
                }
            }
        }

        // 5. Prune old tasks
        let completedCutoff = Date().addingTimeInterval(-pruneCompletedHours)
        try? await store.pruneCompleted(olderThan: completedCutoff)

        let deadLetterCutoff = Date().addingTimeInterval(-pruneDeadLetterDays)
        try? await store.pruneDeadLetter(olderThan: deadLetterCutoff)

        logger.info("flush() complete — synced=\(successCount), failed=\(failCount)")
        return (successCount, failCount)
    }

    /// Flush only tasks of a specific entity type.
    @discardableResult
    func flushEntityType(_ entityType: SyncEntityType) async -> (success: Int, failed: Int) {
        try? await store.resetStuckTasks()
        let tasks = await store.getPendingByEntityType(entityType, maxRetries: maxRetries)
        guard !tasks.isEmpty else { return (0, 0) }

        var success = 0
        var fail = 0

        for task in tasks {
            try? await store.markInProgress(task.id)
            let result = await dispatcher.dispatch(task)

            switch result {
            case .success(let serverId):
                try? await store.markCompleted(task.id)
                success += 1
                await providers[task.entityType]?.onAfterDispatch(task, serverId: serverId)
            case .retryableFailure(let msg, _):
                let newRetry = task.retryCount + 1
                if newRetry >= task.maxRetries {
                    try? await store.markDeadLetter(task.id)
                } else {
                    try? await store.markFailed(task.id, error: msg, retryCount: newRetry)
                }
                fail += 1
            case .permanentFailure:
                try? await store.markDeadLetter(task.id)
                fail += 1
            }
        }

        return (success, fail)
    }

    /// Re-queue a dead-letter task.
    func retryDeadLetter(_ taskId: String) async {
        try? await store.requeue(taskId)
        logger.info("Re-queued dead-letter task \(taskId)")
        triggerFlushIfNeeded(priority: .high)
    }

    /// Clear all tasks (on logout).
    func clearAll() async {
        try? await store.clearAll()
        logger.info("All sync tasks cleared")
    }

    // MARK: - Start / Stop

    func start() {
        connectivityMonitor.start()
        SyncBackgroundWorker.registerBackgroundTask()
        SyncBackgroundWorker.scheduleBackgroundSync()
        logger.info("SyncEngine started")
    }

    func stop() {
        connectivityMonitor.stop()
        logger.info("SyncEngine stopped")
    }

    // MARK: - Private

    private func triggerFlushIfNeeded(priority: SyncPriority) {
        if priority.rawValue <= SyncPriority.high.rawValue && connectivityMonitor.isConnected {
            Task { await flush() }
        }
    }

    private func shouldDefer(_ task: SyncTask) -> Bool {
        guard task.retryCount > 0, let lastAttempt = task.lastAttemptAt else { return false }

        let backoff = min(
            baseBackoffSeconds * pow(2.0, Double(task.retryCount)),
            maxBackoffSeconds
        )
        let nextAllowed = lastAttempt.addingTimeInterval(backoff)
        return Date() < nextAllowed
    }
}

// MARK: - Array Chunking

private extension Array {
    func chunked(into size: Int) -> [[Element]] {
        stride(from: 0, to: count, by: size).map {
            Array(self[$0 ..< Swift.min($0 + size, count)])
        }
    }
}
