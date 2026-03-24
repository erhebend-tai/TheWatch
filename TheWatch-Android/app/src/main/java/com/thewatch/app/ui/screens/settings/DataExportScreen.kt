/**
 * WRITE-AHEAD LOG | File: DataExportScreen.kt | Purpose: GDPR Art.20 data export UI
 * Created: 2026-03-24 | Author: Claude | Deps: Compose Material3, DataExportPort
 * Usage: composable("data_export") { DataExportScreen(navController) }
 */
package com.thewatch.app.ui.screens.settings

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.Share
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.navigation.NavController
import com.thewatch.app.data.gdpr.DataCategory
import com.thewatch.app.data.gdpr.DataExportStatus
import com.thewatch.app.data.gdpr.mock.MockDataExportAdapter
import com.thewatch.app.ui.theme.Navy
import com.thewatch.app.ui.theme.RedPrimary
import com.thewatch.app.ui.theme.White
import kotlinx.coroutines.launch

@Composable
fun DataExportScreen(navController: NavController) {
    val scope = rememberCoroutineScope()
    val exportPort = remember { MockDataExportAdapter() }
    val categorySelections = remember { mutableStateMapOf<DataCategory, Boolean>().apply { DataCategory.entries.forEach { this[it] = true } } }
    var exportStatus by remember { mutableStateOf<DataExportStatus?>(null) }
    var isExporting by remember { mutableStateOf(false) }

    Column(modifier = Modifier.fillMaxSize().background(White).verticalScroll(rememberScrollState())) {
        Row(modifier = Modifier.fillMaxWidth().background(Navy).padding(16.dp), verticalAlignment = Alignment.CenterVertically) {
            IconButton(onClick = { navController.navigateUp() }) { Icon(Icons.AutoMirrored.Filled.ArrowBack, "Back", tint = White) }
            Text("Export My Data", fontSize = 20.sp, fontWeight = FontWeight.Bold, color = White, modifier = Modifier.weight(1f))
        }

        Column(modifier = Modifier.padding(24.dp)) {
            Card(modifier = Modifier.fillMaxWidth(), colors = CardDefaults.cardColors(containerColor = Color(0xFFF0F7FF)), shape = RoundedCornerShape(12.dp)) {
                Column(modifier = Modifier.padding(16.dp)) {
                    Text("Your Data, Your Right", fontSize = 16.sp, fontWeight = FontWeight.Bold, color = Navy)
                    Spacer(Modifier.height(8.dp))
                    Text("Under GDPR Article 20, you have the right to receive your personal data in a structured, machine-readable format (JSON).", fontSize = 13.sp, color = Color.DarkGray)
                }
            }
            Spacer(Modifier.height(20.dp))
            Text("Select Data Categories", fontSize = 16.sp, fontWeight = FontWeight.Bold, color = Navy)
            Spacer(Modifier.height(8.dp))

            DataCategory.entries.forEach { category ->
                Row(modifier = Modifier.fillMaxWidth().padding(vertical = 4.dp).semantics { contentDescription = "Toggle ${category.name}" }, verticalAlignment = Alignment.CenterVertically) {
                    Checkbox(checked = categorySelections[category] == true, onCheckedChange = { categorySelections[category] = it })
                    Text(category.name.replace("_", " ").lowercase().replaceFirstChar { it.uppercase() }, fontSize = 14.sp, color = Navy, modifier = Modifier.padding(start = 8.dp))
                }
            }
            Spacer(Modifier.height(20.dp))

            if (isExporting && exportStatus is DataExportStatus.Preparing) {
                val p = (exportStatus as DataExportStatus.Preparing).progressPercent
                Text("Preparing export... $p%", fontSize = 14.sp, color = Color.Gray)
                Spacer(Modifier.height(8.dp))
                LinearProgressIndicator(progress = { p / 100f }, modifier = Modifier.fillMaxWidth())
                Spacer(Modifier.height(16.dp))
            }

            if (exportStatus is DataExportStatus.Complete) {
                Card(modifier = Modifier.fillMaxWidth(), colors = CardDefaults.cardColors(containerColor = Color(0xFFE8F5E9)), shape = RoundedCornerShape(12.dp)) {
                    Row(modifier = Modifier.padding(16.dp), verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                        Icon(Icons.Default.CheckCircle, "Done", tint = Color(0xFF4CAF50), modifier = Modifier.size(32.dp))
                        Column { Text("Export Complete", fontWeight = FontWeight.Bold, color = Color(0xFF2E7D32)); Text("Size: ${(exportStatus as DataExportStatus.Complete).sizeBytes / 1024}KB", fontSize = 12.sp, color = Color(0xFF4CAF50)) }
                    }
                }
                Spacer(Modifier.height(12.dp))
                Button(onClick = {}, modifier = Modifier.fillMaxWidth(), colors = ButtonDefaults.buttonColors(containerColor = Navy)) {
                    Icon(Icons.Default.Share, null, tint = White); Text("  Share Export", color = White, modifier = Modifier.padding(8.dp))
                }
                Spacer(Modifier.height(12.dp))
            }

            Button(
                onClick = { isExporting = true; scope.launch { exportPort.exportWithProgress("user-001", categorySelections.filter { it.value }.keys).collect { exportStatus = it; if (it is DataExportStatus.Complete) isExporting = false } } },
                modifier = Modifier.fillMaxWidth(), enabled = !isExporting && categorySelections.any { it.value },
                colors = ButtonDefaults.buttonColors(containerColor = RedPrimary)
            ) { if (isExporting) CircularProgressIndicator(Modifier.size(20.dp), color = White, strokeWidth = 2.dp) else Text("Export Selected Data", color = White, modifier = Modifier.padding(8.dp)) }
        }
    }
}
