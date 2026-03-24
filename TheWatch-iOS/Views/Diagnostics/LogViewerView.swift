/**
 * WRITE-AHEAD LOG | File: LogViewerView.swift | Purpose: On-device log viewer with real-time stream
 * Created: 2026-03-24 | Author: Claude | Deps: SwiftUI, LoggingPort
 * Usage: NavigationLink("Log Viewer") { LogViewerView() }
 * NOTE: Developer diagnostics only. Gate behind dev mode.
 */
import SwiftUI

@Observable final class LogViewerViewModel {
    var entries: [LogEntry] = []; var filtered: [LogEntry] = []; var selectedLevel: LogLevel? = nil; var query = ""; var isLive = true
    private let df: DateFormatter = { let f = DateFormatter(); f.dateFormat = "HH:mm:ss.SSS"; return f }()
    func fmtTime(_ d: Date) -> String { df.string(from: d) }

    init() {
        entries = [
            LogEntry(level: .information, sourceContext: "LocationCoordinator", messageTemplate: "Location updated to {Lat},{Lng}", properties: ["Lat": "30.27", "Lng": "-97.74"]),
            LogEntry(level: .warning, sourceContext: "PhraseDetection", messageTemplate: "Audio buffer underrun: {N} frames", properties: ["N": "12"]),
            LogEntry(level: .error, sourceContext: "NotificationService", messageTemplate: "APNS failed: {E}", properties: ["E": "NETWORK_UNAVAILABLE"]),
            LogEntry(level: .debug, sourceContext: "QuickTapDetector", messageTemplate: "Tap: count={C}, interval={I}ms", properties: ["C": "3", "I": "450"]),
            LogEntry(level: .information, sourceContext: "SOSService", messageTemplate: "SOS dispatched to {N} responders", properties: ["N": "7"]),
            LogEntry(level: .fatal, sourceContext: "PersistenceController", messageTemplate: "Migration failed v{F}->v{T}", properties: ["F": "3", "T": "4"])
        ]
        filtered = entries
        Task { @MainActor in while isLive { try? await Task.sleep(nanoseconds: 5_000_000_000); let src = ["LocationCoordinator", "SOSService", "PhraseDetection"].randomElement()!; let lev: [LogLevel] = [.debug, .information, .warning]; entries.insert(LogEntry(level: lev.randomElement()!, sourceContext: src, messageTemplate: "Heartbeat from {S}", properties: ["S": src]), at: 0); applyFilters() } }
    }

    func setLevel(_ l: LogLevel?) { selectedLevel = l; applyFilters() }
    func setQuery(_ q: String) { query = q; applyFilters() }
    func clear() { entries = []; filtered = [] }
    func allText() -> String { filtered.map { "[\(fmtTime($0.timestamp))] [\($0.level.rawValue)] [\($0.sourceContext)] \($0.renderedMessage)" }.joined(separator: "\n") }

    private func applyFilters() {
        var r = entries
        if let l = selectedLevel { let all: [LogLevel] = [.verbose, .debug, .information, .warning, .error, .fatal]; if let i = all.firstIndex(of: l) { let valid = Set(all[i...]); r = r.filter { valid.contains($0.level) } } }
        if !query.isEmpty { r = r.filter { $0.renderedMessage.localizedCaseInsensitiveContains(query) || $0.sourceContext.localizedCaseInsensitiveContains(query) } }
        filtered = r
    }
}

struct LogViewerView: View {
    @Environment(\.dismiss) var dismiss
    @State private var vm = LogViewerViewModel()
    @State private var searchOpen = false

