/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         MockPDFExportAdapter.kt                                │
 * │ Purpose:      Mock adapter for PDFExportPort. Simulated file paths.  │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: PDFExportPort, kotlinx.coroutines                      │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   val mock = MockPDFExportAdapter()                                  │
 * │   val result = mock.generateIncidentReport("user-001")               │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.export.mock

import com.thewatch.app.data.export.*
import kotlinx.coroutines.delay
import java.security.MessageDigest
import java.util.UUID

class MockPDFExportAdapter : PDFExportPort {
    private val reports = mutableListOf<PDFExportResult>()

    override suspend fun generateIncidentReport(userId: String, config: PDFExportConfig): Result<PDFExportResult> {
        delay(2000)
        val result = PDFExportResult("/storage/emulated/0/Download/TheWatch_Report_${UUID.randomUUID().toString().take(8)}.pdf", "TheWatch_Report_$userId.pdf", 245_760, 4, hash("$userId-${System.currentTimeMillis()}"))
        reports.add(result); return Result.success(result)
    }

    override suspend fun generateSingleIncidentReport(incidentId: String, config: PDFExportConfig): Result<PDFExportResult> {
        delay(1500)
        val result = PDFExportResult("/storage/emulated/0/Download/TheWatch_Incident_$incidentId.pdf", "TheWatch_Incident_$incidentId.pdf", 61_440, 1, hash("$incidentId-${System.currentTimeMillis()}"))
        reports.add(result); return Result.success(result)
    }

    override suspend fun verifyTamperHash(filePath: String): Boolean { delay(500); return true }
    override suspend fun listGeneratedReports() = reports.toList()
    override suspend fun deleteReport(filePath: String): Boolean { delay(200); return reports.removeAll { it.filePath == filePath } }

    private fun hash(input: String) = MessageDigest.getInstance("SHA-256").digest(input.toByteArray()).joinToString("") { "%02x".format(it) }
}
