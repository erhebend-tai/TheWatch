/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         SyncTaskStore.swift                                    │
 * │ Purpose:      Persistence layer for the sync task queue. Mirrors     │
 * │               Android's SyncTaskDao (Room DAO). Uses file-based      │
 * │               JSON persistence for crash resilience. Production      │
 * │               should migrate to Core Data or SwiftData.              │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: Foundation                                             │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   let store = SyncTaskStore()                                        │
 * │   try await store.insert(task)                                       │
 * │   let pending = try await store.getPendingByPriority()               │
 * │   try await store.markCompleted(taskId)                              │
 * │                                                                      │
 * │ Thread safety: All mutations go through an actor to serialize access.│
 * │                                                                      │
 * │ NOTE: File-based JSON persistence is adequate for dev/testing with   │
 * │ queue sizes under ~10K tasks. For production with high-frequency     │
 * │ location updates, migrate to Core Data with WAL journaling or        │
 * │ SwiftData @Model (iOS 17+). SQLite via GRDB is another good option. │
 * └──────────────────────────────────────────────────────────────────────┘
 */

import Foundation
import Combine

/// Actor-based persistence for sync tasks. Serializes all reads/writes.
actor SyncTaskStore {

    // MARK: - Storage

    private var tasks: [String: SyncTask] = [:]
    private let fileURL: URL
    private let pendingCountSubject = PassthroughSubject<Int, Never>()

    /// Publisher for pending task count (for UI badge).
    nonisolated var pendingCountPublisher: AnyPublisher<Int, Never> {
        pendingCountSubject.eraseToAnyPublisher()
    }

    init() {
        let appSupport = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first!
        let syncDir = appSupport.appendingPathComponent("TheWatch/Sync", isDirectory: true)
        try? FileManager.default.createDirectory(at: syncDir, withIntermediateDirectories: true)
        self.fileURL = syncDir.appendingPathComponent("sync_tasks.json")

        // Load persisted tasks
        if let data = try? Data(contentsOf: fileURL),
           let decoded = try? JSONDecoder().decode([String: SyncTask].self, from: data) {
            self.tasks = decoded
        }
    }

    // MARK: - Insert / Update / Delete

    func insert(_ task: SyncTask) throws {
        tasks[task.id] = task
        try persist()
        emitPendingCount()
    }

    func insertAll(_ newTasks: [SyncTask]) throws {
        for task in newTasks {
            tasks[task.id] = task
        }
        try persist()
        emitPendingCount()
    }

    func update(_ task: SyncTask) throws {
        tasks[task.id] = task
        try persist()
        emitPendingCount()
    }

    func delete(_ taskId: String) throws {
        tasks.removeValue(forKey: taskId)
        try persist()
        emitPendingCount()
    }

    // MARK: - Queue Reads

    /// Fetch pending/failed tasks ordered by priority then createdAt.
    func getPendingByPriority(maxRetries: Int = 5) -> [SyncTask] {
        tasks.values
            .filter { ($0.status == .queued || $0.status == .failed) && $0.retryCount < maxRetries }
            .sorted { lhs, rhs in
                if lhs.priority != rhs.priority { return lhs.priority < rhs.priority }
                return lhs.createdAt < rhs.createdAt
            }
    }

    /// Fetch pending tasks for a specific entity type.
    func getPendingByEntityType(_ entityType: SyncEntityType, maxRetries: Int = 5) -> [SyncTask] {
        tasks.values
            .filter {
                ($0.status == .queued || $0.status == .failed)
                && $0.entityType == entityType
                && $0.retryCount < maxRetries
            }
            .sorted { lhs, rhs in
                if lhs.priority != rhs.priority { return lhs.priority < rhs.priority }
                return lhs.createdAt < rhs.createdAt
            }
    }

    /// Find a pending task for a specific entity (for coalescing).
    func findPendingForEntity(entityType: SyncEntityType, entityId: String) -> SyncTask? {
        tasks.values.first {
            $0.entityType == entityType
            && $0.entityId == entityId
            && ($0.status == .queued || $0.status == .failed)
        }
    }

    /// Get dead-letter tasks for diagnostics.
    func getDeadLetterTasks() -> [SyncTask] {
        tasks.values
            .filter { $0.status == .deadLetter }
            .sorted { $0.createdAt > $1.createdAt }
    }

    // MARK: - Status Transitions

    func markInProgress(_ taskId: String) throws {
        guard var task = tasks[taskId] else { return }
        task.status = .inProgress
        task.lastAttemptAt = Date()
        tasks[taskId] = task
        try persist()
    }

    func markCompleted(_ taskId: String) throws {
        guard var task = tasks[taskId] else { return }
        task.status = .completed
        task.lastAttemptAt = Date()
        tasks[taskId] = task
        try persist()
        emitPendingCount()
    }

    func markFailed(_ taskId: String, error: String?, retryCount: Int) throws {
        guard var task = tasks[taskId] else { return }
        task.status = .failed
        task.lastError = error
        task.retryCount = retryCount
        task.lastAttemptAt = Date()
        tasks[taskId] = task
        try persist()
        emitPendingCount()
    }

    func markDeadLetter(_ taskId: String) throws {
        guard var task = tasks[taskId] else { return }
        task.status = .deadLetter
        task.lastAttemptAt = Date()
        tasks[taskId] = task
        try persist()
        emitPendingCount()
    }

    func resetStuckTasks() throws {
        var changed = false
        for (id, task) in tasks where task.status == .inProgress {
            var updated = task
            updated.status = .queued
            tasks[id] = updated
            changed = true
        }
        if changed { try persist() }
    }

    func requeue(_ taskId: String) throws {
        guard var task = tasks[taskId], task.status == .deadLetter else { return }
        task.status = .queued
        task.retryCount = 0
        task.lastError = nil
        tasks[taskId] = task
        try persist()
        emitPendingCount()
    }

    // MARK: - Counts

    func getPendingCount() -> Int {
        tasks.values.filter { $0.status == .queued || $0.status == .failed }.count
    }

    func getDeadLetterCount() -> Int {
        tasks.values.filter { $0.status == .deadLetter }.count
    }

    // MARK: - Cleanup

    func pruneCompleted(olderThan cutoff: Date) throws {
        let toRemove = tasks.values.filter { $0.status == .completed && ($0.lastAttemptAt ?? $0.createdAt) < cutoff }
        for task in toRemove { tasks.removeValue(forKey: task.id) }
        if !toRemove.isEmpty { try persist() }
    }

    func pruneDeadLetter(olderThan cutoff: Date) throws {
        let toRemove = tasks.values.filter { $0.status == .deadLetter && $0.createdAt < cutoff }
        for task in toRemove { tasks.removeValue(forKey: task.id) }
        if !toRemove.isEmpty { try persist() }
    }

    func clearAll() throws {
        tasks.removeAll()
        try persist()
        emitPendingCount()
    }

    // MARK: - Private

    private func persist() throws {
        let data = try JSONEncoder().encode(tasks)
        try data.write(to: fileURL, options: .atomic)
    }

    private func emitPendingCount() {
        let count = getPendingCount()
        pendingCountSubject.send(count)
    }
}