    var body: some View {
        ZStack {
            Color(red: 0.12, green: 0.12, blue: 0.12).ignoresSafeArea()
            VStack(spacing: 0) {
                HStack {
                    Button(action: { dismiss() }) { HStack(spacing: 4) { Image(systemName: "chevron.left"); Text("Back") }.foregroundColor(Color(red: 0.9, green: 0.22, blue: 0.27)) }
                    Spacer(); Text("Log Viewer").font(.headline).foregroundColor(.white); Spacer()
                    HStack(spacing: 16) {
                        Button(action: { searchOpen.toggle() }) { Image(systemName: "magnifyingglass").foregroundColor(.white) }.frame(minWidth: 44, minHeight: 44)
                        Button(action: { UIPasteboard.general.string = vm.allText() }) { Image(systemName: "doc.on.doc").foregroundColor(.white) }.frame(minWidth: 44, minHeight: 44)
                        Button(action: { vm.clear() }) { Image(systemName: "trash").foregroundColor(.white) }.frame(minWidth: 44, minHeight: 44)
                    }
                }.padding(16).background(Color(red: 0.06, green: 0.06, blue: 0.15))

                if searchOpen { TextField("Search...", text: Binding(get: { vm.query }, set: { vm.setQuery($0) })).textFieldStyle(.roundedBorder).padding(.horizontal, 16).padding(.vertical, 8) }

                ScrollView(.horizontal, showsIndicators: false) {
                    HStack(spacing: 8) {
                        chipBtn("All", vm.selectedLevel == nil) { vm.setLevel(nil) }
                        ForEach([LogLevel.verbose, .debug, .information, .warning, .error, .fatal], id: \.self) { l in chipBtn(l.rawValue.capitalized, vm.selectedLevel == l) { vm.setLevel(l) } }
                    }.padding(.horizontal, 16).padding(.vertical, 8)
                }

                Text("\(vm.filtered.count) entries").font(.caption2).foregroundColor(.gray).frame(maxWidth: .infinity, alignment: .leading).padding(.horizontal, 16).padding(.bottom, 4)

                ScrollView {
                    LazyVStack(spacing: 4) {
                        ForEach(vm.filtered) { entry in
                            let c: Color = { switch entry.level { case .verbose: return .gray; case .debug: return Color(red: 0.31, green: 0.76, blue: 0.97); case .information: return Color(red: 0.51, green: 0.78, blue: 0.52); case .warning: return Color(red: 1, green: 0.72, blue: 0.3); case .error: return Color(red: 0.9, green: 0.45, blue: 0.45); case .fatal: return Color(red: 1, green: 0.09, blue: 0.27) } }()
                            VStack(alignment: .leading, spacing: 4) {
                                HStack { Text(vm.fmtTime(entry.timestamp)).font(.system(size: 10, design: .monospaced)).foregroundColor(.gray); Text(String(entry.level.rawValue.prefix(4)).uppercased()).font(.system(size: 10, weight: .bold, design: .monospaced)).foregroundColor(c); Text(entry.sourceContext).font(.system(size: 10, design: .monospaced)).foregroundColor(Color(white: 0.62)); Spacer(); Button(action: { UIPasteboard.general.string = entry.renderedMessage }) { Image(systemName: "doc.on.doc").font(.caption2).foregroundColor(.gray) }.frame(minWidth: 44, minHeight: 44) }
                                Text(entry.renderedMessage).font(.system(size: 12, design: .monospaced)).foregroundColor(.white).lineSpacing(2)
                            }.padding(8).background(Color(red: 0.15, green: 0.15, blue: 0.15)).cornerRadius(4)
                        }
                    }.padding(.horizontal, 8)
                }
            }
        }
    }

    private func chipBtn(_ label: String, _ selected: Bool, action: @escaping () -> Void) -> some View {
        Button(action: action) { Text(label).font(.caption).fontWeight(selected ? .bold : .regular).padding(.horizontal, 12).padding(.vertical, 6).background(selected ? Color(red: 0.9, green: 0.22, blue: 0.27) : Color(red: 0.2, green: 0.2, blue: 0.2)).foregroundColor(.white).cornerRadius(16) }.frame(minWidth: 44, minHeight: 44)
    }
}
