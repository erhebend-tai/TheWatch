/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         LogViewerView.swift (Views/Logging)                    │
 * │ Purpose:      Production-grade on-device log viewer. SwiftUI List   │
 * │               with color-coded capsule badges per level, .searchable│
 * │               modifier, expandable entries showing properties /      │
 * │               correlationId / exception, pull-to-refresh, and share │
 * │               sheet export.                                          │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: SwiftUI, Combine, LogViewerViewModel, WatchLogger     │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   NavigationLink("Logs") { LogViewerView() }                        │
 * │   // or present as sheet:                                            │
 * │   .sheet(isPresented: $showLogs) { LogViewerView() }                │
 * │                                                                      │
 * │ Differs from Views/Diagnostics/LogViewerView.swift:                  │
 * │ - Connects to real LoggingPort via WatchLogger.shared                │
 * │ - Uses .searchable() instead of manual TextField toggle              │
 * │ - Color-coded capsule badges (not just text color)                   │
 * │ - Expandable rows with animated disclosure                           │
 * │ - Pull-to-refresh via .refreshable                                   │
 * │ - Share sheet export via UIActivityViewController                    │
 * │ - Filter chips show per-level counts                                 │
 * │ - Proper VoiceOver accessibility                                     │
 * └──────────────────────────────────────────────────────────────────────┘
 */

import SwiftUI
import Combine

// MARK: - Color Helpers

private extension LogLevel {
    var color: Color {
        switch self {
        case .verbose:     return Color.gray
        case .debug:       return Color(red: 0.31, green: 0.76, blue: 0.97)
        case .information: return Color(red: 0.51, green: 0.78, blue: 0.52)
        case .warning:     return Color(red: 1.0, green: 0.72, blue: 0.30)
        case .error:       return Color(red: 0.90, green: 0.45, blue: 0.45)
        case .fatal:       return Color(red: 1.0, green: 0.09, blue: 0.27)
        }
    }

    var displayName: String {
        switch self {
        case .verbose:     return "Verbose"
        case .debug:       return "Debug"
        case .information: return "Info"
        case .warning:     return "Warning"
        case .error:       return "Error"
        case .fatal:       return "Fatal"
        }
    }
}

// MARK: - Main View

struct LogViewerView: View {
    @Environment(\.dismiss) private var dismiss
    @State private var viewModel = LogViewerViewModel()
    @State private var showShareSheet = false
    @State private var shareURL: URL? = nil

    private let darkBg = Color(red: 0.12, green: 0.12, blue: 0.12)
    private let cardBg = Color(red: 0.15, green: 0.15, blue: 0.15)
    private let navBg = Color(red: 0.06, green: 0.06, blue: 0.15)

