import Foundation
import SwiftData
import FirebaseFirestore
import os.log

/// Native Firestore sync adapter — pushes local SwiftData logs to Firestore
/// and pulls cross-device entries.
///
/// Firestore collection structure:
/// ```
/// thewatch-logs/
///   {deviceId}/
///     entries/
///       {logEntryId} → { timestamp, level, sourceContext, ... }
/// ```
///
/// Called from BGProcessingTask and app foregrounding.
@MainActor
final class NativeLogSyncAdapter: LogSyncPort, @unchecked Sendable {

    private let modelContainer: ModelContainer
    private let firestore: Firestore
    private let osLog = Logger(subsystem: "com.thewatch.app", category: "LogSync")

    private let collection = "thewatch-logs"
    private let subcollection = "entries"
    private let batchSize = 500

    init(modelContainer: ModelContainer, firestore: Firestore = Firestore.firestore()) {
        self.modelContainer = modelContainer
        self.firestore = firestore
    }

    func syncToFirestore() async -> Int {
        let context = ModelContext(modelContainer)

        do {
            // Fetch unsynced entries
            var descriptor = FetchDescriptor<LogEntryModel>(
                predicate: #Predicate { $0.synced == false },
                sortBy: [SortDescriptor(\.timestamp)]
            )
            descriptor.fetchLimit = batchSize
            let unsynced = try context.fetch(descriptor)
            guard !unsynced.isEmpty else { return 0 }

            // Group by device
            let byDevice = Dictionary(grouping: unsynced) { $0.deviceId ?? "unknown" }

            var totalSynced = 0
            for (deviceId, batch) in byDevice {
                let firestoreBatch = firestore.batch()

                for model in batch {
                    let docRef = firestore
                        .collection(collection)
                        .document(deviceId)
                        .collection(subcollection)
                        .document(model.id)

                    firestoreBatch.setData(model.toFirestoreMap(), forDocument: docRef)
                }

                try await firestoreBatch.commit()

                // Mark as synced locally
                for model in batch {
                    model.synced = true
                }
                try context.save()
                totalSynced += batch.count
            }

            osLog.info("[LogSync] Synced \(totalSynced) entries to Firestore")
            return totalSynced
        } catch {
            osLog.error("[LogSync] Firestore sync failed: \(error.localizedDescription)")
            return 0
        }
    }

    func pullFromFirestore(limit: Int = 200) async -> [LogEntry] {
        do {
            let snapshot = try await firestore
                .collectionGroup(subcollection)
                .order(by: "timestamp", descending: true)
                .limit(to: limit)
                .getDocuments()

            return snapshot.documents.compactMap { doc -> LogEntry? in
                guard let timestamp = doc.data()["timestamp"] as? Double,
                      let levelRaw = doc.data()["level"] as? Int,
                      let sourceContext = doc.data()["sourceContext"] as? String else {
                    return nil
                }

                let propsRaw = doc.data()["properties"] as? [String: Any] ?? [:]
                let properties = propsRaw.mapValues { String(describing: $0) }

                return LogEntry(
                    id: doc.documentID,
                    timestamp: Date(timeIntervalSince1970: timestamp / 1000),
                    level: LogLevel(rawValue: levelRaw) ?? .information,
                    sourceContext: sourceContext,
                    messageTemplate: doc.data()["messageTemplate"] as? String ?? "",
                    properties: properties,
                    exception: doc.data()["exception"] as? String,
                    correlationId: doc.data()["correlationId"] as? String,
                    userId: doc.data()["userId"] as? String,
                    deviceId: doc.data()["deviceId"] as? String,
                    synced: true
                )
            }
        } catch {
            osLog.error("[LogSync] Firestore pull failed: \(error.localizedDescription)")
            return []
        }
    }

    func isSyncAvailable() async -> Bool {
        do {
            _ = try await firestore.collection(collection).limit(to: 1).getDocuments()
            return true
        } catch {
            return false
        }
    }
}

// MARK: - Firestore Serialization

extension LogEntryModel {
    func toFirestoreMap() -> [String: Any] {
        var map: [String: Any] = [
            "timestamp": timestamp.timeIntervalSince1970 * 1000,
            "level": level,
            "sourceContext": sourceContext,
            "messageTemplate": messageTemplate,
            "renderedMessage": renderedMessage,
            "properties": Self.parseProperties(propertiesJson)
        ]
        if let exception { map["exception"] = exception }
        if let correlationId { map["correlationId"] = correlationId }
        if let userId { map["userId"] = userId }
        if let deviceId { map["deviceId"] = deviceId }
        return map
    }
}
