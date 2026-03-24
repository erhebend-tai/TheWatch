import SwiftUI

struct HistoryView: View {
    @Environment(MockHistoryService.self) var historyService
    @State var viewModel: HistoryViewModel?
    @State private var showFilters = false
    @State private var selectedEvent: HistoryEvent?

    var body: some View {
        NavigationStack {
            ZStack {
                Color(red: 0.97, green: 0.97, blue: 0.97)
                    .ignoresSafeArea()

                VStack(spacing: 0) {
                    // Header
                    HStack {
                        Text("History")
                            .font(.headline)
                            .fontWeight(.bold)
                        Spacer()
                        Button(action: { showFilters.toggle() }) {
                            Image(systemName: "line.3.horizontal.decrease.circle")
                                .font(.headline)
                                .foregroundColor(Color(red: 0.9, green: 0.22, blue: 0.27))
                        }
                        .accessibilityLabel("Show filters")
                    }
                    .padding(16)
                    .background(Color.white)

                    if let vm = viewModel {
                        if showFilters {
                            // Filters Section
                            VStack(spacing: 12) {
                                HStack {
                                    Text("Filters")
                                        .font(.subheadline)
                                        .fontWeight(.semibold)
                                    Spacer()
                                    Button(action: { vm.clearFilters() }) {
                                        Text("Clear All")
                                            .font(.caption)
                                            .foregroundColor(Color(red: 0.9, green: 0.22, blue: 0.27))
                                    }
                                    .accessibilityLabel("Clear all filters")
                                }

                                // Event Type Filter
                                VStack(alignment: .leading, spacing: 8) {
                                    Text("Event Type")
                                        .font(.caption)
                                        .fontWeight(.semibold)
                                    Picker("Type", selection: $vm.selectedEventType) {
                                        Text("All").tag(Optional<String>(nil))
                                        Text("Medical").tag(Optional("medical"))
                                        Text("Security").tag(Optional("security"))
                                        Text("Evacuation").tag(Optional("evacuation"))
                                        Text("Other").tag(Optional("other"))
                                    }
                                    .pickerStyle(.segmented)
                                }

                                // Severity Filter
                                VStack(alignment: .leading, spacing: 8) {
                                    Text("Severity")
                                        .font(.caption)
                                        .fontWeight(.semibold)
                                    Picker("Severity", selection: $vm.selectedSeverity) {
                                        Text("All").tag(Optional<AlertSeverity>(nil))
                                        Text("Low").tag(Optional(AlertSeverity.low))
                                        Text("Medium").tag(Optional(AlertSeverity.medium))
                                        Text("High").tag(Optional(AlertSeverity.high))
                                        Text("Critical").tag(Optional(AlertSeverity.critical))
                                    }
                                    .pickerStyle(.segmented)
                                }

                                // Status Filter
                                VStack(alignment: .leading, spacing: 8) {
                                    Text("Status")
                                        .font(.caption)
                                        .fontWeight(.semibold)
                                    Picker("Status", selection: $vm.selectedStatus) {
                                        Text("All").tag(Optional<String>(nil))
                                        Text("Active").tag(Optional("active"))
                                        Text("Resolved").tag(Optional("resolved"))
                                        Text("Cancelled").tag(Optional("cancelled"))
                                    }
                                    .pickerStyle(.segmented)
                                }

                                // Date Range
                                VStack(alignment: .leading, spacing: 8) {
                                    Text("Date Range")
                                        .font(.caption)
                                        .fontWeight(.semibold)
                                    HStack(spacing: 12) {
                                        VStack(alignment: .leading, spacing: 4) {
                                            Text("From")
                                                .font(.caption2)
                                                .foregroundColor(.gray)
                                            DatePicker("", selection: $vm.startDate, displayedComponents: .date)
                                                .labelsHidden()
                                        }
                                        VStack(alignment: .leading, spacing: 4) {
                                            Text("To")
                                                .font(.caption2)
                                                .foregroundColor(.gray)
                                            DatePicker("", selection: $vm.endDate, displayedComponents: .date)
                                                .labelsHidden()
                                        }
                                    }
                                }

                                Button(action: { showFilters = false }) {
                                    Text("Apply Filters")
                                        .frame(maxWidth: .infinity)
                                        .padding(12)
                                        .background(Color(red: 0.9, green: 0.22, blue: 0.27))
                                        .foregroundColor(.white)
                                        .cornerRadius(8)
                                }
                                .accessibilityLabel("Apply filters")
                            }
                            .padding(12)
                            .background(Color.white)
                            .padding(12)
                        }

                        // Events List
                        ScrollView {
                            LazyVStack(spacing: 12) {
                                if vm.filteredEvents.isEmpty {
                                    VStack(spacing: 16) {
                                        Image(systemName: "doc.text.magnifyingglass")
                                            .font(.system(size: 48))
                                            .foregroundColor(.gray)
                                        Text("No events found")
                                            .font(.headline)
                                        Text("Try adjusting your filters")
                                            .font(.caption)
                                            .foregroundColor(.gray)
                                    }
                                    .frame(maxWidth: .infinity)
                                    .padding(48)
                                } else {
                                    ForEach(vm.filteredEvents) { event in
                                        NavigationLink(destination: HistoryDetailView(event: event)) {
                                            HistoryEventRow(event: event)
                                        }
                                        .accessibilityLabel("Event: \(event.eventType)")
                                    }
                                }
                            }
                            .padding(12)
                        }
                    }
                }
            }
            .onAppear {
                if viewModel == nil {
                    viewModel = HistoryViewModel(historyService: historyService)
                    Task {
                        await viewModel?.loadHistory()
                    }
                }
            }
        }
    }
}

