/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         DataExportPort.kt                                      │
 * │ Purpose:      Hexagonal port interface for GDPR Article 20 data      │
 * │               portability. Exports all user data as structured JSON.  │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: kotlinx.coroutines, kotlinx.serialization              │
 * │                                                                      │
 * │ Adapter tiers:                                                       │
 * │   - Mock:   Returns synthetic sample export. Dev/test.               │
 * │   - Native: Queries Room DB + local stores, assembles JSON.          │
 * │   - Live:   Native + pulls server-side data for completeness.        │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   val port: DataExportPort = hiltGet()                               │
 * │   val result = port.exportAllUserData("user-001")                    │
 * │   if (result.isSuccess) shareJsonViaIntent(result.getOrThrow())      │
 * │                                                                      │
 * │ Regulatory: GDPR Art.20, CCPA 1798.100, LGPD Art.18(V), PIPA Art.4  │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.gdpr

sealed class DataExportStatus {
    data class Preparing(val progressPercent: Int) : DataExportStatus()
    data class Complete(val jsonPayload: String, val sizeBytes: Long) : DataExportStatus()
    data class Failed(val reason: String, val retryable: Boolean) : DataExportStatus()
}

enum class DataCategory {
    PROFILE, EMERGENCY_CONTACTS, INCIDENT_HISTORY, LOCATION_LOGS,
    CONSENT_RECORDS, EULA_ACCEPTANCE_HISTORY, VOLUNTEER_DATA,
    DEVICE_REGISTRATIONS, NOTIFICATION_PREFERENCES,
    BIOMETRIC_ENROLLMENT_METADATA, PHRASE_DETECTION_CONFIG,
    SOS_CONFIGURATION, RESPONDER_INTERACTIONS
}

data class ExportAuditRecord(
    val exportId: String, val userId: String, val requestedAt: Long,
    val completedAt: Long?, val categories: Set<DataCategory>,
    val format: String = "application/json", val sizeBytes: Long = 0,
    val deliveryMethod: String = "in-app-download"
)

interface DataExportPort {
    suspend fun exportAllUserData(
        userId: String, categories: Set<DataCategory> = DataCategory.entries.toSet()
    ): Result<String>

    suspend fun exportWithProgress(
        userId: String, categories: Set<DataCategory> = DataCategory.entries.toSet()
    ): kotlinx.coroutines.flow.Flow<DataExportStatus>

    suspend fun getStoredCategories(userId: String): Set<DataCategory>
    suspend fun recordExportAudit(record: ExportAuditRecord)
    suspend fun getExportHistory(userId: String): List<ExportAuditRecord>
}
