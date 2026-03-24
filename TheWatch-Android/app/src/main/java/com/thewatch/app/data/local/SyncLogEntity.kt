/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         SyncLogEntity.kt                                       │
 * │ Purpose:      Room entity for offline sync queue. Stores pending     │
 * │               operations (location updates, evidence, SOS alerts)    │
 * │               as JSON payloads flushed when connectivity returns.    │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: Room, kotlinx.serialization                            │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   val entry = SyncLogEntity(                                         │
 * │       id = UUID.randomUUID().toString(),                             │
 * │       action = SyncAction.LOCATION_UPDATE.name,                      │
 * │       payload = Json.encodeToString(locationData),                   │
 * │       status = SyncStatus.PENDING.name,                              │
 * │       createdAt = System.currentTimeMillis()                         │
 * │   )                                                                  │
 * │   syncLogDao.insertLog(entry)                                        │
 * │                                                                      │
 * │ NOTE: payload stored as raw JSON. For evidence uploads, store the    │
 * │ local file URI in payload, not binary data.                          │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.local

import androidx.room.Entity
import androidx.room.Index
import androidx.room.PrimaryKey
import kotlinx.serialization.Serializable

/**
 * Sync operation types that can be queued offline.
 */
enum class SyncAction {
    LOCATION_UPDATE,
    EVIDENCE_UPLOAD,
    ALERT_SOS,
    CHECK_IN_RESPONSE,
    SITREP_SUBMISSION,
    PROFILE_UPDATE,
    VOLUNTEER_STATUS,
    HEALTH_DATA_BATCH,
    GEOFENCE_EVENT,
    BLE_MESH_RELAY
}

/**
 * Status of a queued sync operation.
 */
enum class SyncStatus {
    PENDING,
    IN_PROGRESS,
    COMPLETED,
    FAILED
}

@Serializable
@Entity(
    tableName = "sync_logs",
    indices = [
        Index(value = ["status", "createdAt"]),
        Index(value = ["action"]),
        Index(value = ["userId"])
    ]
)
data class SyncLogEntity(
    @PrimaryKey
    val id: String,
    val userId: String = "",
    val action: String = SyncAction.LOCATION_UPDATE.name,
    val payload: String = "{}",
    val status: String = SyncStatus.PENDING.name,
    val retryCount: Int = 0,
    val createdAt: Long = System.currentTimeMillis(),
    val lastAttemptAt: Long? = null,
    val lastError: String? = null,
    val priority: Int = 5,
    // Legacy fields for backward compat
    val synced: Boolean = false,
    val syncAttempts: Int = 0,
    val eventType: String = "",
    val timestamp: Long = System.currentTimeMillis()
)
