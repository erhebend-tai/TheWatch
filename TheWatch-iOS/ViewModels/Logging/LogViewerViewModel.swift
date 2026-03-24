/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         LogViewerViewModel.swift (ViewModels/Logging)          │
 * │ Purpose:      @Observable ViewModel for the production Log Viewer.   │
 * │               Subscribes to loggingPort.logPublisher for real-time   │
 * │               streaming. Manages filter/search state, expandable     │
 * │               entry IDs, and share-sheet export of filtered output.  │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: Combine, LoggingPort, LogEntry, LogLevel              │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   @State private var viewModel = LogViewerViewModel()                │
 * │   // In onAppear: viewModel.configure(loggingPort: WatchLogger.shared.port!) │
 * │                                                                      │
 * │ Architecture:                                                        │
 * │ - All data flows through LoggingPort (hexagonal port/adapter)        │
 * │ - No direct SwiftData/Core Data access from this ViewModel           │
 * │ - logPublisher gives a Combine AnyPublisher<LogEntry, Never>         │
 * │ - query() used for initial load and pull-to-refresh                  │
 * │ - Export writes to temp directory, returns URL for UIActivityVC      │
 * │                                                                      │
 * │ Filter semantics:                                                    │
 * │ - Level filter: shows entries AT or ABOVE selected level             │
 * │ - Search: case-insensitive on messageTemplate + sourceContext        │
 * │ - Both compose with AND logic                                        │
 * │                                                                      │
 * │ Expandable entries:                                                  │
 * │ - Tap row to toggle; expanded shows properties, correlationId, etc  │
 * │ - Multiple simultaneous expansions supported                         │
 * │                                                                      │
 * │ Export format (plain text):                                          │
 * │   [2026-03-24T10:35:00Z] [information] [LocationCoordinator]        │
 * │   Location updated to 30.2672,-97.7431 in HIGH_ACCURACY mode        │
 * │   Properties: {Lat: 30.2672, Lng: -97.7431}                         │
 * │   CorrelationId: sos-abc123                                          │
 * │   ---                                                                │
 * └──────────────────────────────────────────────────────────────────────┘
 */

import Foundation
import Combine
import SwiftUI

@Observable
final class LogViewerViewModel {

    // MARK: - Published State

    var entries: [LogEntry] = []
    var filteredEntries: [LogEntry] = []
    var selectedLevel: LogLevel? = nil
    var searchQuery: String = ""
    var expandedEntryIds: Set<String> = []
    var isRefreshing: Bool = false
    var isLiveStreaming: Bool = true
    var exportFileURL: URL? = nil

    // MARK: - Computed

    var entryCount: Int { entries.count }
    var filteredCount: Int { filteredEntries.count }

    func countForLevel(_ level: LogLevel) -> Int {
        entries.count { $0.level == level }
    }

    // MARK: - Private

    private var loggingPort: (any LoggingPort)?
    private var cancellables = Set<AnyCancellable>()

    private let dateFormatter: DateFormatter = {
        let f = DateFormatter()
        f.dateFormat = "HH:mm:ss.SSS"
        return f
    }()

    private let isoFormatter: ISO8601DateFormatter = {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return f
    }()

    // MARK: - Configuration

    /// Call this once to wire the ViewModel to the LoggingPort.
    /// Typically called from .onAppear in the View.
    func configure(loggingPort: any LoggingPort) {
        guard self.loggingPort == nil else { return } // Already configured
        self.loggingPort = loggingPort
        initialLoad()
        startLiveStream()
    }

    // MARK: - Initial Load

    private func initialLoad() {
        guard let port = loggingPort else { return }
        Task { @MainActor in
            let loaded = await port.query(limit: 500, minLevel: .verbose, sourceContext: nil)
            self.entries = loaded
            self.applyFilters()
        }
    }

    // MARK: - Live Streaming

