/**
 * ┌──────────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                         │
 * │ File:    EvidenceGalleryScreen.kt                                       │
 * │ Purpose: Grid gallery view of all evidence for an incident. Features:   │
 * │          - Filterable by type (Photo/Video/Audio/Sitrep)                │
 * │          - Tap to view full-size evidence detail                        │
 * │          - Chain-of-custody verification badge (Verified/Tampered)       │
 * │          - Storage usage indicator                                       │
 * │          - Thumbnail grid with type icon overlays                       │
 * │ Date:    2026-03-24                                                     │
 * │ Author:  Claude                                                         │
 * │ Deps:    EvidenceGalleryViewModel, Compose, Material 3                  │
 * │                                                                         │
 * │ Usage Example:                                                          │
 * │   composable("evidence_gallery/{incidentId}") { backStackEntry ->       │
 * │       EvidenceGalleryScreen(                                            │
 * │           navController = navController,                                │
 * │           incidentId = backStackEntry.arguments?.getString("incidentId")│
 * │       )                                                                 │
 * │   }                                                                     │
 * └──────────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.ui.screens.evidence

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowBack
import androidx.compose.material.icons.filled.AudioFile
import androidx.compose.material.icons.filled.CameraAlt
import androidx.compose.material.icons.filled.CheckCircle
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Description
import androidx.compose.material.icons.filled.Error
import androidx.compose.material.icons.filled.FilterList
import androidx.compose.material.icons.filled.Info
import androidx.compose.material.icons.filled.PlayCircle
import androidx.compose.material.icons.filled.Shield
import androidx.compose.material.icons.filled.Videocam
import androidx.compose.material.icons.filled.VerifiedUser
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Badge
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.FilterChipDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.vector.ImageVector
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.navigation.NavController
import com.thewatch.app.data.model.Evidence
import com.thewatch.app.data.model.EvidenceType
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

@OptIn(ExperimentalMaterial3Api::class, ExperimentalLayoutApi::class)
@Composable
fun EvidenceGalleryScreen(
    navController: NavController,
    incidentId: String,
    viewModel: EvidenceGalleryViewModel = hiltViewModel()
) {
    val uiState by viewModel.uiState.collectAsState()
    var showDetailDialog by remember { mutableStateOf(false) }

    LaunchedEffect(incidentId) {
        viewModel.loadEvidence(incidentId)
        viewModel.verifyChain()
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = {
                    Column {
                        Text("Evidence Gallery", fontSize = 18.sp)
                        Text(
                            text = "${uiState.allEvidence.size} items | ${formatBytes(uiState.storageUsedBytes)}",
                            fontSize = 11.sp,
                            color = Color.White.copy(alpha = 0.6f)
                        )
                    }
                },
                navigationIcon = {
                    IconButton(onClick = { navController.popBackStack() }) {
                        Icon(Icons.Filled.ArrowBack, contentDescription = "Back")
                    }
                },
                actions = {
                    // Chain verification badge
                    val verification = uiState.chainVerification
                    if (verification != null) {
                        IconButton(onClick = { /* Show verification details */ }) {
                            Icon(
                                if (verification.isValid) Icons.Filled.VerifiedUser
                                else Icons.Filled.Error,
                                contentDescription = "Chain Status",
                                tint = if (verification.isValid) Color(0xFF00E676)
                                else Color(0xFFFF1744)
                            )
                        }
                    }
                },
                colors = TopAppBarDefaults.topAppBarColors(
                    containerColor = Color(0xFF1A1A2E),
                    titleContentColor = Color.White,
                    navigationIconContentColor = Color.White,
                    actionIconContentColor = Color.White
                )
            )
        }
    ) { padding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
                .background(Color(0xFF0D0D1A))
        ) {
            // Chain-of-custody status banner
            val verification = uiState.chainVerification
            if (verification != null) {
                ChainStatusBanner(verification.isValid, verification.message)
            }

            // Type filter chips
            FlowRow(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(horizontal = 16.dp, vertical = 8.dp),
                horizontalArrangement = Arrangement.spacedBy(8.dp)
            ) {
                FilterChip(
                    selected = uiState.activeFilter == null,
                    onClick = { viewModel.filterByType(null) },
                    label = { Text("All (${uiState.allEvidence.size})") },
                    colors = FilterChipDefaults.filterChipColors(
                        selectedContainerColor = Color(0xFF00E676),
                        selectedLabelColor = Color.Black
                    )
                )

                EvidenceType.entries.forEach { type ->
                    val count = uiState.allEvidence.count { it.type == type }
                    if (count > 0) {
                        FilterChip(
                            selected = uiState.activeFilter == type,
                            onClick = { viewModel.filterByType(type) },
                            label = { Text("${type.name} ($count)") },
                            leadingIcon = {
                                Icon(
                                    getTypeIcon(type),
                                    contentDescription = null,
                                    modifier = Modifier.size(16.dp)
                                )
                            },
                            colors = FilterChipDefaults.filterChipColors(
                                selectedContainerColor = getTypeColor(type),
                                selectedLabelColor = Color.White
                            )
                        )
                    }
                }
            }

            // Content
            if (uiState.isLoading) {
                Box(
                    modifier = Modifier.fillMaxSize(),
                    contentAlignment = Alignment.Center
                ) {
                    CircularProgressIndicator(color = Color(0xFF00E676))
                }
            } else if (uiState.filteredEvidence.isEmpty()) {
                Box(
                    modifier = Modifier.fillMaxSize(),
                    contentAlignment = Alignment.Center
                ) {
                    Column(horizontalAlignment = Alignment.CenterHorizontally) {
                        Icon(
                            Icons.Filled.CameraAlt,
                            contentDescription = null,
                            tint = Color.White.copy(alpha = 0.3f),
                            modifier = Modifier.size(64.dp)
                        )
                        Spacer(modifier = Modifier.height(16.dp))
                        Text(
                            text = if (uiState.activeFilter != null) "No ${uiState.activeFilter?.name} evidence"
                            else "No evidence collected yet",
                            color = Color.White.copy(alpha = 0.5f),
                            fontSize = 16.sp
                        )
                    }
                }
            } else {
                // Evidence grid
                LazyVerticalGrid(
                    columns = GridCells.Fixed(3),
                    contentPadding = PaddingValues(8.dp),
                    horizontalArrangement = Arrangement.spacedBy(4.dp),
                    verticalArrangement = Arrangement.spacedBy(4.dp),
                    modifier = Modifier.fillMaxSize()
                ) {
                    items(uiState.filteredEvidence) { evidence ->
                        EvidenceThumbnailCard(
                            evidence = evidence,
                            onClick = {
                                viewModel.selectEvidence(evidence)
                                showDetailDialog = true
                            }
                        )
                    }
                }
            }

            // Error display
            uiState.error?.let { error ->
                Card(
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(16.dp),
                    colors = CardDefaults.cardColors(
                        containerColor = Color(0xFF3E1A1A)
                    )
                ) {
                    Row(
                        modifier = Modifier.padding(12.dp),
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Icon(
                            Icons.Filled.Error,
                            contentDescription = null,
                            tint = Color(0xFFFF5252)
                        )
                        Spacer(modifier = Modifier.width(8.dp))
                        Text(
                            text = error,
                            color = Color(0xFFFF5252),
                            fontSize = 12.sp,
                            modifier = Modifier.weight(1f)
                        )
                        IconButton(
                            onClick = { viewModel.clearError() },
                            modifier = Modifier.size(24.dp)
                        ) {
                            Icon(
                                Icons.Filled.Close,
                                contentDescription = "Dismiss",
                                tint = Color.White,
                                modifier = Modifier.size(16.dp)
                            )
                        }
                    }
                }
            }
        }

        // Detail dialog
        if (showDetailDialog && uiState.selectedEvidence != null) {
            EvidenceDetailDialog(
                evidence = uiState.selectedEvidence!!,
                onDismiss = {
                    showDetailDialog = false
                    viewModel.selectEvidence(null)
                },
                onDelete = { evidenceId ->
                    viewModel.deleteEvidence(evidenceId)
                    showDetailDialog = false
                }
            )
        }
    }
}

