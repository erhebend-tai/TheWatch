/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         LogViewerViewModel.kt                                  │
 * │ Purpose:      Hilt ViewModel for the production Log Viewer screen.   │
 * │               Collects from LoggingPort.observe() for real-time      │
 * │               streaming and LoggingPort.query() for pull-to-refresh. │
 * │               Manages filter/search state, expandable entry IDs,     │
 * │               and export-to-file for sharing filtered log output.    │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: Hilt, LoggingPort, LogEntry, LogLevel, Coroutines     │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   @Composable                                                        │
 * │   fun LogViewerRoute(navController: NavController) {                 │
 * │       val vm: LogViewerViewModel = hiltViewModel()                   │
 * │       LogViewerScreen(navController, vm)                             │
 * │   }                                                                  │
 * │                                                                      │
 * │ Architecture:                                                        │
 * │ - All data flows through LoggingPort (hexagonal port/adapter)        │
 * │ - No direct Room/Firestore access from this ViewModel               │
 * │ - observe() gives a hot Flow<LogEntry> for live streaming            │
 * │ - query() is used for initial load and pull-to-refresh               │
 * │ - Export writes to app cache dir, returns shareable URI              │
 * │                                                                      │
 * │ Filter semantics:                                                    │
 * │ - Level filter: shows entries AT or ABOVE selected level             │
 * │ - Search: case-insensitive match on messageTemplate + sourceContext  │
 * │ - Both filters compose (AND logic)                                   │
 * │                                                                      │
 * │ Expandable entries:                                                  │
 * │ - Tap a log row to toggle expansion                                  │
 * │ - Expanded view shows: properties map, correlationId, exception      │
 * │ - Multiple entries can be expanded simultaneously                    │
 * │                                                                      │
 * │ Export format (plain text):                                          │
 * │   [2026-03-24T10:35:00.123Z] [INFO] [LocationCoordinator]           │
 * │   Location updated to 30.2672,-97.7431 in HIGH_ACCURACY mode        │
 * │   Properties: {Lat=30.2672, Lng=-97.7431, Mode=HIGH_ACCURACY}       │
 * │   CorrelationId: sos-abc123                                          │
 * │   ---                                                                │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.ui.logging

import android.content.Context
import android.content.Intent
import android.net.Uri
import androidx.core.content.FileProvider
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.thewatch.app.data.logging.LogEntry
import com.thewatch.app.data.logging.LogLevel
import com.thewatch.app.data.logging.LoggingPort
import dagger.hilt.android.lifecycle.HiltViewModel
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import java.io.File
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import javax.inject.Inject

/**
 * UI state for the Log Viewer screen.
 *
 * @property entries All log entries collected so far (newest first)
 * @property filteredEntries Entries after level + search filters applied
 * @property selectedLevel Minimum severity filter (null = show all)
 * @property searchQuery Free-text search against messageTemplate and sourceContext
 * @property expandedEntryIds Set of entry IDs currently expanded in the UI
 * @property isRefreshing True while a pull-to-refresh query is in progress
 * @property isLiveStreaming True while the observe() collector is active
 * @property entryCount Total count of unfiltered entries
 * @property exportUri URI of the last exported file (null if no export yet)
 */
data class LogViewerUiState(
    val entries: List<LogEntry> = emptyList(),
    val filteredEntries: List<LogEntry> = emptyList(),
    val selectedLevel: LogLevel? = null,
    val searchQuery: String = "",
    val expandedEntryIds: Set<String> = emptySet(),
    val isRefreshing: Boolean = false,
    val isLiveStreaming: Boolean = true,
    val entryCount: Int = 0,
    val exportUri: Uri? = null
)