    var body: some View {
        NavigationStack {
            ZStack {
                darkBg.ignoresSafeArea()

                VStack(spacing: 0) {
                    // ── Level Filter Chips ────────────────────
                    levelFilterBar

                    // ── Entry Count ──────────────────────────
                    HStack {
                        Text("\(viewModel.filteredCount) of \(viewModel.entryCount) entries")
                            .font(.caption2)
                            .foregroundColor(.gray)
                        Spacer()
                        if viewModel.isLiveStreaming {
                            HStack(spacing: 4) {
                                Circle()
                                    .fill(Color.green)
                                    .frame(width: 6, height: 6)
                                Text("LIVE")
                                    .font(.system(size: 9, weight: .bold, design: .monospaced))
                                    .foregroundColor(.green)
                            }
                        }
                    }
                    .padding(.horizontal, 16)
                    .padding(.vertical, 4)

                    // ── Log Entry List ────────────────────────
                    List {
                        ForEach(viewModel.filteredEntries) { entry in
                            LogEntryRow(
                                entry: entry,
                                isExpanded: viewModel.isExpanded(entry.id),
                                onToggle: { viewModel.toggleExpanded(entry.id) },
                                formatTime: viewModel.formatTime
                            )
                            .listRowBackground(cardBg)
                            .listRowSeparator(.hidden)
                            .listRowInsets(EdgeInsets(top: 2, leading: 8, bottom: 2, trailing: 8))
                        }
                    }
                    .listStyle(.plain)
                    .scrollContentBackground(.hidden)
                    .refreshable {
                        viewModel.refresh()
                    }
                }
            }
            .navigationTitle("Log Viewer")
            .navigationBarTitleDisplayMode(.inline)
            .toolbarBackground(navBg, for: .navigationBar)
            .toolbarBackground(.visible, for: .navigationBar)
            .toolbarColorScheme(.dark, for: .navigationBar)
            .searchable(
                text: Binding(
                    get: { viewModel.searchQuery },
                    set: { viewModel.setSearchQuery($0) }
                ),
                prompt: "Search messageTemplate, sourceContext..."
            )
            .toolbar {
                ToolbarItem(placement: .navigationBarLeading) {
                    Button("Close") { dismiss() }
                        .foregroundColor(Color(red: 0.9, green: 0.22, blue: 0.27))
                }
                ToolbarItem(placement: .navigationBarTrailing) {
                    HStack(spacing: 12) {
                        Button(action: exportAndShare) {
                            Image(systemName: "square.and.arrow.up")
                        }
                        .accessibilityLabel("Export and share logs")

                        Button(action: { viewModel.clearLogs() }) {
                            Image(systemName: "trash")
                        }
                        .accessibilityLabel("Clear all logs")
                    }
                    .foregroundColor(.white)
                }
            }
            .onAppear {
                if let port = WatchLogger.shared.port {
                    viewModel.configure(loggingPort: port)
                }
            }
            .sheet(isPresented: $showShareSheet) {
                if let url = shareURL {
                    ShareSheet(activityItems: [url])
                }
            }
        }
    }

    // MARK: - Level Filter Bar

    private var levelFilterBar: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 8) {
                // "All" chip
                FilterCapsule(
                    label: "All",
                    count: viewModel.entryCount,
                    color: .gray,
                    isSelected: viewModel.selectedLevel == nil,
                    action: { viewModel.setLevelFilter(nil) }
                )

                ForEach(LogLevel.allCases, id: \.self) { level in
                    FilterCapsule(
                        label: level.displayName,
                        count: viewModel.countForLevel(level),
                        color: level.color,
                        isSelected: viewModel.selectedLevel == level,
                        action: { viewModel.setLevelFilter(level) }
                    )
                }
            }
            .padding(.horizontal, 16)
            .padding(.vertical, 8)
        }
    }

    // MARK: - Export

    private func exportAndShare() {
        if let url = viewModel.exportToFile() {
            shareURL = url
            showShareSheet = true
        }
    }
}

// MARK: - Filter Capsule

private struct FilterCapsule: View {
    let label: String
    let count: Int
    let color: Color
    let isSelected: Bool
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            HStack(spacing: 4) {
                Circle()
                    .fill(color)
                    .frame(width: 8, height: 8)
                Text("\(label) (\(count))")
                    .font(.caption)
                    .fontWeight(isSelected ? .bold : .regular)
            }
            .padding(.horizontal, 12)
            .padding(.vertical, 6)
            .background(
                isSelected
                    ? color.opacity(0.3)
                    : Color(red: 0.2, green: 0.2, blue: 0.2)
            )
            .foregroundColor(.white)
            .clipShape(Capsule())
        }
        .frame(minWidth: 44, minHeight: 44)
        .accessibilityLabel("\(label) filter, \(count) entries")
        .accessibilityAddTraits(isSelected ? .isSelected : [])
    }
}

// MARK: - Log Entry Row

private struct LogEntryRow: View {
    let entry: LogEntry
    let isExpanded: Bool
    let onToggle: () -> Void
    let formatTime: (Date) -> String

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            // ── Header: time | level capsule | source | chevron ──
            Button(action: onToggle) {
                HStack {
                    Text(formatTime(entry.timestamp))
                        .font(.system(size: 10, design: .monospaced))
                        .foregroundColor(.gray)

                    // Color-coded capsule badge
                    Text(entry.level.label)
                        .font(.system(size: 9, weight: .bold, design: .monospaced))
                        .foregroundColor(.black)
                        .padding(.horizontal, 6)
                        .padding(.vertical, 2)
                        .background(entry.level.color)
                        .clipShape(Capsule())

                    Text(entry.sourceContext)
                        .font(.system(size: 10, design: .monospaced))
                        .foregroundColor(Color(white: 0.62))
                        .lineLimit(1)

                    Spacer()

                    Image(systemName: isExpanded ? "chevron.up" : "chevron.down")
                        .font(.caption2)
                        .foregroundColor(.gray)
                }
            }
            .buttonStyle(.plain)