@Composable
private fun ChainStatusBanner(isValid: Boolean, message: String) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .background(
                if (isValid) Color(0xFF1B5E20).copy(alpha = 0.3f)
                else Color(0xFFB71C1C).copy(alpha = 0.3f)
            )
            .padding(horizontal = 16.dp, vertical = 8.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Icon(
            if (isValid) Icons.Filled.Shield else Icons.Filled.Error,
            contentDescription = null,
            tint = if (isValid) Color(0xFF00E676) else Color(0xFFFF1744),
            modifier = Modifier.size(20.dp)
        )
        Spacer(modifier = Modifier.width(8.dp))
        Text(
            text = if (isValid) "Chain of Custody: VERIFIED" else "Chain of Custody: TAMPERED",
            color = if (isValid) Color(0xFF00E676) else Color(0xFFFF1744),
            fontSize = 12.sp,
            fontWeight = FontWeight.Bold
        )
    }
}

@Composable
private fun EvidenceThumbnailCard(
    evidence: Evidence,
    onClick: () -> Unit
) {
    Card(
        modifier = Modifier
            .aspectRatio(1f)
            .clickable(onClick = onClick),
        colors = CardDefaults.cardColors(
            containerColor = Color(0xFF1A1A2E)
        ),
        shape = RoundedCornerShape(8.dp)
    ) {
        Box(
            modifier = Modifier.fillMaxSize(),
            contentAlignment = Alignment.Center
        ) {
            // Thumbnail placeholder — in native impl, load actual thumbnail via Coil
            Icon(
                getTypeIcon(evidence.type),
                contentDescription = evidence.type.name,
                tint = getTypeColor(evidence.type).copy(alpha = 0.6f),
                modifier = Modifier.size(40.dp)
            )

            // Type badge (top-left)
            Text(
                text = evidence.type.name.take(1),
                color = Color.White,
                fontSize = 10.sp,
                fontWeight = FontWeight.Bold,
                modifier = Modifier
                    .align(Alignment.TopStart)
                    .padding(4.dp)
                    .background(
                        getTypeColor(evidence.type),
                        CircleShape
                    )
                    .padding(horizontal = 6.dp, vertical = 2.dp)
            )

            // Verified badge (top-right)
            Icon(
                if (evidence.verified) Icons.Filled.CheckCircle else Icons.Filled.Error,
                contentDescription = if (evidence.verified) "Verified" else "Unverified",
                tint = if (evidence.verified) Color(0xFF00E676) else Color(0xFFFF9800),
                modifier = Modifier
                    .align(Alignment.TopEnd)
                    .padding(4.dp)
                    .size(16.dp)
            )

            // Timestamp (bottom)
            Text(
                text = SimpleDateFormat("HH:mm:ss", Locale.US)
                    .format(Date(evidence.timestamp)),
                color = Color.White.copy(alpha = 0.7f),
                fontSize = 9.sp,
                modifier = Modifier
                    .align(Alignment.BottomCenter)
                    .padding(4.dp)
                    .background(
                        Color.Black.copy(alpha = 0.6f),
                        RoundedCornerShape(4.dp)
                    )
                    .padding(horizontal = 4.dp, vertical = 1.dp)
            )
        }
    }
}

