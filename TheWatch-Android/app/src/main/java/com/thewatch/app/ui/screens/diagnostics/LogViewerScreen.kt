/**
 * WRITE-AHEAD LOG | File: LogViewerScreen.kt | Purpose: On-device log viewer with real-time stream
 * Created: 2026-03-24 | Author: Claude | Deps: Compose Material3, LoggingPort, ViewModel
 * Usage: composable("log_viewer") { LogViewerScreen(navController) }
 * NOTE: Developer diagnostics only. Gate behind dev mode toggle.
 */
package com.thewatch.app.ui.screens.diagnostics

import androidx.compose.foundation.background
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.ContentCopy
import androidx.compose.material.icons.filled.Delete
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalClipboardManager
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import androidx.navigation.NavController
import com.thewatch.app.data.logging.LogEntry
import com.thewatch.app.data.logging.LogLevel
import com.thewatch.app.ui.theme.Navy
import com.thewatch.app.ui.theme.White
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import java.time.Instant
import java.time.ZoneId
import java.time.format.DateTimeFormatter

data class LogViewerState(val entries: List<LogEntry> = emptyList(), val filtered: List<LogEntry> = emptyList(), val selectedLevel: LogLevel? = null, val query: String = "", val isLive: Boolean = true)

class LogViewerViewModel : ViewModel() {
    private val _state = MutableStateFlow(LogViewerState())
    val state: StateFlow<LogViewerState> = _state.asStateFlow()
    private val fmt = DateTimeFormatter.ofPattern("HH:mm:ss.SSS").withZone(ZoneId.systemDefault())

    init {
        val mocks = listOf(
            LogEntry(level = LogLevel.Information, sourceContext = "LocationCoordinator", messageTemplate = "Location updated to {Lat},{Lng}", properties = mapOf("Lat" to "30.2672", "Lng" to "-97.7431")),
            LogEntry(level = LogLevel.Warning, sourceContext = "PhraseDetectionService", messageTemplate = "Audio buffer underrun: {Frames} frames", properties = mapOf("Frames" to "12")),
            LogEntry(level = LogLevel.Error, sourceContext = "NotificationService", messageTemplate = "FCM token refresh failed: {Error}", properties = mapOf("Error" to "NETWORK_UNAVAILABLE")),
            LogEntry(level = LogLevel.Debug, sourceContext = "QuickTapDetector", messageTemplate = "Tap: count={C}, interval={I}ms", properties = mapOf("C" to "3", "I" to "450")),
            LogEntry(level = LogLevel.Information, sourceContext = "SOSService", messageTemplate = "SOS dispatched to {N} responders", properties = mapOf("N" to "7")),
            LogEntry(level = LogLevel.Fatal, sourceContext = "AppDatabase", messageTemplate = "Migration failed v{F}->v{T}", properties = mapOf("F" to "3", "T" to "4"))
        )
        _state.value = LogViewerState(entries = mocks, filtered = mocks)
        viewModelScope.launch { while (_state.value.isLive) { delay(5000); val e = LogEntry(level = listOf(LogLevel.Debug, LogLevel.Information, LogLevel.Warning).random(), sourceContext = listOf("LocationCoordinator", "SOSService").random(), messageTemplate = "Heartbeat at {T}", properties = mapOf("T" to fmt.format(Instant.now()))); _state.value = _state.value.copy(entries = listOf(e) + _state.value.entries); applyFilters() } }
    }

    fun setLevel(l: LogLevel?) { _state.value = _state.value.copy(selectedLevel = l); applyFilters() }
    fun setQuery(q: String) { _state.value = _state.value.copy(query = q); applyFilters() }
    fun clear() { _state.value = _state.value.copy(entries = emptyList(), filtered = emptyList()) }
    fun allText() = _state.value.filtered.joinToString("\n") { "[${fmt.format(it.timestamp)}] [${it.level}] [${it.sourceContext}] ${it.renderedMessage()}" }

    private fun applyFilters() {
        var f = _state.value.entries
        _state.value.selectedLevel?.let { l -> f = f.filter { it.level.ordinal >= l.ordinal } }
        if (_state.value.query.isNotBlank()) f = f.filter { it.renderedMessage().contains(_state.value.query, true) || it.sourceContext.contains(_state.value.query, true) }
        _state.value = _state.value.copy(filtered = f)
    }
}

