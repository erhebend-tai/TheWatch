import Foundation
import SwiftData
import Combine
import os.log

/// Native logging adapter — Tier 2 (On-Device + Offline).
///
/// - Persists every log entry to SwiftData (local SQLite)
/// - Echoes Warning+ to os_log for crash diagnostics
/// - Emits to a Combine publisher for real-time observation
/// - Works fully offline — no network dependency
/// - Paired with `NativeLogSyncAdapter` for Firestore sync
@MainActor
final class NativeLoggingAdapter: LoggingPort, @unchecked Sendable {

    private let modelContainer: ModelContainer
    private let subject = PassthroughSubject<LogEntry, Never>()
    private let osLog = Logger(subsystem: "com.thewatch.app", category: "NativeLog")

    var logPublisher: AnyPublisher<LogEntry, Never> {
        subject.eraseToAnyPublisher()
    }

    init(modelContainer: ModelContainer) {
        self.modelContainer = modelContainer
    }

    func write(_ entry: LogEntry) async {
        let model = LogEntryModel.fromDomain(entry)
        let context = ModelContext(modelContainer)
        context.insert(model)
        try? context.save()

        // Echo Warning+ to os_log
        if entry.level >= .warning {
            let msg = "[\(entry.sourceContext)] \(entry.renderedMessage)"
            switch entry.level {
            case .warning: osLog.warning("\(msg)")
            case .error:   osLog.error("\(msg)")
            case .fatal:   osLog.critical("\(msg)")
            default: break
            }
        }

        subject.send(entry)
    }

    func query(limit: Int = 100, minLevel: LogLevel = .verbose, sourceContext: String? = nil) async -> [LogEntry] {
        let context = ModelContext(modelContainer)
        var descriptor = FetchDescriptor<LogEntryModel>(
            predicate: #Predicate { $0.level >= minLevel.rawValue },
            sortBy: [SortDescriptor(\.timestamp, order: .reverse)]
        )
        descriptor.fetchLimit = limit

        do {
            var results = try context.fetch(descriptor)
            if let source = sourceContext {
                results = results.filter { $0.sourceContext == source }
            }
            return results.map { $0.toDomain() }
        } catch {
            osLog.error("[NativeLogging] Query failed: \(error.localizedDescription)")
            return []
        }
    }

    func queryByCorrelation(_ correlationId: String) async -> [LogEntry] {
        let context = ModelContext(modelContainer)
        let descriptor = FetchDescriptor<LogEntryModel>(
            predicate: #Predicate { $0.correlationId == correlationId },
            sortBy: [SortDescriptor(\.timestamp)]
        )

        do {
            return try context.fetch(descriptor).map { $0.toDomain() }
        } catch {
            osLog.error("[NativeLogging] Correlation query failed: \(error.localizedDescription)")
            return []
        }
    }

    func flush() async {
        let context = ModelContext(modelContainer)
        let total = (try? context.fetchCount(FetchDescriptor<LogEntryModel>())) ?? 0
        let unsyncedDescriptor = FetchDescriptor<LogEntryModel>(
            predicate: #Predicate { $0.synced == false }
        )
        let unsynced = (try? context.fetchCount(unsyncedDescriptor)) ?? 0
        osLog.debug("[NativeLogging] Flush — \(total) total, \(unsynced) unsynced")
    }

    func prune(olderThan date: Date) async {
        let context = ModelContext(modelContainer)
        do {
            let descriptor = FetchDescriptor<LogEntryModel>(
                predicate: #Predicate { $0.timestamp < date }
            )
            let old = try context.fetch(descriptor)
            for entry in old {
                context.delete(entry)
            }
            try context.save()
            osLog.debug("[NativeLogging] Pruned \(old.count) entries")
        } catch {
            osLog.error("[NativeLogging] Prune failed: \(error.localizedDescription)")
        }
    }
}
