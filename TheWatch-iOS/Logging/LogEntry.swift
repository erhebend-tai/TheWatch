import Foundation

/// Structured log entry — the domain model for all logging in TheWatch iOS.
///
/// Mirrors Serilog's structured logging semantics:
/// - `messageTemplate` with `{PropertyName}` placeholders
/// - `properties` dictionary for structured data
/// - `sourceContext` identifies the originating component
/// - `correlationId` links related events across an SOS lifecycle
struct LogEntry: Identifiable, Codable, Sendable {
    let id: String
    let timestamp: Date
    let level: LogLevel
    let sourceContext: String
    let messageTemplate: String
    let properties: [String: String]
    let exception: String?
    let correlationId: String?
    let userId: String?
    let deviceId: String?
    var synced: Bool

    init(
        id: String = UUID().uuidString,
        timestamp: Date = Date(),
        level: LogLevel,
        sourceContext: String,
        messageTemplate: String,
        properties: [String: String] = [:],
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
        self.properties = properties
        self.exception = exception
        self.correlationId = correlationId
        self.userId = userId
        self.deviceId = deviceId
        self.synced = synced
    }

    /// Render the message template with property values substituted.
    var renderedMessage: String {
        var result = messageTemplate
        for (key, value) in properties {
            result = result.replacingOccurrences(of: "{\(key)}", with: value)
        }
        return result
    }
}