@Composable
private fun EvidenceDetailDialog(
    evidence: Evidence,
    onDismiss: () -> Unit,
    onDelete: (String) -> Unit
) {
    AlertDialog(
        onDismissRequest = onDismiss,
        containerColor = Color(0xFF1A1A2E),
        title = {
            Row(verticalAlignment = Alignment.CenterVertically) {
                Icon(
                    getTypeIcon(evidence.type),
                    contentDescription = null,
                    tint = getTypeColor(evidence.type),
                    modifier = Modifier.size(24.dp)
                )
                Spacer(modifier = Modifier.width(8.dp))
                Text(
                    text = "${evidence.type.name} Evidence",
                    color = Color.White,
                    fontSize = 18.sp,
                    fontWeight = FontWeight.Bold
                )
            }
        },
        text = {
            Column(
                modifier = Modifier.verticalScroll(rememberScrollState())
            ) {
                DetailRow("ID", evidence.id.take(16) + "...")
                DetailRow("Incident", evidence.incidentId)
                DetailRow("Timestamp", SimpleDateFormat("yyyy-MM-dd HH:mm:ss", Locale.US)
                    .format(Date(evidence.timestamp)))
                if (evidence.latitude != null && evidence.longitude != null) {
                    DetailRow("Location", "%.6f, %.6f".format(evidence.latitude, evidence.longitude))
                }
                if (evidence.filePath != null) {
                    DetailRow("File", evidence.filePath)
                }
                if (evidence.durationMillis != null) {
                    DetailRow("Duration", "${evidence.durationMillis / 1000}s")
                }
                DetailRow("Size", formatBytes(evidence.fileSizeBytes))
                if (evidence.annotation != null) {
                    DetailRow("Annotation", evidence.annotation)
                }
                DetailRow("Submitted By", evidence.submittedBy)

                Spacer(modifier = Modifier.height(12.dp))

                // Chain-of-custody info
                Text(
                    text = "Chain of Custody",
                    color = Color(0xFF00E676),
                    fontSize = 12.sp,
                    fontWeight = FontWeight.Bold
                )
                Spacer(modifier = Modifier.height(4.dp))
                Text(
                    text = "Hash: ${evidence.hash}",
                    color = Color.White.copy(alpha = 0.5f),
                    fontSize = 9.sp,
                    fontFamily = FontFamily.Monospace
                )
                Text(
                    text = "Prev: ${evidence.previousHash.take(32)}${if (evidence.previousHash.length > 32) "..." else ""}",
                    color = Color.White.copy(alpha = 0.5f),
                    fontSize = 9.sp,
                    fontFamily = FontFamily.Monospace
                )

                Spacer(modifier = Modifier.height(8.dp))

                Row(verticalAlignment = Alignment.CenterVertically) {
                    Icon(
                        if (evidence.verified) Icons.Filled.CheckCircle else Icons.Filled.Error,
                        contentDescription = null,
                        tint = if (evidence.verified) Color(0xFF00E676) else Color(0xFFFF1744),
                        modifier = Modifier.size(16.dp)
                    )
                    Spacer(modifier = Modifier.width(4.dp))
                    Text(
                        text = if (evidence.verified) "Integrity Verified" else "Integrity Unknown",
                        color = if (evidence.verified) Color(0xFF00E676) else Color(0xFFFF9800),
                        fontSize = 12.sp
                    )
                }
            }
        },
        confirmButton = {
            TextButton(onClick = onDismiss) {
                Text("Close", color = Color(0xFF00E676))
            }
        },
        dismissButton = {
            TextButton(onClick = { onDelete(evidence.id) }) {
                Text("Delete", color = Color(0xFFFF5252))
            }
        }
    )
}

