import Foundation
import os.log
import UIKit

/// Structured logger facade — the primary API all components use.
///
/// Mirrors Serilog's API. Each log call is dispatched to the port asynchronously,
/// so callers never block on persistence.
///
/// Usage:
/// ```swift
/// WatchLogger.shared.information(
///     source: "LocationCoordinator",
///     template: "Escalated to {Mode} mode",
///     properties: ["Mode": "Emergency"],
///     correlationId: activeAlertId
/// )
/// ```
@Observable
final class WatchLogger: @unchecked Sendable {

    static let shared = WatchLogger()

    /// The active logging port — set during app configuration.
    /// Defaults to MockLoggingAdapter for safety.
    var port: (any LoggingPort)?

    /// Current user ID — set on login, cleared on logout.
    var userId: String?

    /// Stable device ID — set once at app startup.
    let deviceId: String = UIDevice.current.identifierForVendor?.uuidString ?? UUID().uuidString

    private let osLog = Logger(subsystem: "com.thewatch.app", category: "Structured")

    private init() {}

    // MARK: - Convenience Methods

    func verbose(source: String, template: String, properties: [String: String] = [:], correlationId: String? = nil) {
        log(level: .verbose, source: source, template: template, properties: properties, correlationId: correlationId)
    }

    func debug(source: String, template: String, properties: [String: String] = [:], correlationId: String? = nil) {
        log(level: .debug, source: source, template: template, properties: properties, correlationId: correlationId)
    }

    func information(source: String, template: String, properties: [String: String] = [:], correlationId: String? = nil) {
        log(level: .information, source: source, template: template, properties: properties, correlationId: correlationId)
    }

    func warning(source: String, template: String, properties: [String: String] = [:], error: Error? = nil, correlationId: String? = nil) {
        log(level: .warning, source: source, template: template, properties: properties, error: error, correlationId: correlationId)
    }

    func error(source: String, template: String, properties: [String: String] = [:], error: Error? = nil, correlationId: String? = nil) {
        log(level: .error, source: source, template: template, properties: properties, error: error, correlationId: correlationId)
    }

    func fatal(source: String, template: String, properties: [String: String] = [:], error: Error? = nil, correlationId: String? = nil) {
        log(level: .fatal, source: source, template: template, properties: properties, error: error, correlationId: correlationId)
    }

    // MARK: - Core Write

    private func log(
        level: LogLevel,
        source: String,
        template: String,
        properties: [String: String],
        error: Error? = nil,
        correlationId: String? = nil
    ) {
        var enriched = properties
        enriched["Platform"] = "iOS"
        enriched["OsVersion"] = UIDevice.current.systemVersion
        enriched["AppVersion"] = Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "dev"

        let entry = LogEntry(
            level: level,
            sourceContext: source,
            messageTemplate: template,
            properties: enriched,
            exception: error.map { "\(type(of: $0)): \($0.localizedDescription)" },
            correlationId: correlationId,
            userId: userId,
            deviceId: deviceId
        )

        // Fire-and-forget to background
        Task.detached(priority: .utility) { [weak self] in
            await self?.port?.write(entry)
        }

        // Also echo to os_log for Console.app / Xcode
        let rendered = entry.renderedMessage
        switch level {
        case .verbose:     osLog.trace("[\(source)] \(rendered)")
        case .debug:       osLog.debug("[\(source)] \(rendered)")
        case .information: osLog.info("[\(source)] \(rendered)")
        case .warning:     osLog.warning("[\(source)] \(rendered)")
        case .error:       osLog.error("[\(source)] \(rendered)")
        case .fatal:       osLog.critical("[\(source)] \(rendered)")
        }
    }

    /// Flush buffered logs — call from scenePhase .background transition.
    func flush() {
        Task.detached(priority: .utility) { [weak self] in
            await self?.port?.flush()
        }
    }
}