@Composable
fun LogViewerScreen(navController: NavController, vm: LogViewerViewModel = androidx.lifecycle.viewmodel.compose.viewModel()) {
    val state by vm.state.collectAsState()
    val clip = LocalClipboardManager.current
    var searchOpen by remember { mutableStateOf(false) }

    Column(Modifier.fillMaxSize().background(Color(0xFF1E1E1E))) {
        Row(Modifier.fillMaxWidth().background(Navy).padding(16.dp), verticalAlignment = Alignment.CenterVertically) {
            IconButton(onClick = { navController.navigateUp() }) { Icon(Icons.AutoMirrored.Filled.ArrowBack, "Back", tint = White) }
            Text("Log Viewer", fontSize = 20.sp, fontWeight = FontWeight.Bold, color = White, modifier = Modifier.weight(1f))
            IconButton(onClick = { searchOpen = !searchOpen }) { Icon(Icons.Default.Search, "Search", tint = White) }
            IconButton(onClick = { clip.setText(AnnotatedString(vm.allText())) }) { Icon(Icons.Default.ContentCopy, "Copy all", tint = White) }
            IconButton(onClick = { vm.clear() }) { Icon(Icons.Default.Delete, "Clear", tint = White) }
        }
        if (searchOpen) OutlinedTextField(state.query, { vm.setQuery(it) }, Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 8.dp), placeholder = { Text("Search...", color = Color.Gray) }, singleLine = true)
        Row(Modifier.fillMaxWidth().horizontalScroll(rememberScrollState()).padding(horizontal = 16.dp, vertical = 8.dp), horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            FilterChip(state.selectedLevel == null, { vm.setLevel(null) }, label = { Text("All") })
            LogLevel.entries.forEach { l -> FilterChip(state.selectedLevel == l, { vm.setLevel(l) }, label = { Text(l.name) }) }
        }
        Text("${state.filtered.size} entries", fontSize = 12.sp, color = Color.Gray, modifier = Modifier.padding(horizontal = 16.dp, vertical = 4.dp))
        LazyColumn(Modifier.fillMaxSize().padding(horizontal = 8.dp)) {
            items(state.filtered, key = { it.id }) { entry ->
                val c = when (entry.level) { LogLevel.Verbose -> Color(0xFF808080); LogLevel.Debug -> Color(0xFF4FC3F7); LogLevel.Information -> Color(0xFF81C784); LogLevel.Warning -> Color(0xFFFFB74D); LogLevel.Error -> Color(0xFFE57373); LogLevel.Fatal -> Color(0xFFFF1744) }
                val tf = remember { DateTimeFormatter.ofPattern("HH:mm:ss.SSS").withZone(ZoneId.systemDefault()) }
                Card(Modifier.fillMaxWidth().padding(vertical = 2.dp).semantics { contentDescription = "${entry.level} from ${entry.sourceContext}" }, colors = CardDefaults.cardColors(containerColor = Color(0xFF252525)), shape = RoundedCornerShape(4.dp)) {
                    Column(Modifier.padding(8.dp)) {
                        Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                            Row { Text(tf.format(entry.timestamp), fontSize = 10.sp, fontFamily = FontFamily.Monospace, color = Color.Gray); Spacer(Modifier.width(8.dp)); Text(entry.level.name.take(4).uppercase(), fontSize = 10.sp, fontFamily = FontFamily.Monospace, fontWeight = FontWeight.Bold, color = c); Spacer(Modifier.width(8.dp)); Text(entry.sourceContext, fontSize = 10.sp, fontFamily = FontFamily.Monospace, color = Color(0xFF9E9E9E)) }
                            IconButton(onClick = { clip.setText(AnnotatedString(entry.renderedMessage())) }) { Icon(Icons.Default.ContentCopy, "Copy", tint = Color.Gray) }
                        }
                        Text(entry.renderedMessage(), fontSize = 12.sp, fontFamily = FontFamily.Monospace, color = White, lineHeight = 16.sp)
                    }
                }
            }
        }
    }
}
