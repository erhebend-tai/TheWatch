/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         LogViewerScreen.kt (ui/logging)                        │
 * │ Purpose:      Production-grade on-device log viewer. Compose screen  │
 * │               with LazyColumn, color-coded LogLevel chips, search    │
 * │               bar, pull-to-refresh, expandable entries showing       │
 * │               properties/correlationId/exception, and export-to-    │
 * │               share functionality.                                   │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: Jetpack Compose Material3, Hilt, LogViewerViewModel   │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   // In NavGraph:                                                    │
 * │   composable("log_viewer") {                                         │
 * │       LogViewerScreen(navController = navController)                  │
 * │   }                                                                  │
 * │                                                                      │
 * │ Differs from diagnostics/LogViewerScreen.kt:                         │
 * │ - Uses Hilt-injected ViewModel with real LoggingPort                 │
 * │ - Pull-to-refresh via Material3 pullRefresh                          │
 * │ - Expandable rows show properties map, correlationId, exception     │
 * │ - Export-to-file action writes to cache and opens share sheet        │
 * │ - Filter chips show entry counts per level                          │
 * │ - Proper accessibility labels and content descriptions              │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.ui.logging

import android.content.Intent
import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.expandVertically
import androidx.compose.animation.shrinkVertically
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.KeyboardArrowDown
import androidx.compose.material.icons.filled.KeyboardArrowUp
import androidx.compose.material.icons.filled.Search
import androidx.compose.material.icons.filled.Share
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.navigation.NavController
import com.thewatch.app.data.logging.LogEntry
import com.thewatch.app.data.logging.LogLevel
import com.thewatch.app.ui.theme.Navy
import com.thewatch.app.ui.theme.White

// ─────────────────────────── Color Constants ───────────────────────────

private val DarkBackground = Color(0xFF1E1E1E)
private val CardBackground = Color(0xFF252525)
private val ExpandedBackground = Color(0xFF2D2D2D)
private val SubtleGray = Color(0xFF9E9E9E)

private val VerboseColor = Color(0xFF808080)
private val DebugColor = Color(0xFF4FC3F7)
private val InfoColor = Color(0xFF81C784)
private val WarningColor = Color(0xFFFFB74D)
private val ErrorColor = Color(0xFFE57373)
private val FatalColor = Color(0xFFFF1744)

private fun logLevelColor(level: LogLevel): Color = when (level) {
    LogLevel.Verbose -> VerboseColor
    LogLevel.Debug -> DebugColor
    LogLevel.Information -> InfoColor
    LogLevel.Warning -> WarningColor
    LogLevel.Error -> ErrorColor
    LogLevel.Fatal -> FatalColor
}

