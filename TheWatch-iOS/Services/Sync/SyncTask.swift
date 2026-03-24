/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         SyncTask.swift                                         │
 * │ Purpose:      Data model for offline sync queue tasks. Mirrors the   │
 * │               Android SyncTaskEntity. Persisted to UserDefaults      │
 * │               (dev) or Core Data / SwiftData (production) for        │
 * │               crash resilience.                                      │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: Foundation                                             │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   let task = SyncTask(                                               │
 * │       entityType: .sosEvent,                                         │
 * │       entityId: "sos-123",                                           │
 * │       action: .create,                                               │
 * │       payload: sosJSON,                                              │
 * │       priority: .critical                                            │
 * │   )                                                                  │
 * │   await syncEngine.enqueue(task)                                     │
 * │                                                                      │
 * │ Collection mapping (mirrors Android SyncDispatcher):                 │
 * │   .sosEvent       -> "sos_events"                                    │
 * │   .volunteer      -> "volunteers"                                    │
 * │   .contact        -> "contacts"                                      │
 * │   .device         -> "devices"                                       │
 * │   .evidence       -> "evidence"                                      │
 * │   .location       -> "locations"                                     │
 * │   .checkIn        -> "check_ins"                                     │
 * │   .sitrep         -> "sitreps"                                       │
 * │   .profile        -> "profiles"                                      │
 * │   .healthData     -> "health_data"                                   │
 * │   .geofence       -> "geofences"                                     │
 * │   .bleRelay       -> "ble_relays"                                    │
 * │   .logEntry       -> "logs"                                          │
 * │   .guardianConsent -> "guardian_consents"                             │
 * │   .escalation     -> "escalations"                                   │
 * │                                                                      │
 * │ NOTE: Codable conformance enables JSON serialization for persistence │
 * │ and debugging. Consider migrating to SwiftData @Model for production │
 * │ (iOS 17+) or Core Data NSManagedObject for iOS 15+ support.         │
 * └──────────────────────────────────────────────────────────────────────┘
 */

import Foundation

// MARK: - Entity Types

/// All entity types that participate in the generalized sync engine.
enum SyncEntityType: String, Codable, CaseIterable {
    case sosEvent = "SOS_EVENT"
    case volunteer = "VOLUNTEER"
    case contact = "CONTACT"
    case device = "DEVICE"
    case evidence = "EVIDENCE"
    case location = "LOCATION"
    case checkIn = "CHECK_IN"
    case sitrep = "SITREP"
    case profile = "PROFILE"
    case healthData = "HEALTH_DATA"
    case geofence = "GEOFENCE"
    case bleRelay = "BLE_RELAY"
    case logEntry = "LOG_ENTRY"
    case guardianConsent = "GUARDIAN_CONSENT"
    case escalation = "ESCALATION"

    /// Firestore collection name for this entity type.
    var collectionName: String {
        switch self {
        case .sosEvent: return "sos_events"
        case .volunteer: return "volunteers"
        case .contact: return "contacts"
        case .device: return "devices"
        case .evidence: return "evidence"
        case .location: return "locations"
        case .checkIn: return "check_ins"
        case .sitrep: return "sitreps"
        case .profile: return "profiles"
        case .healthData: return "health_data"
        case .geofence: return "geofences"
        case .bleRelay: return "ble_relays"
        case .logEntry: return "logs"
        case .guardianConsent: return "guardian_consents"
        case .escalation: return "escalations"
        }
    }
}

// MARK: - Task Action

/// CRUD action for the sync task.
enum SyncTaskAction: String, Codable {
    case create = "CREATE"
    case update = "UPDATE"
    case delete = "DELETE"
}

// MARK: - Priority

/// Priority levels. Lower raw value = higher priority.
/// SOS and evidence get critical priority to sync first on reconnect.
enum SyncPriority: Int, Codable, Comparable {
    case critical = 0   // SOS, active emergencies
    case high = 1       // Evidence, check-in responses
    case normal = 5     // Profile updates, volunteer status
    case low = 10       // Logs, health data, analytics

    static func < (lhs: SyncPriority, rhs: SyncPriority) -> Bool {
        lhs.rawValue < rhs.rawValue
    }
}

// MARK: - Task Status

/// Processing state of a sync task.
enum SyncTaskStatus: String, Codable {
    case queued = "QUEUED"
    case inProgress = "IN_PROGRESS"
    case completed = "COMPLETED"
    case failed = "FAILED"
    case deadLetter = "DEAD_LETTER"
}

// MARK: - Dispatch Result

/// Result of dispatching a sync task to the backend.
enum SyncDispatchResult {
    case success(serverId: String?)
    case retryableFailure(message: String, error: Error?)
    case permanentFailure(message: String, error: Error?)
}

// MARK: - Sync Task

/// A single queued sync operation. Persisted for crash resilience.
struct SyncTask: Codable, Identifiable, Equatable {
    let id: String
    var entityType: SyncEntityType
    var entityId: String
    var action: SyncTaskAction
    var payload: String
    var priority: SyncPriority
    var status: SyncTaskStatus
    var retryCount: Int
    var maxRetries: Int
    var createdAt: Date
    var lastAttemptAt: Date?
    var lastError: String?
    var userId: String
    var idempotencyKey: String

    init(
        id: String = UUID().uuidString,
        entityType: SyncEntityType,
        entityId: String,
        action: SyncTaskAction = .create,
        payload: String = "{}",
        priority: SyncPriority = .normal,
        status: SyncTaskStatus = .queued,
        retryCount: Int = 0,
        maxRetries: Int = 5,
        createdAt: Date = Date(),
        lastAttemptAt: Date? = nil,
        lastError: String? = nil,
        userId: String = "",
        idempotencyKey: String? = nil
    ) {
        self.id = id
        self.entityType = entityType
        self.entityId = entityId
        self.action = action
        self.payload = payload
        self.priority = priority
        self.status = status
        self.retryCount = retryCount
        self.maxRetries = maxRetries
        self.createdAt = createdAt
        self.lastAttemptAt = lastAttemptAt
        self.lastError = lastError
        self.userId = userId
        self.idempotencyKey = idempotencyKey ?? id
    }
}
