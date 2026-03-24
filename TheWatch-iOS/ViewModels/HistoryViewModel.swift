import Foundation

@Observable
final class HistoryViewModel {
    var events: [HistoryEvent] = []
    var isLoading = false
    var errorMessage: String?

    // Filtering
    var selectedEventTypes: Set<String> = []
    var selectedSeverities: Set<AlertSeverity> = []
    var selectedStatuses: Set<AlertStatus> = []
    var dateRange: ClosedRange<Date> = Date(timeIntervalSinceNow: -30 * 86400)...Date()

    private let historyService: MockHistoryService

    init(historyService: MockHistoryService) {
        self.historyService = historyService
    }

    func loadHistory(userId: String) async {
        isLoading = true
        errorMessage = nil

        do {
            self.events = try await historyService.getHistory(userId: userId, limit: 50, offset: 0)
        } catch {
            errorMessage = "Failed to load history"
        }

        isLoading = false
    }

    func applyFilters(userId: String) async {
        isLoading = true
        errorMessage = nil

        do {
            let severities = selectedSeverities.isEmpty ? AlertSeverity.allCases : Array(selectedSeverities)
            let statuses = selectedStatuses.isEmpty ? AlertStatus.allCases : Array(selectedStatuses)
            let eventTypes = Array(selectedEventTypes)

            self.events = try await historyService.filterHistory(
                userId: userId,
                dateRange: dateRange,
                eventTypes: eventTypes,
                severities: severities,
                statuses: statuses
            )
        } catch {
            errorMessage = "Failed to apply filters"
        }

        isLoading = false
    }

    func resetFilters() {
        selectedEventTypes.removeAll()
        selectedSeverities.removeAll()
        selectedStatuses.removeAll()
        dateRange = Date(timeIntervalSinceNow: -30 * 86400)...Date()
    }

    func toggleEventTypeFilter(_ type: String) {
        if selectedEventTypes.contains(type) {
            selectedEventTypes.remove(type)
        } else {
            selectedEventTypes.insert(type)
        }
    }

    func toggleSeverityFilter(_ severity: AlertSeverity) {
        if selectedSeverities.contains(severity) {
            selectedSeverities.remove(severity)
        } else {
            selectedSeverities.insert(severity)
        }
    }

    func toggleStatusFilter(_ status: AlertStatus) {
        if selectedStatuses.contains(status) {
            selectedStatuses.remove(status)
        } else {
            selectedStatuses.insert(status)
        }
    }

    var eventTypeOptions: [String] {
        ["SOS", "Safety Check", "Accident", "Wellness", "Custom"]
    }
}