            // ── Rendered message ──
            Text(entry.renderedMessage)
                .font(.system(size: 12, design: .monospaced))
                .foregroundColor(.white)
                .lineSpacing(2)

            // ── Expanded detail section ──
            if isExpanded {
                VStack(alignment: .leading, spacing: 6) {
                    Divider().overlay(Color.gray.opacity(0.3))

                    // Properties
                    if !entry.properties.isEmpty {
                        Text("Properties")
                            .font(.system(size: 10, weight: .bold))
                            .foregroundColor(LogLevel.information.color)

                        ForEach(Array(entry.properties.keys.sorted()), id: \.self) { key in
                            HStack(spacing: 4) {
                                Text("\(key):")
                                    .font(.system(size: 10, weight: .bold, design: .monospaced))
                                    .foregroundColor(LogLevel.debug.color)
                                Text(entry.properties[key] ?? "")
                                    .font(.system(size: 10, design: .monospaced))
                                    .foregroundColor(.white)
                            }
                            .padding(.leading, 8)
                        }
                    }

                    // Correlation ID
                    if let cid = entry.correlationId {
                        Divider().overlay(Color.gray.opacity(0.3))
                        HStack(spacing: 4) {
                            Text("CorrelationId:")
                                .font(.system(size: 10, weight: .bold, design: .monospaced))
                                .foregroundColor(LogLevel.warning.color)
                            Text(cid)
                                .font(.system(size: 10, design: .monospaced))
                                .foregroundColor(.white)
                        }
                    }

                    // Exception
                    if let ex = entry.exception {
                        Divider().overlay(Color.gray.opacity(0.3))
                        Text("Exception")
                            .font(.system(size: 10, weight: .bold))
                            .foregroundColor(LogLevel.error.color)
                        Text(ex)
                            .font(.system(size: 10, design: .monospaced))
                            .foregroundColor(LogLevel.error.color.opacity(0.85))
                            .lineSpacing(2)
                    }

                    // Metadata footer
                    Divider().overlay(Color.gray.opacity(0.3))
                    HStack {
                        Text("id: \(String(entry.id.prefix(8)))…")
                            .font(.system(size: 9, design: .monospaced))
                            .foregroundColor(.gray)
                        Spacer()
                        if let uid = entry.userId {
                            Text("user: \(String(uid.prefix(8)))…")
                                .font(.system(size: 9, design: .monospaced))
                                .foregroundColor(.gray)
                        }
                        Text(entry.synced ? "synced" : "local")
                            .font(.system(size: 9, weight: .bold, design: .monospaced))
                            .foregroundColor(entry.synced ? LogLevel.information.color : LogLevel.warning.color)
                    }
                }
                .padding(8)
                .background(Color(red: 0.18, green: 0.18, blue: 0.18))
                .cornerRadius(4)
                .transition(.opacity.combined(with: .move(edge: .top)))
            }
        }
        .padding(8)
        .background(Color(red: 0.15, green: 0.15, blue: 0.15))
        .cornerRadius(6)
        .accessibilityElement(children: .combine)
        .accessibilityLabel("\(entry.level.displayName) log from \(entry.sourceContext): \(entry.renderedMessage)")
        .accessibilityHint(isExpanded ? "Collapse to hide details" : "Expand to show properties and details")
    }
}

// MARK: - Share Sheet (UIKit bridge)

private struct ShareSheet: UIViewControllerRepresentable {
    let activityItems: [Any]

    func makeUIViewController(context: Context) -> UIActivityViewController {
        UIActivityViewController(activityItems: activityItems, applicationActivities: nil)
    }

    func updateUIViewController(_ uiViewController: UIActivityViewController, context: Context) {}
}

// MARK: - Preview

#Preview {
    LogViewerView()
}