    private func startLiveStream() {
        guard let port = loggingPort else { return }
        port.logPublisher
            .receive(on: DispatchQueue.main)
            .sink { [weak self] entry in
                guard let self else { return }
                self.entries.insert(entry, at: 0)
                // Cap at 2000 in memory
                if self.entries.count > 2000 {
                    self.entries = Array(self.entries.prefix(2000))
                }
                self.applyFilters()
            }
            .store(in: &cancellables)
    }

    // MARK: - Pull-to-Refresh

    func refresh() {
        guard let port = loggingPort else { return }
        isRefreshing = true
        Task { @MainActor in
            let loaded = await port.query(limit: 500, minLevel: .verbose, sourceContext: nil)
            self.entries = loaded
            self.isRefreshing = false
            self.applyFilters()
        }
    }

    // MARK: - Filters

    func setLevelFilter(_ level: LogLevel?) {
        selectedLevel = level
        applyFilters()
    }

    func setSearchQuery(_ query: String) {
        searchQuery = query
        applyFilters()
    }

    private func applyFilters() {
        var result = entries

        if let minLevel = selectedLevel {
            result = result.filter { $0.level.rawValue >= minLevel.rawValue }
        }

        if !searchQuery.isEmpty {
            result = result.filter {
                $0.messageTemplate.localizedCaseInsensitiveContains(searchQuery) ||
                $0.sourceContext.localizedCaseInsensitiveContains(searchQuery) ||
                $0.renderedMessage.localizedCaseInsensitiveContains(searchQuery)
            }
        }

        filteredEntries = result
    }

    // MARK: - Expand / Collapse

    func toggleExpanded(_ entryId: String) {
        if expandedEntryIds.contains(entryId) {
            expandedEntryIds.remove(entryId)
        } else {
            expandedEntryIds.insert(entryId)
        }
    }

    func isExpanded(_ entryId: String) -> Bool {
        expandedEntryIds.contains(entryId)
    }

    // MARK: - Clear

    func clearLogs() {
        entries = []
        filteredEntries = []
        expandedEntryIds = []
    }

    // MARK: - Formatting

    func formatTime(_ date: Date) -> String {
        dateFormatter.string(from: date)
    }

    // MARK: - Export to Share

    /// Writes filtered entries to a temp .txt file and returns the URL.
    /// Use this URL with UIActivityViewController / ShareLink.
    func exportToFile() -> URL? {
        guard !filteredEntries.isEmpty else { return nil }

        var text = ""
        text += "TheWatch Log Export — \(isoFormatter.string(from: Date()))\n"
        text += "Filter: level=\(selectedLevel.map { "\($0)" } ?? "ALL"), query=\"\(searchQuery)\"\n"
        text += "Entries: \(filteredEntries.count)\n"
        text += String(repeating: "═", count: 72) + "\n\n"

        for entry in filteredEntries {
            text += "[\(isoFormatter.string(from: entry.timestamp))] [\(entry.level)] [\(entry.sourceContext)]\n"
            text += entry.renderedMessage + "\n"
            if !entry.properties.isEmpty {
                let props = entry.properties.map { "  \($0.key): \($0.value)" }.joined(separator: "\n")
                text += "  Properties:\n\(props)\n"
            }
            if let cid = entry.correlationId {
                text += "  CorrelationId: \(cid)\n"
            }
            if let ex = entry.exception {
                text += "  Exception: \(ex)\n"
            }
            text += "---\n"
        }

        let formatter = DateFormatter()
        formatter.dateFormat = "yyyyMMdd_HHmmss"
        let timestamp = formatter.string(from: Date())
        let fileName = "thewatch_logs_\(timestamp).txt"
        let tempURL = FileManager.default.temporaryDirectory.appendingPathComponent(fileName)

        do {
            try text.write(to: tempURL, atomically: true, encoding: .utf8)
            exportFileURL = tempURL
            return tempURL
        } catch {
            return nil
        }
    }
}
