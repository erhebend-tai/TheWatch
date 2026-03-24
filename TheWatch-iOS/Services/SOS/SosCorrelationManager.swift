import Foundation
import Combine

// ── Write-Ahead Log ──────────────────────────────────────────────
// WAL: SosCorrelationManager generates a single UUID at SOS trigger time.
// That correlationId threads through every WatchLogger call in the SOS lifecycle:
//   phrase detection → confirmation → alert dispatch → volunteer notification → resolution
// MobileLogController on the Aspire dashboard can filter by correlationId to reconstruct
// a complete SOS timeline across devices.
//
// Example usage:
// ```swift
// let corrId = await SosCorrelationManager.shared.beginCorrelation(method: .phrase)
//
// // Every log in the pipeline:
// WatchLogger.shared.information(source: "AlertService",
//     template: "Alert dispatched to {Count} volunteers",
//     properties: ["Count": "5"],
//     correlationId: await SosCorrelationManager.shared.currentId)
//
// // Scoped block:
// await SosCorrelationManager.shared.withCorrelation { corrId in
//     WatchLogger.shared.information(source: "VolunteerDispatch",
//         template: "Volunteer {Id} acknowledged",
//         properties: ["Id": volId], correlationId: corrId)
// }
//
// // On resolution:
// await SosCorrelationManager.shared.endCorrelation(reason: .userResolved)
// ```

/// Trigger method that initiated the SOS lifecycle.
/// Stored as a property in the first log entry for audit trail.
enum SosTriggerMethod: String, Codable, Sendable {
    /// Voice phrase detection via speech-to-text pipeline
    case phrase = "Phrase"
    /// Rapid tap pattern on hardware button / screen
    case quickTap = "QuickTap"
    /// Manual SOS button press with countdown confirmation
    case manual = "Manual"
    /// Implicit detection: fall, crash, elevated HR, etc.
    case implicitDetection = "ImplicitDetection"
    /// Silent/duress SOS — no UI confirmation shown
    case silentDuress = "SilentDuress"
    /// External trigger via wearable BLE beacon
    case wearableTrigger = "WearableTrigger"
}

/// Why the SOS correlation ended — logged as the final entry.
enum SosResolutionReason: String, Codable, Sendable {
    /// User explicitly marked themselves safe
    case userResolved = "UserResolved"
    /// Volunteer confirmed user is safe via check-in
    case volunteerConfirmed = "VolunteerConfirmed"
    /// First responders took over — authority handoff
    case firstResponderHandoff = "FirstResponderHandoff"
    /// User cancelled during confirmation countdown
    case userCancelled = "UserCancelled"
    /// Auto-cleared after timeout (default 30 min)
    case timeout = "Timeout"
    /// System override — admin or dashboard forced close
    case systemOverride = "SystemOverride"
}

