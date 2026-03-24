/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         MockDataExportAdapter.kt                               │
 * │ Purpose:      Mock adapter for DataExportPort. Returns synthetic     │
 * │               GDPR export data for development and UI tests.         │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: DataExportPort, kotlinx.coroutines                     │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   val mock = MockDataExportAdapter()                                 │
 * │   val json = mock.exportAllUserData("user-001").getOrThrow()         │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.gdpr.mock

import com.thewatch.app.data.gdpr.*
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.flow
import java.util.UUID

class MockDataExportAdapter : DataExportPort {
    private val auditRecords = mutableListOf<ExportAuditRecord>()

    override suspend fun exportAllUserData(userId: String, categories: Set<DataCategory>): Result<String> {
        delay(1500)
        return Result.success(buildMockJson(userId, categories))
    }

    override suspend fun exportWithProgress(userId: String, categories: Set<DataCategory>): Flow<DataExportStatus> = flow {
        emit(DataExportStatus.Preparing(0)); delay(500)
        emit(DataExportStatus.Preparing(30)); delay(500)
        emit(DataExportStatus.Preparing(65)); delay(500)
        emit(DataExportStatus.Preparing(90)); delay(300)
        val json = buildMockJson(userId, categories)
        emit(DataExportStatus.Complete(json, json.toByteArray().size.toLong()))
    }

    override suspend fun getStoredCategories(userId: String): Set<DataCategory> {
        delay(200); return DataCategory.entries.toSet()
    }

    override suspend fun recordExportAudit(record: ExportAuditRecord) { auditRecords.add(record) }
    override suspend fun getExportHistory(userId: String) = auditRecords.filter { it.userId == userId }

    private fun buildMockJson(userId: String, categories: Set<DataCategory>): String {
        val sections = mutableListOf<String>()
        if (DataCategory.PROFILE in categories) sections.add(""""profile":{"userId":"$userId","email":"alex@example.com","firstName":"Alex","lastName":"Rivera","bloodType":"O+"}""")
        if (DataCategory.EMERGENCY_CONTACTS in categories) sections.add(""""emergencyContacts":[{"name":"Jordan Rivera","phone":"+1-555-0456","relationship":"Spouse"}]""")
        if (DataCategory.INCIDENT_HISTORY in categories) sections.add(""""incidentHistory":[{"id":"inc-001","eventType":"SOS","severity":"high","timestamp":"2026-03-10T14:22:00Z","status":"resolved"}]""")
        if (DataCategory.VOLUNTEER_DATA in categories) sections.add(""""volunteerData":{"isVolunteer":true,"hasCar":true,"isOver18":true}""")
        return """{"exportMetadata":{"exportId":"${UUID.randomUUID()}","userId":"$userId","format":"TheWatch GDPR Export v1.0"},${sections.joinToString(",")}}"""
    }
}
