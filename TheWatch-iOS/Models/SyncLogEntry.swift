import SwiftData
import Foundation

enum SyncStatus {
    case pending
    case inProgress
    case completed
    case failed
}

@Model
final class SyncLogEntry {
    @Attribute(.unique) var id: UUID
    var action: String
    var status: SyncStatus
    var createdAt: Date
    var completedAt: Date?
    var errorMessage: String?
    var retryCount: Int
    
    init(
        id: UUID = UUID(),
        action: String,
        status: SyncStatus = .pending,
        createdAt: Date = Date(),
        completedAt: Date? = nil,
        errorMessage: String? = nil,
        retryCount: Int = 0
    ) {
        self.id = id
        self.action = action
        self.status = status
        self.createdAt = createdAt
        self.completedAt = completedAt
        self.errorMessage = errorMessage
        self.retryCount = retryCount
    }
    
    func retry() {
        self.retryCount += 1
        self.status = .pending
        self.errorMessage = nil
    }
    
    func markAsFailed(error: String) {
        self.status = .failed
        self.errorMessage = error
    }
}
