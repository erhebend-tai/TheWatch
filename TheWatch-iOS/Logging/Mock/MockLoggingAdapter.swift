import Foundation
import Combine
import os.log

/// Mock logging adapter — Tier 1 (Development).
///
/// - Stores all entries in an in-memory array (capped at 5000)
/// - Echoes every entry to os_log (Console.app)
/// - Emits to a Combine publisher for real-time observation
/// - Never touches disk or network
/// - First-class, permanent — not a "test stub"
final class MockLoggingAdapter: LoggingPort, @unchecked Sendable {

    private let lock = NSLock()
    private var entries: [LogEntry] = []
    private let subject = PassthroughSubject<LogEntry, Never>()
    private let osLog = Logger(subsystem: "com.thewatch.app", category: "MockLog")

    private let maxEntries = 5000

    var logPublisher: AnyPublisher<LogEntry, Never> {
        subject.eraseToAnyPublisher()
    }

    func write(_ entry: LogEntry) async {
        lock.lock()
        entries.insert(entry, at: 0)
        if entries.count > maxEntries {
            entries.removeLast(entries.count - maxEntries)
        }
        lock.unlock()

        // Echo to os_log
        let msg = "[\(entry.sourceContext)] \(entry.renderedMessage)"
        switch entry.level {
        case .verbose:     osLog.trace("\(msg)")
        case .debug:       osLog.debug("\(msg)")
        case .information: osLog.info("\(msg)")
        case .warning:     osLog.warning("\(msg)")
        case .error:       osLog.error("\(msg)")
        case .fatal:       osLog.critical("\(msg)")
        }
        if let ex = entry.exception {
            osLog.error("  Exception: \(ex)")
        }
        if let cid = entry.correlationId {
            osLog.debug("  CorrelationId: \(cid)")
        }

        subject.send(entry)
    }

    func query(limit: Int = 100, minLevel: LogLevel = .verbose, sourceContext: String? = nil) async -> [LogEntry] {
        lock.lock()
        defer { lock.unlock() }
        return entries
            .filter { $0.level >= minLevel }
            .filter { sourceContext == nil || $0.sourceContext == sourceContext }
            .prefix(limit)
            .map { $0 }
    }

    func queryByCorrelation(_ correlationId: String) async -> [LogEntry] {
        lock.lock()
        defer { lock.unlock() }
        return entries.filter { $0.correlationId == correlationId }
    }

    func flush() async {
        osLog.debug("[MockLogging] Flush called — \(self.entries.count) entries in buffer")
    }

    func prune(olderThan date: Date) async {
        lock.lock()
        let before = entries.count
        entries.removeAll { $0.timestamp < date }
        let after = entries.count
        lock.unlock()
        osLog.debug("[MockLogging] Pruned \(before - after) entries")
    }

    /// Expose all entries for testing / debug UI.
    func allEntries() -> [LogEntry] {
        lock.lock()
        defer { lock.unlock() }
        return entries
    }

    /// Clear all entries.
    func clear() {
        lock.lock()
        entries.removeAll()
        lock.unlock()
    }
}

/// Mock sync adapter — Tier 1 no-op.
final class MockLogSyncAdapter: LogSyncPort, @unchecked Sendable {

    private let osLog = Logger(subsystem: "com.thewatch.app", category: "MockSync")

    func syncToFirestore() async -> Int {
        osLog.info("[MockLogSync] syncToFirestore called — mock returns 0")
        return 0
    }

    func pullFromFirestore(limit: Int) async -> [LogEntry] {
        osLog.info("[MockLogSync] pullFromFirestore(\(limit)) — mock returns empty")
        return []
    }

    func isSyncAvailable() async -> Bool {
        osLog.debug("[MockLogSync] isSyncAvailable — mock returns false")
        return false
    }
}
