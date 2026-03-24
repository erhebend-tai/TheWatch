// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:    Views/CheckIn/CheckInScheduleView.swift
// Purpose: SwiftUI view for configuring check-in schedules. Users select
//          frequency (daily/12h/6h/custom), see next/last check-in times,
//          perform instant check-in, and configure grace windows.
//          BGTaskScheduler delivers reminders even when app is backgrounded.
// Date:    2026-03-24
// Author:  Claude (Anthropic)
// Deps:    SwiftUI, ViewModels/CheckInScheduleViewModel, Services/CheckInScheduleService
// Usage:
//   CheckInScheduleView()
//     // Presented from SettingsView or NavigationDrawer
// ============================================================================

import SwiftUI

struct CheckInScheduleView: View {
    @State private var viewModel: CheckInScheduleViewModel
    @Environment(\.dismiss) var dismiss
    @Environment(\.colorScheme) var colorScheme

    init(service: CheckInScheduleServiceProtocol = MockCheckInScheduleService()) {
        _viewModel = State(initialValue: CheckInScheduleViewModel(service: service))
    }

    var body: some View {
        ZStack {
            Color.lightGray
                .ignoresSafeArea()

            VStack(spacing: 0) {
                // Header
                HStack {
                    Button(action: { dismiss() }) {
                        HStack(spacing: 4) {
                            Image(systemName: "chevron.left")
                            Text("Back")
                        }
                        .foregroundColor(.primaryRed)
                    }
                    Spacer()
                    Text("Check-In Schedule")
                        .font(.headline)
                        .fontWeight(.bold)
                    Spacer()
                    // Balance spacer
                    Color.clear.frame(width: 60)
                }
                .padding(16)
                .background(Color.white)

                Divider()

                ScrollView {
                    VStack(spacing: 20) {
                        // Quick Check-In Button
                        Button(action: {
                            Task { await viewModel.checkInNow() }
                        }) {
                            HStack(spacing: 12) {
                                Image(systemName: "checkmark.circle.fill")
                                    .font(.title2)
                                Text("Check In Now")
                                    .font(.headline)
                                    .fontWeight(.bold)
                            }
                            .frame(maxWidth: .infinity)
                            .padding(16)
                            .background(Color.statusSafe)
                            .foregroundColor(.white)
                            .cornerRadius(12)
                        }
                        .accessibilityLabel("Record check-in now")
                        .padding(.horizontal, 16)

                        // Status Card
                        VStack(spacing: 12) {
                            HStack {
                                Text("Schedule Status")
                                    .font(.subheadline)
                                    .fontWeight(.semibold)
                                Spacer()
                                Toggle("", isOn: Binding(
                                    get: { viewModel.isEnabled },
                                    set: { _ in Task { await viewModel.toggleEnabled() } }
                                ))
                                .accessibilityLabel("Enable check-in schedule")
                            }

                            Divider()

                            HStack {
                                VStack(alignment: .leading, spacing: 4) {
                                    Text("Last Check-In")
                                        .font(.caption)
                                        .foregroundColor(.gray)
                                    Text(viewModel.lastCheckInDisplay)
                                        .font(.subheadline)
                                        .fontWeight(.semibold)
                                }
                                Spacer()
                                VStack(alignment: .trailing, spacing: 4) {
                                    Text("Next Check-In")
                                        .font(.caption)
                                        .foregroundColor(.gray)
                                    Text(viewModel.nextCheckInDisplay)
                                        .font(.subheadline)
                                        .fontWeight(.semibold)
                                }
                            }

                            if viewModel.missedCount > 0 {
                                HStack(spacing: 8) {
                                    Image(systemName: "exclamationmark.triangle.fill")
                                        .foregroundColor(.statusWarning)
                                    Text("\(viewModel.missedCount) missed check-in(s)")
                                        .font(.caption)
                                        .foregroundColor(.statusWarning)
                                    Spacer()
                                }
                                .padding(8)
                                .background(Color.statusWarning.opacity(0.1))
                                .cornerRadius(6)
                            }
                        }
                        .padding(16)
                        .background(Color.white)
                        .cornerRadius(8)
                        .padding(.horizontal, 16)

                        // Frequency Selection
                        VStack(spacing: 12) {
                            HStack {
                                Text("Frequency")
                                    .font(.subheadline)
                                    .fontWeight(.semibold)
                                Spacer()
                            }

                            ForEach(CheckInFrequency.allCases) { freq in
                                Button(action: {
                                    viewModel.selectedFrequency = freq
                                    Task { await viewModel.saveSchedule() }
                                }) {
                                    HStack(spacing: 12) {
                                        Image(systemName: freq.icon)
                                            .foregroundColor(.primaryRed)
                                            .frame(width: 24)

                                        Text(freq.displayName)
                                            .foregroundColor(.primary)

                                        Spacer()

                                        if viewModel.selectedFrequency == freq {
                                            Image(systemName: "checkmark.circle.fill")
                                                .foregroundColor(.primaryRed)
                                        } else {
                                            Image(systemName: "circle")
                                                .foregroundColor(.gray)
                                        }
                                    }
                                    .padding(12)
                                    .background(
                                        viewModel.selectedFrequency == freq
                                            ? Color.primaryRed.opacity(0.1)
                                            : Color.white
                                    )
                                    .cornerRadius(8)
                                }
                                .accessibilityLabel("Set frequency to \(freq.displayName)")
                            }

                            // Custom interval slider
                            if viewModel.selectedFrequency == .custom {
                                VStack(alignment: .leading, spacing: 8) {
                                    Text("Custom Interval")
                                        .font(.caption)
                                        .fontWeight(.semibold)

                                    HStack(spacing: 12) {
                                        Slider(
                                            value: Binding(
                                                get: { viewModel.customIntervalHours },
                                                set: { viewModel.customIntervalHours = $0 }
                                            ),
                                            in: 1...48,
                                            step: 0.5
                                        )
                                        .accessibilityLabel("Custom interval hours")

                                        Text(String(format: "%.1fh", viewModel.customIntervalHours))
                                            .font(.caption)
                                            .fontWeight(.semibold)
                                            .frame(width: 45)
                                    }

                                    Button(action: {
                                        Task { await viewModel.saveSchedule() }
                                    }) {
                                        Text("Apply Custom Interval")
                                            .frame(maxWidth: .infinity)
                                            .padding(10)
                                            .background(Color.primaryRed)
                                            .foregroundColor(.white)
                                            .cornerRadius(8)
                                            .font(.subheadline)
                                    }
                                    .accessibilityLabel("Apply custom check-in interval")
                                }
                                .padding(12)
                                .background(Color.white)
                                .cornerRadius(8)
                            }
                        }
                        .padding(16)
                        .background(Color.white)
                        .cornerRadius(8)
                        .padding(.horizontal, 16)

                        // Grace Window & Notifications
                        VStack(spacing: 12) {
                            HStack {
                                Text("Safety Options")
                                    .font(.subheadline)
                                    .fontWeight(.semibold)
                                Spacer()
                            }

                            VStack(alignment: .leading, spacing: 8) {
                                Text("Grace Window")
                                    .font(.caption)
                                    .fontWeight(.semibold)
                                HStack(spacing: 12) {
                                    Slider(
                                        value: Binding(
                                            get: { Double(viewModel.graceWindowMinutes) },
                                            set: { viewModel.graceWindowMinutes = Int($0) }
                                        ),
                                        in: 5...60,
                                        step: 5
                                    )
                                    .accessibilityLabel("Grace window minutes")

                                    Text("\(viewModel.graceWindowMinutes) min")
                                        .font(.caption)
                                        .fontWeight(.semibold)
                                        .frame(width: 50)
                                }
                                Text("Extra time before contacts are notified of a missed check-in")
                                    .font(.caption2)
                                    .foregroundColor(.gray)
                            }

                            Divider()

                            HStack {
                                VStack(alignment: .leading, spacing: 4) {
                                    Text("Notify Contacts on Miss")
                                        .font(.subheadline)
                                        .fontWeight(.semibold)
                                    Text("Alert emergency contacts when you miss a check-in")
                                        .font(.caption)
                                        .foregroundColor(.gray)
                                }
                                Spacer()
                                Toggle("", isOn: $viewModel.notifyContactsOnMiss)
                                    .accessibilityLabel("Notify contacts on missed check-in")
                            }
                        }
                        .padding(16)
                        .background(Color.white)
                        .cornerRadius(8)
                        .padding(.horizontal, 16)

                        // Error display
                        if let error = viewModel.errorMessage {
                            Text(error)
                                .font(.caption)
                                .foregroundColor(.red)
                                .padding(.horizontal, 16)
                        }

                        Spacer().frame(height: 20)
                    }
                    .padding(.vertical, 16)
                }
            }
        }
        .task {
            await viewModel.loadSchedule()
        }
    }
}

#Preview {
    CheckInScheduleView()
}