/// Actor that manages SOS lifecycle correlation IDs.
///
/// Generates a single UUID at SOS trigger time and makes it available to every
/// component in the pipeline. Auto-clears on resolution or configurable timeout.
///
/// Actor-isolated for thread safety — all state mutations are serialized.
///
/// The MAUI dashboard's MobileLogController queries by correlationId to reconstruct
/// the full SOS timeline: trigger → confirmation → alert → volunteer dispatch → resolution.
actor SosCorrelationManager {

    static let shared = SosCorrelationManager()

    /// Timeout interval after which an unresolved SOS auto-clears. Default: 30 minutes.
    var timeoutInterval: TimeInterval = 30 * 60

    // MARK: - Published State

    /// Combine publisher for observing correlation ID changes from SwiftUI.
    nonisolated let correlationIdPublisher = CurrentValueSubject<String?, Never>(nil)
    nonisolated let triggerMethodPublisher = CurrentValueSubject<SosTriggerMethod?, Never>(nil)

    // MARK: - Internal State

    private var _currentCorrelationId: String?
    private var _triggerMethod: SosTriggerMethod?
    private var _triggerTimestamp: Date?
    private var timeoutTask: Task<Void, Never>?

    /// The active SOS correlation ID, or nil if no SOS is in progress.
    var currentId: String? { _currentCorrelationId }

    /// Whether an SOS lifecycle is currently active.
    var isActive: Bool { _currentCorrelationId != nil }

    /// The method that triggered the current SOS.
    var currentTriggerMethod: SosTriggerMethod? { _triggerMethod }

    /// When the current SOS was triggered.
    var currentTriggerTimestamp: Date? { _triggerTimestamp }

    private let logger = WatchLogger.shared

    private init() {}

    // MARK: - Lifecycle Control

    /// Begin a new SOS correlation. Generates a fresh UUID, logs the trigger event,
    /// and starts the auto-timeout watchdog.
    ///
    /// If a correlation is already active, the previous one is force-ended with
    /// `.systemOverride` before starting the new one.
    ///
    /// - Parameter method: How the SOS was triggered (phrase, quick tap, manual, etc.)
    /// - Returns: The new correlationId
    @discardableResult
    func beginCorrelation(method: SosTriggerMethod) -> String {
        // If there's an existing active SOS, close it first
        if isActive {
            endCorrelation(reason: .systemOverride)
        }

        let correlationId = UUID().uuidString
        let now = Date()

        _currentCorrelationId = correlationId
        _triggerMethod = method
        _triggerTimestamp = now

        // Publish to Combine observers
        correlationIdPublisher.send(correlationId)
        triggerMethodPublisher.send(method)

        logger.information(
            source: "SosCorrelationManager",
            template: "SOS triggered by {Method}. CorrelationId: {CorrelationId}",
            properties: [
                "Method": method.rawValue,
                "CorrelationId": correlationId,
                "Stage": "TRIGGER"
            ],
            correlationId: correlationId
        )

        // Start timeout watchdog
        startTimeoutWatchdog(correlationId: correlationId)

        return correlationId
    }

    /// End the active SOS correlation with a reason.
    /// Logs the resolution event and clears state.
    ///
    /// No-op if no correlation is active.
    ///
    /// - Parameter reason: Why the SOS lifecycle ended
    func endCorrelation(reason: SosResolutionReason) {
        guard let corrId = _currentCorrelationId else { return }
        let method = _triggerMethod
        let triggerTime = _triggerTimestamp
        let durationMs = triggerTime.map { Int(Date().timeIntervalSince($0) * 1000) }

        var props: [String: String] = [
            "Reason": reason.rawValue,
            "Stage": "RESOLUTION"
        ]
        if let method { props["Method"] = method.rawValue }
        if let durationMs { props["DurationMs"] = "\(durationMs)" }

        logger.information(
            source: "SosCorrelationManager",
            template: "SOS resolved. Reason: {Reason}, Duration: {DurationMs}ms, TriggerMethod: {Method}",
            properties: props,
            correlationId: corrId
        )

        // Clear state
        timeoutTask?.cancel()
        timeoutTask = nil
        _currentCorrelationId = nil
        _triggerMethod = nil
        _triggerTimestamp = nil

        correlationIdPublisher.send(nil)
        triggerMethodPublisher.send(nil)
    }

    // MARK: - Scoped Correlation

    /// Execute a closure with the current correlationId.
    /// If no SOS is active, the closure receives nil.
    ///
    /// ```swift
    /// await sosCorrelation.withCorrelation { corrId in
    ///     WatchLogger.shared.information(source: "VolunteerDispatch",
    ///         template: "Dispatched {Count} volunteers",
    ///         properties: ["Count": "5"], correlationId: corrId)
    /// }
    /// ```
    func withCorrelation<T>(_ block: (String?) -> T) -> T {
        block(_currentCorrelationId)
    }

    /// Execute a closure only if an SOS is active. Returns nil if no SOS in progress.
    func withActiveCorrelation<T>(_ block: (String) -> T) -> T? {
        guard let corrId = _currentCorrelationId else { return nil }
        return block(corrId)
    }

    /// Log a pipeline stage event with the current correlationId.
    /// Convenience method that enriches properties with the Stage tag.
    ///
    /// Stages in order:
    ///   TRIGGER → CONFIRMATION → ALERT_DISPATCH → VOLUNTEER_NOTIFY →
    ///   VOLUNTEER_ACKNOWLEDGE → CHECK_IN → ESCALATION → RESOLUTION
    func logStage(
        _ stage: String,
        source: String,
        template: String,
        properties: [String: String] = [:]
    ) {
        guard let corrId = _currentCorrelationId else { return }
        var enriched = properties
        enriched["Stage"] = stage

        logger.information(
            source: source,
            template: template,
            properties: enriched,
            correlationId: corrId
        )
    }

    // MARK: - Timeout Watchdog

    private func startTimeoutWatchdog(correlationId: String) {
        timeoutTask?.cancel()
        let timeout = timeoutInterval
        timeoutTask = Task { [weak self] in
            try? await Task.sleep(nanoseconds: UInt64(timeout * 1_000_000_000))
            guard !Task.isCancelled else { return }
            guard let self else { return }
            // Only timeout if the same correlation is still active
            if await self.currentId == correlationId {
                await self.logger.warning(
                    source: "SosCorrelationManager",
                    template: "SOS auto-timeout after {TimeoutMin} minutes. CorrelationId: {CorrelationId}",
                    properties: [
                        "TimeoutMin": "\(Int(timeout / 60))",
                        "CorrelationId": correlationId,
                        "Stage": "TIMEOUT"
                    ],
                    correlationId: correlationId
                )
                await self.endCorrelation(reason: .timeout)
            }
        }
    }
}
