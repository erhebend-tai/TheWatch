// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:    Views/Volunteering/AvailabilityScheduleView.swift
// Purpose: SwiftUI view displaying a weekly calendar grid where volunteers
//          mark available hours. Tap cells to toggle availability. Includes
//          "Available Now" instant toggle, designated responder enrollment,
//          and total hours summary.
// Date:    2026-03-24
// Author:  Claude (Anthropic)
// Deps:    SwiftUI, Models/AvailabilitySchedule
// Usage:
//   AvailabilityScheduleView(schedule: $schedule)
//     // Presented from VolunteeringView or ProfileView
// ============================================================================

import SwiftUI

struct AvailabilityScheduleView: View {
    @Binding var schedule: AvailabilitySchedule
    @Environment(\.dismiss) var dismiss
    @State private var selectedDay: Weekday = .monday

    /// Hours to display in the grid (0-23, but we show common waking hours prominently)
    private let displayHours = Array(0..<24)

    /// Filtered hours for the compact grid (6 AM - 11 PM)
    private let compactHours = Array(6..<23)

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
                    Text("Availability")
                        .font(.headline)
                        .fontWeight(.bold)
                    Spacer()
                    Color.clear.frame(width: 60)
                }
                .padding(16)
                .background(Color.white)

                Divider()

                ScrollView {
                    VStack(spacing: 20) {
                        // Available Now Toggle
                        HStack {
                            VStack(alignment: .leading, spacing: 4) {
                                Text("Available Now")
                                    .font(.headline)
                                    .fontWeight(.bold)
                                Text(schedule.availableNow
                                     ? "You're visible to nearby users in need"
                                     : "Toggle on to be immediately available")
                                    .font(.caption)
                                    .foregroundColor(.gray)
                            }
                            Spacer()
                            Toggle("", isOn: $schedule.availableNow)
                                .accessibilityLabel("Toggle available now")
                        }
                        .padding(16)
                        .background(
                            schedule.availableNow
                                ? Color.statusSafe.opacity(0.15)
                                : Color.white
                        )
                        .cornerRadius(12)
                        .overlay(
                            RoundedRectangle(cornerRadius: 12)
                                .stroke(
                                    schedule.availableNow ? Color.statusSafe : Color.clear,
                                    lineWidth: 2
                                )
                        )
                        .padding(.horizontal, 16)

                        // Designated Responder
                        HStack {
                            VStack(alignment: .leading, spacing: 4) {
                                Text("Designated Responder")
                                    .font(.subheadline)
                                    .fontWeight(.semibold)
                                Text("Commit to specific scheduled hours for priority dispatch")
                                    .font(.caption)
                                    .foregroundColor(.gray)
                            }
                            Spacer()
                            Toggle("", isOn: $schedule.isDesignatedResponder)
                                .accessibilityLabel("Toggle designated responder")
                        }
                        .padding(16)
                        .background(Color.white)
                        .cornerRadius(8)
                        .padding(.horizontal, 16)

                        // Summary
                        HStack(spacing: 16) {
                            VStack(spacing: 4) {
                                Text("\(schedule.totalAvailableHours)")
                                    .font(.title2)
                                    .fontWeight(.bold)
                                    .foregroundColor(.primaryRed)
                                Text("hrs/week")
                                    .font(.caption)
                                    .foregroundColor(.gray)
                            }
                            .frame(maxWidth: .infinity)
                            .padding(12)
                            .background(Color.white)
                            .cornerRadius(8)

                            VStack(spacing: 4) {
                                Image(systemName: schedule.isEffectivelyAvailable
                                      ? "checkmark.circle.fill"
                                      : "xmark.circle.fill")
                                    .font(.title2)
                                    .foregroundColor(schedule.isEffectivelyAvailable
                                                     ? .statusSafe
                                                     : .gray)
                                Text(schedule.isEffectivelyAvailable ? "Active" : "Inactive")
                                    .font(.caption)
                                    .foregroundColor(.gray)
                            }
                            .frame(maxWidth: .infinity)
                            .padding(12)
                            .background(Color.white)
                            .cornerRadius(8)
                        }
                        .padding(.horizontal, 16)

                        // Day Selector Tabs
                        ScrollView(.horizontal, showsIndicators: false) {
                            HStack(spacing: 8) {
                                ForEach(Weekday.allCases) { day in
                                    Button(action: { selectedDay = day }) {
                                        VStack(spacing: 4) {
                                            Text(day.shortName)
                                                .font(.caption)
                                                .fontWeight(.semibold)
                                            Text("\(schedule.availableHours(for: day).count)h")
                                                .font(.caption2)
                                                .foregroundColor(.gray)
                                        }
                                        .frame(width: 48, height: 48)
                                        .background(
                                            selectedDay == day
                                                ? Color.primaryRed
                                                : Color.white
                                        )
                                        .foregroundColor(selectedDay == day ? .white : .primary)
                                        .cornerRadius(8)
                                    }
                                    .accessibilityLabel("\(day.fullName), \(schedule.availableHours(for: day).count) hours scheduled")
                                }
                            }
                            .padding(.horizontal, 16)
                        }

                        // Hour Grid for Selected Day
                        VStack(spacing: 8) {
                            HStack {
                                Text(selectedDay.fullName)
                                    .font(.subheadline)
                                    .fontWeight(.semibold)
                                Spacer()

                                Button(action: {
                                    let allAvailable = schedule.availableHours(for: selectedDay).count == 24
                                    schedule.setDayAvailability(day: selectedDay, available: !allAvailable)
                                }) {
                                    Text(schedule.availableHours(for: selectedDay).count == 24 ? "Clear All" : "Select All")
                                        .font(.caption)
                                        .foregroundColor(.primaryRed)
                                }
                                .accessibilityLabel("Toggle all hours for \(selectedDay.fullName)")
                            }

                            LazyVGrid(
                                columns: Array(repeating: GridItem(.flexible(), spacing: 4), count: 6),
                                spacing: 4
                            ) {
                                ForEach(displayHours, id: \.self) { hour in
                                    let isAvail = schedule.isAvailable(day: selectedDay, hour: hour)
                                    Button(action: {
                                        schedule.setAvailable(day: selectedDay, hour: hour, available: !isAvail)
                                    }) {
                                        VStack(spacing: 2) {
                                            Text(hourLabel(hour))
                                                .font(.system(size: 10, weight: .semibold))
                                        }
                                        .frame(maxWidth: .infinity)
                                        .frame(height: 36)
                                        .background(isAvail ? Color.primaryRed.opacity(0.8) : Color.white)
                                        .foregroundColor(isAvail ? .white : .primary)
                                        .cornerRadius(6)
                                        .overlay(
                                            RoundedRectangle(cornerRadius: 6)
                                                .stroke(Color.gray.opacity(0.2), lineWidth: 0.5)
                                        )
                                    }
                                    .accessibilityLabel("\(hourLabel(hour)) \(isAvail ? "available" : "unavailable")")
                                }
                            }
                        }
                        .padding(16)
                        .background(Color.white)
                        .cornerRadius(8)
                        .padding(.horizontal, 16)

                        // Quick Presets
                        VStack(spacing: 12) {
                            HStack {
                                Text("Quick Presets")
                                    .font(.subheadline)
                                    .fontWeight(.semibold)
                                Spacer()
                            }

                            HStack(spacing: 8) {
                                presetButton("Mornings", icon: "sunrise.fill") {
                                    for day in Weekday.allCases {
                                        schedule.setRange(day: day, startHour: 6, endHour: 12, available: true)
                                    }
                                }
                                presetButton("Evenings", icon: "sunset.fill") {
                                    for day in Weekday.allCases {
                                        schedule.setRange(day: day, startHour: 17, endHour: 23, available: true)
                                    }
                                }
                                presetButton("Weekends", icon: "calendar") {
                                    schedule.setDayAvailability(day: .saturday, available: true)
                                    schedule.setDayAvailability(day: .sunday, available: true)
                                }
                            }

                            HStack(spacing: 8) {
                                presetButton("9-5 M-F", icon: "briefcase.fill") {
                                    for day in [Weekday.monday, .tuesday, .wednesday, .thursday, .friday] {
                                        schedule.setRange(day: day, startHour: 9, endHour: 17, available: true)
                                    }
                                }
                                presetButton("Clear All", icon: "trash") {
                                    for day in Weekday.allCases {
                                        schedule.setDayAvailability(day: day, available: false)
                                    }
                                }
                            }
                        }
                        .padding(16)
                        .background(Color.white)
                        .cornerRadius(8)
                        .padding(.horizontal, 16)

                        Spacer().frame(height: 20)
                    }
                    .padding(.vertical, 16)
                }
            }
        }
    }

    // MARK: - Helpers
    private func hourLabel(_ hour: Int) -> String {
        if hour == 0 { return "12 AM" }
        if hour < 12 { return "\(hour) AM" }
        if hour == 12 { return "12 PM" }
        return "\(hour - 12) PM"
    }

    private func presetButton(_ title: String, icon: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            HStack(spacing: 4) {
                Image(systemName: icon)
                    .font(.caption2)
                Text(title)
                    .font(.caption)
                    .fontWeight(.semibold)
            }
            .frame(maxWidth: .infinity)
            .padding(10)
            .background(Color.primaryRed.opacity(0.1))
            .foregroundColor(.primaryRed)
            .cornerRadius(8)
        }
        .accessibilityLabel("Apply \(title) preset")
    }
}

#Preview {
    AvailabilityScheduleView(
        schedule: .constant(AvailabilitySchedule(userId: "preview-user"))
    )
}
