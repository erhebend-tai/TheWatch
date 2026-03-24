import SwiftData
import Foundation

@MainActor
class PersistenceController {
    static let shared = PersistenceController()
    
    let modelContainer: ModelContainer
    var context: ModelContext {
        return modelContainer.mainContext
    }
    
    init(isPreview: Bool = false) {
        let schema = Schema([
            HistoryEvent.self,
            User.self,
            EmergencyContact.self,
            SyncLogEntry.self
        ])
        
        let modelConfiguration = ModelConfiguration(
            schema: schema,
            isStoredInMemoryOnly: isPreview,
            cloudKitDatabase: .private
        )
        
        do {
            self.modelContainer = try ModelContainer(
                for: schema,
                configurations: [modelConfiguration]
            )
        } catch {
            fatalError("Could not initialize ModelContainer: \(error)")
        }
    }
    
    // MARK: - HistoryEvent Operations
    func fetchHistoryEvents(
        filterType: AlertType? = nil,
        filterStatus: AlertStatus? = nil,
        sortByDate: Bool = true
    ) throws -> [HistoryEvent] {
        var predicate: Predicate<HistoryEvent>? = nil
        
        if let type = filterType, let status = filterStatus {
            predicate = #Predicate<HistoryEvent> { event in
                event.type == type && event.status == status
            }
        } else if let type = filterType {
            predicate = #Predicate<HistoryEvent> { event in
                event.type == type
            }
        } else if let status = filterStatus {
            predicate = #Predicate<HistoryEvent> { event in
                event.status == status
            }
        }
        
        var fetchDescriptor = FetchDescriptor<HistoryEvent>(predicate: predicate)
        if sortByDate {
            fetchDescriptor.sortBy = [SortDescriptor(\.timestamp, order: .reverse)]
        }
        
        return try context.fetch(fetchDescriptor)
    }
    
    func addHistoryEvent(_ event: HistoryEvent) throws {
        context.insert(event)
        try context.save()
    }
    
    func deleteHistoryEvent(_ event: HistoryEvent) throws {
        context.delete(event)
        try context.save()
    }
    
    // MARK: - User Operations
    func fetchUser() throws -> User? {
        let descriptor = FetchDescriptor<User>()
        let users = try context.fetch(descriptor)
        return users.first
    }
    
    func saveUser(_ user: User) throws {
        if let existingUser = try fetchUser() {
            context.delete(existingUser)
        }
        context.insert(user)
        try context.save()
    }
    
    // MARK: - EmergencyContact Operations
    func fetchEmergencyContacts() throws -> [EmergencyContact] {
        let descriptor = FetchDescriptor<EmergencyContact>()
        let contacts = try context.fetch(descriptor)
        return contacts.sorted { $0.priority < $1.priority }
    }
    
    func addEmergencyContact(_ contact: EmergencyContact) throws {
        context.insert(contact)
        try context.save()
    }
    
    func deleteEmergencyContact(_ contact: EmergencyContact) throws {
        context.delete(contact)
        try context.save()
    }
    
    // MARK: - Sync Operations
    func fetchSyncQueue() throws -> [SyncLogEntry] {
        let predicate = #Predicate<SyncLogEntry> { entry in
            entry.status == .pending
        }
        let descriptor = FetchDescriptor<SyncLogEntry>(predicate: predicate)
        return try context.fetch(descriptor)
    }
    
    func addSyncLogEntry(_ entry: SyncLogEntry) throws {
        context.insert(entry)
        try context.save()
    }
    
    func markSyncEntryAsCompleted(_ entry: SyncLogEntry) throws {
        entry.status = .completed
        entry.completedAt = Date()
        try context.save()
    }
    
    func clearOldSyncLogs(olderThan days: Int = 7) throws {
        let threshold = Date().addingTimeInterval(-Double(days) * 24 * 60 * 60)
        let predicate = #Predicate<SyncLogEntry> { entry in
            entry.completedAt ?? Date.distantPast < threshold
        }
        let descriptor = FetchDescriptor<SyncLogEntry>(predicate: predicate)
        let oldEntries = try context.fetch(descriptor)
        
        for entry in oldEntries {
            context.delete(entry)
        }
        
        try context.save()
    }
}
