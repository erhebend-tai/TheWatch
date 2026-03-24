import Foundation
import Combine

// ── Write-Ahead Log ──────────────────────────────────────────────
// WAL: SosTimelineBuilder queries the LoggingPort by correlationId to reconstruct
// the full SOS lifecycle as a sorted timeline. This powers both:
//   1. On-device history view (HistoryScreen / SOS detail)
//   2. MAUI dashboard timeline (MobileLogController → SignalR → DashboardHub)
//
// The timeline is sorted chronologically and annotated with stage labels
// extracted from the "Stage" property in each log entry.
//
// Example usage:
// ```swift
// let builder = SosTimelineBuilder(loggingPort: mockLogging)
// let events = await builder.build(correlationId: "abc-123-def")
// for event in events {
//     print("\(event.timestamp) [\(event.stage ?? "?")] \(event.renderedMessage)")
// }
//
// if let summary = await builder.summarize(correlationId: "abc-123-def") {
//     print("SOS \(summary.correlationId): \(summary.triggerMethod ?? "?") → \(summary.resolution ?? "active")")
//     print("Duration: \(summary.durationFormatted ?? "ongoing"), Stages: \(summary.stageCount)")
// }
// ```

/// A single event in the SOS timeline, enriched with stage metadata.
struct SosTimelineEvent: Identifiable, Sendable {
    let id: String
    let logEntry: LogEntry
    let stage: String?
    let timestamp: Date
    let renderedMessage: String

    static func from(_ entry: LogEntry) -> SosTimelineEvent {
        SosTimelineEvent(
            id: entry.id,
            logEntry: entry,
            stage: entry.properties["Stage"],
            timestamp: entry.timestamp,
            renderedMessage: entry.renderedMessage
        )
    }
}

/// Summary of a complete SOS lifecycle — computed from the timeline entries.
struct SosTimelineSummary: Sendable {
    let correlationId: String
    let triggerMethod: String?
    let resolution: String?
    let triggerTimestamp: Date?
    let resolutionTimestamp: Date?
    let durationMs: Int?
    let durationFormatted: String?
    let stageCount: Int
    let entryCount: Int
    let stages: [String]
    let hasEscalation: Bool
    let volunteerCount: Int
}

/// Builds a chronological timeline of an SOS lifecycle from log entries.
///
/// Queries `LoggingPort.queryByCorrelation` and transforms raw log entries into
/// an ordered, annotated timeline that both on-device UI and MAUI dashboard consume.
///
/// Thread-safe: all operations are async, delegating to the logging port.
final class SosTimelineBuilder: Sendable {

    private let loggingPort: any LoggingPort

    init(loggingPort: any LoggingPort) {
        self.loggingPort = loggingPort
    }

    /// Build a sorted timeline for the given correlationId.
    ///
    /// - Parameter correlationId: The SOS correlation ID to query
    /// - Returns: List of timeline events, sorted chronologically (oldest first)
    func build(correlationId: String) async -> [SosTimelineEvent] {
        let entries = await loggingPort.queryByCorrelation(correlationId)
        return entries
            .sorted { $0.timestamp < $1.timestamp }
            .map { SosTimelineEvent.from($0) }
    }

    /// Build a summary of the SOS lifecycle — trigger method, resolution, duration,
    /// stages traversed, volunteer count, escalation status.
    ///
    /// - Parameter correlationId: The SOS correlation ID to summarize
    /// - Returns: Summary object, or nil if no entries found
    func summarize(correlationId: String) async -> SosTimelineSummary? {
        let events = await build(correlationId: correlationId)
        guard !events.isEmpty else { return nil }

        let triggerEvent = events.first { $0.stage == "TRIGGER" }
        let resolutionEvent = events.last { $0.stage == "RESOLUTION" }

        let triggerMethod = triggerEvent?.logEntry.properties["Method"]
        let resolution = resolutionEvent?.logEntry.properties["Reason"]

        let triggerTs = triggerEvent?.timestamp ?? events.first!.timestamp
        let resolutionTs = resolutionEvent?.timestamp

        let durationMs = resolutionTs.map { Int($0.timeIntervalSince(triggerTs) * 1000) }
        let durationFormatted = durationMs.map { Self.formatDuration(ms: $0) }

        let stages = events.compactMap(\.stage).uniqued()

        let escalationStages: Set<String> = ["ESCALATION", "FIRST_RESPONDER_DISPATCH", "NG911_CALL"]
        let hasEscalation = stages.contains(where: { escalationStages.contains($0) })

        // Count unique volunteer IDs from VOLUNTEER_ACKNOWLEDGE stage entries
        let volunteerCount = Set(
            events
                .filter { $0.stage == "VOLUNTEER_ACKNOWLEDGE" }
                .compactMap { $0.logEntry.properties["VolunteerId"] }
        ).count

        return SosTimelineSummary(
            correlationId: correlationId,
            triggerMethod: triggerMethod,
            resolution: resolution,
            triggerTimestamp: triggerTs,
            resolutionTimestamp: resolutionTs,
            durationMs: durationMs,
            durationFormatted: durationFormatted,
            stageCount: stages.count,
            entryCount: events.count,
            stages: stages,
            hasEscalation: hasEscalation,
            volunteerCount: volunteerCount
        )
    }

    /// Get only events for a specific pipeline stage.
    ///
    /// - Parameters:
    ///   - correlationId: The SOS correlation ID
    ///   - stage: Stage name (TRIGGER, ALERT_DISPATCH, VOLUNTEER_NOTIFY, etc.)
    /// - Returns: Filtered and sorted events for that stage
    func events(forStage stage: String, correlationId: String) async -> [SosTimelineEvent] {
        await build(correlationId: correlationId).filter { $0.stage == stage }
    }

    /// Check whether a correlation is still active (has TRIGGER but no RESOLUTION).
    func isActive(correlationId: String) async -> Bool {
        let events = await build(correlationId: correlationId)
        let hasTrigger = events.contains { $0.stage == "TRIGGER" }
        let hasResolution = events.contains { $0.stage == "RESOLUTION" }
        return hasTrigger && !hasResolution
    }

    // MARK: - Private

    private static func formatDuration(ms: Int) -> String {
        let totalSeconds = ms / 1000
        let hours = totalSeconds / 3600
        let minutes = (totalSeconds % 3600) / 60
        let seconds = totalSeconds % 60
        var parts: [String] = []
        if hours > 0 { parts.append("\(hours)h") }
        if minutes > 0 { parts.append("\(minutes)m") }
        parts.append("\(seconds)s")
        return parts.joined(separator: " ")
    }
}

// MARK: - Array Extension

private extension Array where Element: Hashable {
    /// Returns elements in order, removing duplicates.
    func uniqued() -> [Element] {
        var seen = Set<Element>()
        return filter { seen.insert($0).inserted }
    }
}
