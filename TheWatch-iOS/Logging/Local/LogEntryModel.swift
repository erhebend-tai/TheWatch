import SwiftData
import Foundation

/// SwiftData model for persisting structured log entries locally.
///
/// Indexed on timestamp, correlationId, synced, and level
/// for efficient queries matching the LoggingPort contract.
@Model
final class LogEntryModel {
    @Attribute(.unique) var id: String
    var timestamp: Date
    var level: Int // LogLevel.rawValue
    var sourceContext: String
    var messageTemplate: String
    var renderedMessage: String

    /// JSON-serialized properties dictionary
    var propertiesJson: String

    var exception: String?
    var correlationId: String?
    var userId: String?
    var deviceId: String?
    var synced: Bool

    init(
        id: String,
        timestamp: Date,
        level: Int,
        sourceContext: String,
        messageTemplate: String,
        renderedMessage: String,
        propertiesJson: String,
        exception: String? = nil,
        correlationId: String? = nil,
        userId: String? = nil,
        deviceId: String? = nil,
        synced: Bool = false
    ) {
        self.id = id
        self.timestamp = timestamp
        self.level = level
        self.sourceContext = sourceContext
        self.messageTemplate = messageTemplate
        self.renderedMessage = renderedMessage
        self.propertiesJson = propertiesJson
        self.exception = exception
        self.correlationId = correlationId
        self.userId = userId
        self.deviceId = deviceId
        self.synced = synced
    }

    // MARK: - Domain Conversion

    func toDomain() -> LogEntry {
        LogEntry(
            id: id,
            timestamp: timestamp,
            level: LogLevel(rawValue: level) ?? .information,
            sourceContext: sourceContext,
            messageTemplate: messageTemplate,
            properties: Self.parseProperties(propertiesJson),
            exception: exception,
            correlationId: correlationId,
            userId: userId,
            deviceId: deviceId,
            synced: synced
        )
    }

    static func fromDomain(_ entry: LogEntry) -> LogEntryModel {
        LogEntryModel(
            id: entry.id,
            timestamp: entry.timestamp,
            level: entry.level.rawValue,
            sourceContext: entry.sourceContext,
            messageTemplate: entry.messageTemplate,
            renderedMessage: entry.renderedMessage,
            propertiesJson: serializeProperties(entry.properties),
            exception: entry.exception,
            correlationId: entry.correlationId,
            userId: entry.userId,
            deviceId: entry.deviceId,
            synced: entry.synced
        )
    }

    // MARK: - JSON helpers

    private static func serializeProperties(_ props: [String: String]) -> String {
        guard !props.isEmpty else { return "{}" }
        guard let data = try? JSONSerialization.data(withJSONObject: props),
              let json = String(data: data, encoding: .utf8) else {
            return "{}"
        }
        return json
    }

    static func parseProperties(_ json: String) -> [String: String] {
        guard json != "{}", !json.isEmpty,
              let data = json.data(using: .utf8),
              let dict = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            return [:]
        }
        return dict.mapValues { String(describing: $0) }
    }
}