// ─────────────────────────── Screen ───────────────────────────

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun LogViewerScreen(
    navController: NavController,
    viewModel: LogViewerViewModel = hiltViewModel()
) {
    val state by viewModel.uiState.collectAsState()
    val context = LocalContext.current
    var searchExpanded by remember { mutableStateOf(false) }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .background(DarkBackground)
    ) {
        // ── Top Bar ──────────────────────────────────────────────
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .background(Navy)
                .padding(horizontal = 8.dp, vertical = 12.dp),
            verticalAlignment = Alignment.CenterVertically
        ) {
            IconButton(onClick = { navController.navigateUp() }) {
                Icon(Icons.AutoMirrored.Filled.ArrowBack, "Back", tint = White)
            }
            Text(
                text = "Log Viewer",
                fontSize = 20.sp,
                fontWeight = FontWeight.Bold,
                color = White,
                modifier = Modifier.weight(1f)
            )
            Text(
                text = "${state.entryCount}",
                fontSize = 12.sp,
                color = SubtleGray,
                modifier = Modifier.padding(end = 8.dp)
            )
            IconButton(onClick = { searchExpanded = !searchExpanded }) {
                Icon(
                    if (searchExpanded) Icons.Default.Close else Icons.Default.Search,
                    "Toggle search",
                    tint = White
                )
            }
            IconButton(onClick = {
                val uri = viewModel.exportToFile()
                if (uri != null) {
                    val intent = viewModel.createShareIntent(uri)
                    context.startActivity(Intent.createChooser(intent, "Share Logs"))
                }
            }) {
                Icon(Icons.Default.Share, "Export and share logs", tint = White)
            }
            IconButton(onClick = { viewModel.clearLogs() }) {
                Icon(Icons.Default.Delete, "Clear logs", tint = White)
            }
        }

        // ── Search Bar ───────────────────────────────────────────
        AnimatedVisibility(visible = searchExpanded) {
            OutlinedTextField(
                value = state.searchQuery,
                onValueChange = { viewModel.setSearchQuery(it) },
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 16.dp, vertical = 8.dp),
                placeholder = { Text("Search messageTemplate, sourceContext...", color = Color.Gray) },
                singleLine = true,
                colors = OutlinedTextFieldDefaults.colors(
                    focusedTextColor = White,
                    unfocusedTextColor = White,
                    cursorColor = InfoColor,
                    focusedBorderColor = InfoColor,
                    unfocusedBorderColor = SubtleGray
                )
            )
        }

        // ── Level Filter Chips ───────────────────────────────────
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .horizontalScroll(rememberScrollState())
                .padding(horizontal = 16.dp, vertical = 8.dp),
            horizontalArrangement = Arrangement.spacedBy(8.dp)
        ) {
            // "All" chip
            FilterChip(
                selected = state.selectedLevel == null,
                onClick = { viewModel.setLevelFilter(null) },
                label = { Text("All") },
                colors = FilterChipDefaults.filterChipColors(
                    selectedContainerColor = InfoColor.copy(alpha = 0.3f),
                    selectedLabelColor = White
                )
            )
            // Per-level chips
            LogLevel.entries.forEach { level ->
                val color = logLevelColor(level)
                val count = state.entries.count { it.level == level }
                FilterChip(
                    selected = state.selectedLevel == level,
                    onClick = { viewModel.setLevelFilter(level) },
                    label = {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Box(
                                modifier = Modifier
                                    .size(8.dp)
                                    .background(color, RoundedCornerShape(4.dp))
                            )
                            Spacer(modifier = Modifier.width(4.dp))
                            Text("${level.name} ($count)")
                        }
                    },
                    colors = FilterChipDefaults.filterChipColors(
                        selectedContainerColor = color.copy(alpha = 0.25f),
                        selectedLabelColor = White
                    )
                )
            }
        }

        // ── Entry Count ──────────────────────────────────────────
        Text(
            text = "${state.filteredEntries.size} of ${state.entryCount} entries",
            fontSize = 12.sp,
            color = SubtleGray,
            modifier = Modifier.padding(horizontal = 16.dp, vertical = 4.dp)
        )

        // ── Pull-to-Refresh Log List ─────────────────────────────
        val listState = rememberLazyListState()

        PullToRefreshBox(
            isRefreshing = state.isRefreshing,
            onRefresh = { viewModel.refresh() },
            modifier = Modifier.fillMaxSize()
        ) {
            LazyColumn(
                state = listState,
                modifier = Modifier
                    .fillMaxSize()
                    .padding(horizontal = 8.dp),
                verticalArrangement = Arrangement.spacedBy(4.dp)
            ) {
                items(state.filteredEntries, key = { it.id }) { entry ->
                    LogEntryCard(
                        entry = entry,
                        isExpanded = viewModel.isExpanded(entry.id),
                        onToggleExpand = { viewModel.toggleExpanded(entry.id) },
                        formatTime = { viewModel.formatTime(it) }
                    )
                }

                // Bottom spacer for scroll breathing room
                item {
                    Spacer(modifier = Modifier.height(16.dp))
                }
            }
        }
    }
}

// ─────────────────────────── Log Entry Card ───────────────────────────

