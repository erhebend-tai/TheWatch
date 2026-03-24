/**
 * +----------------------------------------------------------------------+
 * | WRITE-AHEAD LOG                                                       |
 * +----------------------------------------------------------------------+
 * | File:         SyncTaskEntity.kt                                       |
 * | Purpose:      Room entity for the generalized offline sync queue.     |
 * |               Each row represents a single pending Create/Update/     |
 * |               Delete operation for any entity type (SOS events,       |
 * |               volunteer profiles, contact lists, device registrations,|
 * |               evidence uploads, location updates, etc.).              |
 * | Created:      2026-03-24                                              |
 * | Author:       Claude                                                  |
 * | Dependencies: Room                                                    |
 * |                                                                       |
 * | Usage example:                                                        |
 * |   val task = SyncTaskEntity(                                          |
 * |       id = UUID.randomUUID().toString(),                              |
 * |       entityType = SyncEntityType.SOS_EVENT,                          |
 * |       entityId = "sos-123",                                           |
 * |       action = SyncAction.CREATE,                                     |
 * |       payload = Json.encodeToString(sosData),                         |
 * |       priority = SyncPriority.CRITICAL                                |
 * |   )                                                                   |
 * |   syncTaskDao.insert(task)                                            |
 * |                                                                       |
 * | Conflict resolution: Last-write-wins using serverTimestamp.           |
 * | The SyncDispatcher stamps each write with FieldValue.serverTimestamp()|
 * | so Firestore rules can reject stale writes if needed.                 |
 * |                                                                       |
 * | NOTE: payload is raw JSON. For binary data (evidence photos/video),   |
 * | store the local file URI in payload, not the binary content.          |
 * | Consider adding a TTL field for time-sensitive tasks (e.g., location  |
 * | updates older than 1 hour may be irrelevant by the time they sync).   |
 * +----------------------------------------------------------------------+
 */
package com.thewatch.app.data.sync

import androidx.room.Entity
import androidx.room.Index
import androidx.room.PrimaryKey

/**
 * Entity types that can be synced through the generalized engine.
 * Each type maps to a specific Firestore collection in SyncDispatcher.
 *
 * Mapping:
 *   SOS_EVENT       -> "sos_events"
 *   VOLUNTEER       -> "volunteers"
 *   CONTACT         -> "contacts"
 *   DEVICE          -> "devices"
 *   EVIDENCE        -> "evidence"
 *   LOCATION        -> "locations"
 *   CHECK_IN        -> "check_ins"
 *   SITREP          -> "sitreps"
 *   PROFILE         -> "profiles"
 *   HEALTH_DATA     -> "health_data"
 *   GEOFENCE        -> "geofences"
 *   BLE_RELAY       -> "ble_relays"
 *   LOG_ENTRY       -> "logs" (wraps existing LogSync)
 *   GUARDIAN_CONSENT -> "guardian_consents"
 *   ESCALATION      -> "escalations"
 */
enum class SyncEntityType {
    SOS_EVENT,
    VOLUNTEER,
    CONTACT,
    DEVICE,
    EVIDENCE,
    LOCATION,
    CHECK_IN,
    SITREP,
    PROFILE,
    HEALTH_DATA,
    GEOFENCE,
    BLE_RELAY,
    LOG_ENTRY,
    GUARDIAN_CONSENT,
    ESCALATION
}

/**
 * CRUD action for the sync task.
 */
enum class SyncTaskAction {
    CREATE,
    UPDATE,
    DELETE
}

/**
 * Priority levels. Lower numeric value = higher priority.
 * SOS and evidence uploads get critical priority to ensure
 * they sync first when connectivity returns.
 *
 * Numeric mapping:
 *   CRITICAL = 0   (SOS, active emergencies)
 *   HIGH     = 1   (evidence, check-in responses)
 *   NORMAL   = 5   (profile updates, volunteer status)
 *   LOW      = 10  (logs, health data batches, analytics)
 */
object SyncPriority {
    const val CRITICAL = 0
    const val HIGH = 1
    const val NORMAL = 5
    const val LOW = 10
}

/**
 * Processing state of a sync task.
 */
enum class SyncTaskStatus {
    QUEUED,
    IN_PROGRESS,
    COMPLETED,
    FAILED,
    DEAD_LETTER
}

@Entity(
    tableName = "sync_tasks",
    indices = [
        Index(value = ["status", "priority", "createdAt"]),
        Index(value = ["entityType", "entityId"]),
        Index(value = ["status"]),
        Index(value = ["entityType"])
    ]
)
data class SyncTaskEntity(
    @PrimaryKey
    val id: String,

    /** What kind of entity is being synced. */
    val entityType: SyncEntityType = SyncEntityType.LOG_ENTRY,

    /** The unique ID of the entity (e.g., SOS event ID, volunteer ID). */
    val entityId: String = "",

    /** Create / Update / Delete. */
    val action: SyncTaskAction = SyncTaskAction.CREATE,

    /** JSON-serialized payload. For deletes, may contain only the entity ID. */
    val payload: String = "{}",

    /** Lower = higher priority. See [SyncPriority]. */
    val priority: Int = SyncPriority.NORMAL,

    /** Current processing state. */
    val status: SyncTaskStatus = SyncTaskStatus.QUEUED,

    /** Number of failed push attempts. */
    val retryCount: Int = 0,

    /** Max retries before moving to DEAD_LETTER. Default 5. */
    val maxRetries: Int = 5,

    /** Epoch millis when the task was enqueued. */
    val createdAt: Long = System.currentTimeMillis(),

    /** Epoch millis of last push attempt, null if never attempted. */
    val lastAttemptAt: Long? = null,

    /** Error message from the last failed attempt. */
    val lastError: String? = null,

    /** User ID that owns this entity, for multi-user queue partitioning. */
    val userId: String = "",

    /**
     * Idempotency key. SyncDispatcher uses this to deduplicate on the server.
     * Defaults to the task ID itself, but callers can set a domain-specific key
     * (e.g., "location-update-{userId}-{timestamp}") for natural dedup.
     */
    val idempotencyKey: String = ""
)
