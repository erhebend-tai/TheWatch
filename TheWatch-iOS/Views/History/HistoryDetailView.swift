import SwiftUI

struct HistoryDetailView: View {
    let event: HistoryEvent
    @Environment(\.dismiss) var dismiss

    var body: some View {
        ZStack {
            Color(red: 0.97, green: 0.97, blue: 0.97)
                .ignoresSafeArea()

            VStack(spacing: 0) {
                // Header
                HStack {
                    Button(action: { dismiss() }) {
                        HStack(spacing: 4) {
                            Image(systemName: "chevron.left")
                            Text("Back")
                        }
                        .foregroundColor(Color(red: 0.9, green: 0.22, blue: 0.27))
                    }
                    Spacer()
                }
                .padding(16)

                ScrollView {
                    VStack(spacing: 16) {
                        // Title Section
                        VStack(alignment: .leading, spacing: 12) {
                            HStack {
                                VStack(alignment: .leading, spacing: 4) {
                                    Text(event.eventType)
                                        .font(.title2)
                                        .fontWeight(.bold)
                                    Text(event.dateTime.formatted(date: .abbreviated, time: .shortened))
                                        .font(.caption)
                                        .foregroundColor(.gray)
                                }
                                Spacer()
                                VStack(alignment: .trailing, spacing: 8) {
                                    SeverityBadge(severity: event.severity)
                                    StatusBadge(status: event.status)
                                }
                            }
                            .padding(12)
                            .background(Color.white)
                            .cornerRadius(8)
                        }

                        // Description Section
                        if !event.description.isEmpty {
                            VStack(alignment: .leading, spacing: 12) {
                                Text("Description")
                                    .font(.subheadline)
                                    .fontWeight(.semibold)

                                Text(event.description)
                                    .font(.body)
                                    .foregroundColor(.gray)
                                    .padding(12)
                                    .background(Color.white)
                                    .cornerRadius(8)
                            }
                        }

                        // Duration Section
                        if let duration = event.durationDisplay {
                            VStack(alignment: .leading, spacing: 12) {
                                Text("Duration")
                                    .font(.subheadline)
                                    .fontWeight(.semibold)

                                HStack {
                                    Image(systemName: "clock.fill")
                                        .font(.headline)
                                        .foregroundColor(Color(red: 0.9, green: 0.22, blue: 0.27))

                                    VStack(alignment: .leading, spacing: 4) {
                                        Text(duration)
                                            .font(.subheadline)
                                            .fontWeight(.semibold)
                                        Text("Total response time")
                                            .font(.caption)
                                            .foregroundColor(.gray)
                                    }

                                    Spacer()
                                }
                                .padding(12)
                                .background(Color.white)
                                .cornerRadius(8)
                            }
                        }

                        // Details Section
                        VStack(alignment: .leading, spacing: 12) {
                            Text("Details")
                                .font(.subheadline)
                                .fontWeight(.semibold)

                            VStack(spacing: 12) {
                                DetailRow(label: "Event Type", value: event.eventType)
                                Divider()
                                DetailRow(label: "Status", value: event.status.capitalized)
                                Divider()
                                DetailRow(label: "Severity", value: event.severity.rawValue.capitalized)
                                Divider()
                                DetailRow(label: "Date", value: event.dateTime.formatted(date: .long, time: .omitted))
                                Divider()
                                DetailRow(label: "Time", value: event.dateTime.formatted(date: .omitted, time: .standard))
                            }
                            .padding(12)
                            .background(Color.white)
                            .cornerRadius(8)
                        }

                        // Responders Section
                        VStack(alignment: .leading, spacing: 12) {
                            HStack {
                                Text("Responders")
                                    .font(.subheadline)
                                    .fontWeight(.semibold)
                                Spacer()
                                Text("\(event.responderCount ?? 0)")
                                    .font(.caption)
                                    .fontWeight(.semibold)
                                    .padding(.horizontal, 8)
                                    .padding(.vertical, 4)
                                    .background(Color.gray.opacity(0.1))
                                    .cornerRadius(4)
                            }

                            if let count = event.responderCount, count > 0 {
                                VStack(spacing: 8) {
                                    ForEach(0..<min(count, 3), id: \.self) { _ in
                                        HStack {
                                            Circle()
                                                .fill(Color.green.opacity(0.2))
                                                .frame(width: 32, height: 32)
                                                .overlay(
                                                    Image(systemName: "person.fill")
                                                        .font(.caption)
                                                        .foregroundColor(.green)
                                                )

                                            VStack(alignment: .leading, spacing: 2) {
                                                Text("Responder")
                                                    .font(.caption)
                                                    .fontWeight(.semibold)
                                                Text("ETA: 4 minutes")
                                                    .font(.caption2)
                                                    .foregroundColor(.gray)
                                            }

                                            Spacer()

                                            Image(systemName: "checkmark.circle.fill")
                                                .foregroundColor(.green)
                                        }
                                        .padding(12)
                                        .background(Color.white)
                                        .cornerRadius(8)
                                    }

                                    if count > 3 {
                                        Text("and \(count - 3) more responder\(count - 3 == 1 ? "" : "s")")
                                            .font(.caption)
                                            .foregroundColor(.gray)
                                            .padding(12)
                                            .background(Color.white)
                                            .cornerRadius(8)
                                    }
                                }
                            } else {
                                VStack(spacing: 8) {
                                    Image(systemName: "person.slash")
                                        .font(.title2)
                                        .foregroundColor(.gray)
                                    Text("No responders assigned")
                                        .font(.caption)
                                        .foregroundColor(.gray)
                                }
                                .frame(maxWidth: .infinity)
                                .padding(24)
                                .background(Color.white)
                                .cornerRadius(8)
                            }
                        }

                        // Actions Section
                        VStack(spacing: 12) {
                            if event.status == "active" {
                                Button(action: {}) {
                                    HStack {
                                        Image(systemName: "exclamationmark.circle.fill")
                                        Text("Update Status")
                                    }
                                    .frame(maxWidth: .infinity)
                                    .padding(12)
                                    .background(Color.orange.opacity(0.1))
                                    .foregroundColor(.orange)
                                    .cornerRadius(8)
                                }
                                .accessibilityLabel("Update event status")
                            }

                            Button(action: {}) {
                                HStack {
                                    Image(systemName: "square.and.arrow.up")
                                    Text("Share Event")
                                }
                                .frame(maxWidth: .infinity)
                                .padding(12)
                                .background(Color.blue.opacity(0.1))
                                .foregroundColor(.blue)
                                .cornerRadius(8)
                            }
                            .accessibilityLabel("Share event details")
                        }

                        Spacer()
                            .frame(height: 32)
                    }
                    .padding(12)
                }
            }
        }
    }
}

struct DetailRow: View {
    let label: String
    let value: String

    var body: some View {
        HStack {
            Text(label)
                .font(.caption)
                .foregroundColor(.gray)
            Spacer()
            Text(value)
                .font(.caption)
                .fontWeight(.semibold)
        }
    }
}

#Preview {
    HistoryDetailView(
        event: HistoryEvent(
            id: UUID(),
            eventType: "Medical Emergency",
            description: "Severe chest pain reported",
            severity: .high,
            status: "resolved",
            startTime: Date(),
            endTime: Date().addingTimeInterval(300),
            responderCount: 2
        )
    )
}
