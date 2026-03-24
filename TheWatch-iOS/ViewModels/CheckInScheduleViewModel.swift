// ============================================================================
// WRITE-AHEAD LOG
// ============================================================================
// File:    ViewModels/CheckInScheduleViewModel.swift
// Purpose: ViewModel for the CheckInScheduleView. Manages check-in frequency
//          selection, custom interval input, schedule enable/disable, and
//          communicates with CheckInScheduleService for persistence.
// Date:    2026-03-24
// Author:  Claude (Anthropic)
// Deps:    Foundation, Services/CheckInScheduleService
// Usage:
//   let vm = CheckInScheduleViewModel(service: MockCheckInScheduleService())
//   await vm.loadSchedule()
//   vm.selectedFrequency = .every6Hours
//   await vm.saveSchedule()
// ============================================================================

import Foundation

@Observable
final class CheckInScheduleViewModel {
    // MARK: - Published State
    var selectedFrequency: CheckInFrequency = .daily
    var customIntervalMinutes: Int = 60
    var isEnabled: Bool = true
    var graceWindowMinutes: Int = 15
    var notifyContactsOnMiss: Bool = true
    var lastCheckIn: Date?
    var nextCheckIn: Date?
    var missedCount: Int = 0
    var isLoading: Bool = false
    var errorMessage: String?

    // MARK: - Dependencies
    private let service: CheckInScheduleServiceProtocol
    private let userId: String

    // MARK: - Computed
    var nextCheckInDisplay: String {
        guard let next = nextCheckIn else { return "Not scheduled" }
        let formatter = RelativeDateTimeFormatter()
        formatter.unitsStyle = .abbreviated
        return formatter.localizedString(for: next, relativeTo: Date())
    }

    var lastCheckInDisplay: String {
        guard let last = lastCheckIn else { return "Never" }
        let formatter = RelativeDateTimeFormatter()
        formatter.unitsStyle = .abbreviated
        return formatter.localizedString(for: last, relativeTo: Date())
    }

    var customIntervalHours: Double {
        get { Double(customIntervalMinutes) / 60.0 }
        set { customIntervalMinutes = Int(newValue * 60) }
    }

    // MARK: - Init
    init(service: CheckInScheduleServiceProtocol, userId: String = "current-user") {
        self.service = service
        self.userId = userId
    }

    // MARK: - Actions
    func loadSchedule() async {
        isLoading = true
        errorMessage = nil
        do {
            if let schedule = try await service.getSchedule(userId: userId) {
                selectedFrequency = schedule.frequency
                customIntervalMinutes = schedule.customIntervalMinutes ?? 60
                isEnabled = schedule.isEnabled
                graceWindowMinutes = schedule.graceWindowMinutes
                notifyContactsOnMiss = schedule.notifyContactsOnMiss
                lastCheckIn = schedule.lastCheckIn
                nextCheckIn = schedule.nextCheckIn
                missedCount = schedule.missedCount
            }
        } catch {
            errorMessage = "Failed to load schedule: \(error.localizedDescription)"
        }
        isLoading = false
    }

    func saveSchedule() async {
        isLoading = true
        errorMessage = nil
        do {
            let customMins = selectedFrequency == .custom ? customIntervalMinutes : nil
            try await service.setSchedule(
                userId: userId,
                frequency: selectedFrequency,
                customMinutes: customMins
            )
            try await service.setEnabled(userId: userId, enabled: isEnabled)
            // Reload to get computed nextCheckIn
            await loadSchedule()
        } catch {
            errorMessage = "Failed to save schedule: \(error.localizedDescription)"
        }
        isLoading = false
    }

    func checkInNow() async {
        isLoading = true
        errorMessage = nil
        do {
            try await service.recordCheckIn(userId: userId)
            lastCheckIn = Date()
            missedCount = 0
            await loadSchedule()
        } catch {
            errorMessage = "Failed to record check-in: \(error.localizedDescription)"
        }
        isLoading = false
    }

    func toggleEnabled() async {
        isEnabled.toggle()
        do {
            try await service.setEnabled(userId: userId, enabled: isEnabled)
        } catch {
            isEnabled.toggle() // revert
            errorMessage = "Failed to toggle schedule: \(error.localizedDescription)"
        }
    }
}