struct HistoryEventRow: View {
    let event: HistoryEvent

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                VStack(alignment: .leading, spacing: 4) {
                    Text(event.eventType)
                        .font(.subheadline)
                        .fontWeight(.semibold)
                    Text(event.dateTime.formatted(date: .abbreviated, time: .shortened))
                        .font(.caption)
                        .foregroundColor(.gray)
                }
                Spacer()
                VStack(alignment: .trailing, spacing: 4) {
                    SeverityBadge(severity: event.severity)
                    StatusBadge(status: event.status)
                }
            }

            if !event.description.isEmpty {
                Text(event.description)
                    .font(.caption)
                    .foregroundColor(.gray)
                    .lineLimit(2)
            }

            HStack(spacing: 16) {
                if let duration = event.durationDisplay {
                    HStack(spacing: 4) {
                        Image(systemName: "clock.fill")
                            .font(.caption)
                            .foregroundColor(.gray)
                        Text(duration)
                            .font(.caption2)
                            .foregroundColor(.gray)
                    }
                }

                Spacer()

                Image(systemName: "chevron.right")
                    .font(.caption)
                    .foregroundColor(.gray)
            }
        }
        .padding(12)
        .background(Color.white)
        .cornerRadius(8)
    }
}

struct SeverityBadge: View {
    let severity: AlertSeverity

    var body: some View {
        Text(severity.rawValue.capitalized)
            .font(.caption2)
            .fontWeight(.semibold)
            .padding(.horizontal, 8)
            .padding(.vertical, 4)
            .background(backgroundColor)
            .foregroundColor(foregroundColor)
            .cornerRadius(4)
    }

    var backgroundColor: Color {
        switch severity {
        case .low:
            return Color.blue.opacity(0.1)
        case .medium:
            return Color.yellow.opacity(0.1)
        case .high:
            return Color.orange.opacity(0.1)
        case .critical:
            return Color.red.opacity(0.1)
        }
    }

    var foregroundColor: Color {
        switch severity {
        case .low:
            return .blue
        case .medium:
            return Color(red: 1.0, green: 0.6, blue: 0.0)
        case .high:
            return .orange
        case .critical:
            return .red
        }
    }
}

struct StatusBadge: View {
    let status: String

    var body: some View {
        HStack(spacing: 4) {
            Circle()
                .fill(statusColor)
                .frame(width: 6, height: 6)
            Text(status.capitalized)
                .font(.caption2)
                .fontWeight(.semibold)
        }
        .padding(.horizontal, 8)
        .padding(.vertical, 4)
        .background(Color.gray.opacity(0.1))
        .foregroundColor(.gray)
        .cornerRadius(4)
    }

    var statusColor: Color {
        switch status {
        case "active":
            return .green
        case "resolved":
            return .blue
        case "cancelled":
            return .gray
        default:
            return .gray
        }
    }
}

#Preview {
    HistoryView()
        .environment(MockHistoryService())
}
