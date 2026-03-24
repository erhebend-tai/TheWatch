/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         PDFExportPort.kt                                       │
 * │ Purpose:      Hexagonal port for tamper-hashed PDF incident reports. │
 * │               Uses Android Canvas-based PdfDocument. HMAC-SHA256     │
 * │               hash in footer for tamper detection.                   │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: android.graphics.pdf.PdfDocument, kotlinx.coroutines   │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   val port: PDFExportPort = hiltGet()                                │
 * │   val result = port.generateIncidentReport("user-001")               │
 * │   if (result.isSuccess) shareFile(result.getOrThrow().filePath)      │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.data.export

data class PDFExportResult(
    val filePath: String, val fileName: String, val sizeBytes: Long,
    val pageCount: Int, val tamperHash: String, val generatedAt: Long = System.currentTimeMillis()
)

data class PDFExportConfig(
    val title: String = "TheWatch Incident Report",
    val includeLocationMaps: Boolean = false,
    val includeResponderDetails: Boolean = true,
    val includeTimeline: Boolean = true,
    val dateRangeStart: Long? = null,
    val dateRangeEnd: Long? = null,
    val pageSize: PageSize = PageSize.A4
)

enum class PageSize(val widthPt: Int, val heightPt: Int) {
    A4(595, 842), LETTER(612, 792), LEGAL(612, 1008)
}

interface PDFExportPort {
    suspend fun generateIncidentReport(userId: String, config: PDFExportConfig = PDFExportConfig()): Result<PDFExportResult>
    suspend fun generateSingleIncidentReport(incidentId: String, config: PDFExportConfig = PDFExportConfig()): Result<PDFExportResult>
    suspend fun verifyTamperHash(filePath: String): Boolean
    suspend fun listGeneratedReports(): List<PDFExportResult>
    suspend fun deleteReport(filePath: String): Boolean
}