@Composable
private fun DetailRow(label: String, value: String) {
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(vertical = 2.dp)
    ) {
        Text(
            text = "$label:",
            color = Color.White.copy(alpha = 0.5f),
            fontSize = 12.sp,
            modifier = Modifier.width(80.dp)
        )
        Text(
            text = value,
            color = Color.White,
            fontSize = 12.sp,
            maxLines = 2,
            overflow = TextOverflow.Ellipsis
        )
    }
}

private fun getTypeIcon(type: EvidenceType): ImageVector {
    return when (type) {
        EvidenceType.PHOTO -> Icons.Filled.CameraAlt
        EvidenceType.VIDEO -> Icons.Filled.Videocam
        EvidenceType.AUDIO -> Icons.Filled.AudioFile
        EvidenceType.SITREP -> Icons.Filled.Description
    }
}

private fun getTypeColor(type: EvidenceType): Color {
    return when (type) {
        EvidenceType.PHOTO -> Color(0xFF2196F3)
        EvidenceType.VIDEO -> Color(0xFFFF1744)
        EvidenceType.AUDIO -> Color(0xFFFFC107)
        EvidenceType.SITREP -> Color(0xFF00E676)
    }
}

private fun formatBytes(bytes: Long): String {
    return when {
        bytes < 1024 -> "${bytes}B"
        bytes < 1024 * 1024 -> "%.1fKB".format(bytes / 1024.0)
        bytes < 1024 * 1024 * 1024 -> "%.1fMB".format(bytes / (1024.0 * 1024))
        else -> "%.1fGB".format(bytes / (1024.0 * 1024 * 1024))
    }
}