@HiltViewModel
class LogViewerViewModel @Inject constructor(
    private val loggingPort: LoggingPort,
    @ApplicationContext private val appContext: Context
) : ViewModel() {

    private val _uiState = MutableStateFlow(LogViewerUiState())
    val uiState: StateFlow<LogViewerUiState> = _uiState.asStateFlow()

    private val timeFormatter: DateTimeFormatter = DateTimeFormatter
        .ofPattern("HH:mm:ss.SSS")
        .withZone(ZoneId.systemDefault())

    private val isoFormatter: DateTimeFormatter = DateTimeFormatter.ISO_INSTANT

    init {
        initialLoad()
        startLiveStream()
    }

    // ── Initial Load ──────────────────────────────────────────────

    private fun initialLoad() {
        viewModelScope.launch {
            val entries = loggingPort.query(limit = 500)
            _uiState.value = _uiState.value.copy(
                entries = entries,
                entryCount = entries.size
            )
            applyFilters()
        }
    }

    // ── Live Streaming ────────────────────────────────────────────

    private fun startLiveStream() {
        viewModelScope.launch {
            loggingPort.observe(LogLevel.Verbose).collect { entry ->
                val current = _uiState.value
                val updated = listOf(entry) + current.entries
                // Cap at 2000 entries in memory to prevent OOM
                val capped = if (updated.size > 2000) updated.take(2000) else updated
                _uiState.value = current.copy(
                    entries = capped,
                    entryCount = capped.size
                )
                applyFilters()
            }
        }
    }

    // ── Pull-to-Refresh ───────────────────────────────────────────

    fun refresh() {
        viewModelScope.launch {
            _uiState.value = _uiState.value.copy(isRefreshing = true)
            val entries = loggingPort.query(limit = 500)
            _uiState.value = _uiState.value.copy(
                entries = entries,
                entryCount = entries.size,
                isRefreshing = false
            )
            applyFilters()
        }
    }

    // ── Filters ───────────────────────────────────────────────────

    fun setLevelFilter(level: LogLevel?) {
        _uiState.value = _uiState.value.copy(selectedLevel = level)
        applyFilters()
    }

    fun setSearchQuery(query: String) {
        _uiState.value = _uiState.value.copy(searchQuery = query)
        applyFilters()
    }

    private fun applyFilters() {
        var result = _uiState.value.entries

        _uiState.value.selectedLevel?.let { minLevel ->
            result = result.filter { it.level.ordinal >= minLevel.ordinal }
        }

        val query = _uiState.value.searchQuery
        if (query.isNotBlank()) {
            result = result.filter {
                it.messageTemplate.contains(query, ignoreCase = true) ||
                    it.sourceContext.contains(query, ignoreCase = true) ||
                    it.renderedMessage().contains(query, ignoreCase = true)
            }
        }

        _uiState.value = _uiState.value.copy(filteredEntries = result)
    }

    // ── Expand / Collapse ─────────────────────────────────────────

    fun toggleExpanded(entryId: String) {
        val current = _uiState.value.expandedEntryIds
        _uiState.value = _uiState.value.copy(
            expandedEntryIds = if (entryId in current) current - entryId else current + entryId
        )
    }

    fun isExpanded(entryId: String): Boolean =
        entryId in _uiState.value.expandedEntryIds

    // ── Clear ─────────────────────────────────────────────────────

    fun clearLogs() {
        _uiState.value = _uiState.value.copy(
            entries = emptyList(),
            filteredEntries = emptyList(),
            expandedEntryIds = emptySet(),
            entryCount = 0
        )
    }

    // ── Export to Shareable File ──────────────────────────────────

    /**
     * Writes filtered entries to a plain-text file in the app cache directory
     * and returns a content:// URI suitable for sharing via Intent.ACTION_SEND.
     *
     * Export format per entry:
     *   [ISO-8601 timestamp] [LEVEL] [SourceContext]
     *   Rendered message text
     *   Properties: {key=value, ...}
     *   CorrelationId: <id>      (if present)
     *   Exception: <text>        (if present)
     *   ---
     */
    fun exportToFile(): Uri? {
        val entries = _uiState.value.filteredEntries
        if (entries.isEmpty()) return null

        val sb = StringBuilder()
        sb.appendLine("TheWatch Log Export — ${Instant.now()}")
        sb.appendLine("Filter: level=${_uiState.value.selectedLevel?.name ?: "ALL"}, query=\"${_uiState.value.searchQuery}\"")
        sb.appendLine("Entries: ${entries.size}")
        sb.appendLine("═".repeat(72))
        sb.appendLine()

        for (entry in entries) {
            sb.appendLine("[${isoFormatter.format(entry.timestamp)}] [${entry.level.name}] [${entry.sourceContext}]")
            sb.appendLine(entry.renderedMessage())
            if (entry.properties.isNotEmpty()) {
                sb.appendLine("  Properties: ${entry.properties}")
            }
            entry.correlationId?.let { sb.appendLine("  CorrelationId: $it") }
            entry.exception?.let { sb.appendLine("  Exception: $it") }
            sb.appendLine("---")
        }

        val exportDir = File(appContext.cacheDir, "log_exports")
        exportDir.mkdirs()
        val timestamp = DateTimeFormatter.ofPattern("yyyyMMdd_HHmmss")
            .withZone(ZoneId.systemDefault())
            .format(Instant.now())
        val file = File(exportDir, "thewatch_logs_$timestamp.txt")
        file.writeText(sb.toString())

        val uri = FileProvider.getUriForFile(
            appContext,
            "${appContext.packageName}.fileprovider",
            file
        )
        _uiState.value = _uiState.value.copy(exportUri = uri)
        return uri
    }

    /**
     * Creates a share intent for the exported log file.
     * Call exportToFile() first, then pass the returned URI here.
     */
    fun createShareIntent(uri: Uri): Intent {
        return Intent(Intent.ACTION_SEND).apply {
            type = "text/plain"
            putExtra(Intent.EXTRA_STREAM, uri)
            putExtra(Intent.EXTRA_SUBJECT, "TheWatch Log Export")
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
            addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        }
    }

    // ── Formatting Helpers ────────────────────────────────────────

    fun formatTime(instant: Instant): String = timeFormatter.format(instant)

    fun levelColor(level: LogLevel): Long = when (level) {
        LogLevel.Verbose -> 0xFF808080
        LogLevel.Debug -> 0xFF4FC3F7
        LogLevel.Information -> 0xFF81C784
        LogLevel.Warning -> 0xFFFFB74D
        LogLevel.Error -> 0xFFE57373
        LogLevel.Fatal -> 0xFFFF1744
    }
}