@Composable
private fun LogEntryCard(
    entry: LogEntry,
    isExpanded: Boolean,
    onToggleExpand: () -> Unit,
    formatTime: (java.time.Instant) -> String
) {
    val levelColor = logLevelColor(entry.level)

    Card(
        modifier = Modifier
            .fillMaxWidth()
            .clickable(onClick = onToggleExpand)
            .semantics {
                contentDescription = "${entry.level} log from ${entry.sourceContext}: ${entry.renderedMessage()}"
            },
        colors = CardDefaults.cardColors(containerColor = CardBackground),
        shape = RoundedCornerShape(6.dp)
    ) {
        Column(modifier = Modifier.padding(10.dp)) {
            // ── Header Row: timestamp | level badge | source | expand arrow ──
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically
            ) {
                Row(verticalAlignment = Alignment.CenterVertically) {
                    // Timestamp
                    Text(
                        text = formatTime(entry.timestamp),
                        fontSize = 10.sp,
                        fontFamily = FontFamily.Monospace,
                        color = SubtleGray
                    )
                    Spacer(modifier = Modifier.width(8.dp))
                    // Level badge (colored capsule)
                    Text(
                        text = entry.level.name.take(4).uppercase(),
                        fontSize = 10.sp,
                        fontFamily = FontFamily.Monospace,
                        fontWeight = FontWeight.Bold,
                        color = Color.Black,
                        modifier = Modifier
                            .background(levelColor, RoundedCornerShape(10.dp))
                            .padding(horizontal = 6.dp, vertical = 2.dp)
                    )
                    Spacer(modifier = Modifier.width(8.dp))
                    // Source context
                    Text(
                        text = entry.sourceContext,
                        fontSize = 10.sp,
                        fontFamily = FontFamily.Monospace,
                        color = SubtleGray
                    )
                }
                Icon(
                    imageVector = if (isExpanded) Icons.Default.KeyboardArrowUp
                    else Icons.Default.KeyboardArrowDown,
                    contentDescription = if (isExpanded) "Collapse" else "Expand",
                    tint = SubtleGray,
                    modifier = Modifier.size(18.dp)
                )
            }

            Spacer(modifier = Modifier.height(4.dp))

            // ── Rendered message ──
            Text(
                text = entry.renderedMessage(),
                fontSize = 12.sp,
                fontFamily = FontFamily.Monospace,
                color = White,
                lineHeight = 16.sp
            )

            // ── Expanded detail section ──
            AnimatedVisibility(
                visible = isExpanded,
                enter = expandVertically(),
                exit = shrinkVertically()
            ) {
                Column(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(top = 8.dp)
                        .background(ExpandedBackground, RoundedCornerShape(4.dp))
                        .padding(8.dp)
                ) {
                    // Properties map
                    if (entry.properties.isNotEmpty()) {
                        Text(
                            text = "Properties",
                            fontSize = 10.sp,
                            fontWeight = FontWeight.Bold,
                            color = InfoColor
                        )
                        entry.properties.forEach { (key, value) ->
                            Row(modifier = Modifier.padding(start = 8.dp, top = 2.dp)) {
                                Text(
                                    text = "$key: ",
                                    fontSize = 10.sp,
                                    fontFamily = FontFamily.Monospace,
                                    fontWeight = FontWeight.Bold,
                                    color = DebugColor
                                )
                                Text(
                                    text = value,
                                    fontSize = 10.sp,
                                    fontFamily = FontFamily.Monospace,
                                    color = White
                                )
                            }
                        }
                        Spacer(modifier = Modifier.height(6.dp))
                    }

                    // Correlation ID
                    entry.correlationId?.let { cid ->
                        HorizontalDivider(color = SubtleGray.copy(alpha = 0.3f))
                        Spacer(modifier = Modifier.height(4.dp))
                        Row {
                            Text(
                                text = "CorrelationId: ",
                                fontSize = 10.sp,
                                fontFamily = FontFamily.Monospace,
                                fontWeight = FontWeight.Bold,
                                color = WarningColor
                            )
                            Text(
                                text = cid,
                                fontSize = 10.sp,
                                fontFamily = FontFamily.Monospace,
                                color = White
                            )
                        }
                    }

                    // Exception
                    entry.exception?.let { ex ->
                        Spacer(modifier = Modifier.height(4.dp))
                        HorizontalDivider(color = SubtleGray.copy(alpha = 0.3f))
                        Spacer(modifier = Modifier.height(4.dp))
                        Text(
                            text = "Exception",
                            fontSize = 10.sp,
                            fontWeight = FontWeight.Bold,
                            color = ErrorColor
                        )
                        Text(
                            text = ex,
                            fontSize = 10.sp,
                            fontFamily = FontFamily.Monospace,
                            color = ErrorColor.copy(alpha = 0.85f),
                            lineHeight = 14.sp,
                            modifier = Modifier.padding(top = 2.dp)
                        )
                    }

                    // Entry metadata
                    Spacer(modifier = Modifier.height(6.dp))
                    HorizontalDivider(color = SubtleGray.copy(alpha = 0.3f))
                    Spacer(modifier = Modifier.height(4.dp))
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.SpaceBetween
                    ) {
                        Text(
                            text = "id: ${entry.id.take(8)}…",
                            fontSize = 9.sp,
                            fontFamily = FontFamily.Monospace,
                            color = SubtleGray
                        )
                        entry.userId?.let {
                            Text(
                                text = "user: ${it.take(8)}…",
                                fontSize = 9.sp,
                                fontFamily = FontFamily.Monospace,
                                color = SubtleGray
                            )
                        }
                        Text(
                            text = if (entry.synced) "synced" else "local",
                            fontSize = 9.sp,
                            fontFamily = FontFamily.Monospace,
                            color = if (entry.synced) InfoColor else WarningColor
                        )
                    }
                }
            }
        }
    }
}
