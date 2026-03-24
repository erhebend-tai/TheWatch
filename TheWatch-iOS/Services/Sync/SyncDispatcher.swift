/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         SyncDispatcher.swift                                   │
 * │ Purpose:      Maps SyncEntityType to Firestore collection and        │
 * │               dispatches sync tasks. Handles circuit breaker for     │
 * │               backend failure detection. Mirrors Android             │
 * │               SyncDispatcher.kt.                                     │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: Foundation                                             │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   let dispatcher = SyncDispatcher()                                  │
 * │   let result = await dispatcher.dispatch(task)                       │
 * │   switch result {                                                    │
 * │   case .success(let serverId): // mark completed                     │
 * │   case .retryableFailure(let msg, _): // retry later                 │
 * │   case .permanentFailure(let msg, _): // dead letter                 │
 * │   }                                                                  │
 * │                                                                      │
 * │ This is the Mock (Tier 1) implementation. Native (Tier 2) replaces   │
 * │ dispatch() with real Firebase Firestore SDK calls:                   │
 * │   Firestore.firestore()                                              │
 * │       .collection(task.entityType.collectionName)                    │
 * │       .document(task.entityId)                                       │
 * │       .setData(payload, merge: true)                                 │
 * │                                                                      │
 * │ NOTE: Circuit breaker opens after 3 consecutive failures and stays   │
 * │ open for 60 seconds. During this time all dispatches return          │
 * │ retryableFailure immediately without network calls.                  │
 * └──────────────────────────────────────────────────────────────────────┘
 */

import Foundation
import os.log

/// Protocol for sync dispatch (enables Mock/Native/Live tier switching).
protocol SyncDispatchProtocol {
    func dispatch(_ task: SyncTask) async -> SyncDispatchResult
    func dispatchBatch(_ tasks: [SyncTask]) async -> [String: SyncDispatchResult]
    func isBackendReachable() async -> Bool
    func isCircuitOpen() -> Bool
}

/// Mock (Tier 1) dispatcher. Simulates Firestore writes.
final class SyncDispatcher: SyncDispatchProtocol {

    private let logger = Logger(subsystem: "com.thewatch.app", category: "SyncDispatch")

    // Circuit breaker
    private let circuitFailureThreshold = 3
    private let circuitOpenDurationSeconds: TimeInterval = 60
    private var consecutiveFailures = 0
    private var circuitOpenedAt: Date?

    // Test simulation
    var simulateFailure = false
    var simulateBackendDown = false

    func dispatch(_ task: SyncTask) async -> SyncDispatchResult {
        // Circuit breaker check
        if isCircuitOpen() {
            return .retryableFailure(message: "Circuit breaker open — backend assumed down", error: nil)
        }

        let collection = task.entityType.collectionName
        logger.debug("dispatch(\(task.id)) -> \(collection)/\(task.entityId) [\(task.action.rawValue)]")

        // Simulated network delay
        try? await Task.sleep(nanoseconds: 150_000_000) // 150ms

        if simulateFailure {
            consecutiveFailures += 1
            if consecutiveFailures >= circuitFailureThreshold {
                circuitOpenedAt = Date()
                logger.warning("Circuit breaker OPEN after \(self.consecutiveFailures) consecutive failures")
            }
            return .retryableFailure(message: "Mock: simulated dispatch failure", error: nil)
        }

        consecutiveFailures = 0
        logger.info("dispatch(\(task.id)) -> success (mock, collection=\(collection))")
        return .success(serverId: "mock-\(collection)-\(task.entityId)")
    }

    func dispatchBatch(_ tasks: [SyncTask]) async -> [String: SyncDispatchResult] {
        var results: [String: SyncDispatchResult] = [:]
        for task in tasks {
            results[task.id] = await dispatch(task)
        }
        return results
    }

    func isBackendReachable() async -> Bool {
        return !simulateBackendDown && !isCircuitOpen()
    }

    func isCircuitOpen() -> Bool {
        guard let openedAt = circuitOpenedAt else { return false }
        let elapsed = Date().timeIntervalSince(openedAt)
        if elapsed > circuitOpenDurationSeconds {
            // Half-open -> closed
            circuitOpenedAt = nil
            consecutiveFailures = 0
            logger.info("Circuit breaker HALF-OPEN -> CLOSED (cooldown elapsed)")
            return false
        }
        return true
    }

    func resetCircuitBreaker() {
        consecutiveFailures = 0
        circuitOpenedAt = nil
        logger.info("Circuit breaker manually reset")
    }
}
