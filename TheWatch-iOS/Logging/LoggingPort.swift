import Foundation
import Combine

// MARK: - Logging Port Protocol

/// Port protocol for structured logging — the domain contract.
///
/// Three-tier implementations:
/// - **Mock**: In-memory array + os_log output. Always-on for development.
/// - **Native**: SwiftData persistence + periodic Firestore sync. Works offline.
/// - **Live**: Direct Firestore writes. Production only.
///
/// All implementations MUST be thread-safe (actor-isolated or using locks).
protocol LoggingPort: AnyObject, Sendable {

    /// Write a structured log entry.
    func write(_ entry: LogEntry) async

    /// Query recent log entries, newest first.
    /// - Parameters:
    ///   - limit: Maximum entries to return
    ///   - minLevel: Minimum severity filter (inclusive)
    ///   - sourceContext: Optional filter by source component
    func query(limit: Int, minLevel: LogLevel, sourceContext: String?) async -> [LogEntry]

    /// Query log entries by correlation ID (e.g. all logs for a specific SOS incident).
    func queryByCorrelation(_ correlationId: String) async -> [LogEntry]

    /// Publisher that emits log entries in real time. Used by on-device log viewer.
    var logPublisher: AnyPublisher<LogEntry, Never> { get }

    /// Flush any buffered entries to persistent storage / remote.
    func flush() async

    /// Delete entries older than the given date.
    func prune(olderThan date: Date) async
}

// MARK: - Log Sync Port Protocol

/// Port for syncing local logs to Firestore.
/// Separated from LoggingPort so mock logging doesn't need sync awareness.
protocol LogSyncPort: AnyObject, Sendable {

    /// Push unsynced local entries to Firestore. Returns count synced.
    func syncToFirestore() async -> Int

    /// Pull recent entries from Firestore (for cross-device view).
    func pullFromFirestore(limit: Int) async -> [LogEntry]

    /// Check connectivity and Firestore availability.
    func isSyncAvailable() async -> Bool
}
