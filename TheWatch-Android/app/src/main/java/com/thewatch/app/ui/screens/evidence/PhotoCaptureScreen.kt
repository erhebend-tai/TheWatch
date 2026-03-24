/**
 * ┌──────────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                         │
 * │ File:    PhotoCaptureScreen.kt                                          │
 * │ Purpose: Full-screen photo capture UI with CameraX preview, capture     │
 * │          button, text annotation overlay, preview with accept/retake,   │
 * │          auto-geotag and timestamp display. Material 3 design.          │
 * │ Date:    2026-03-24                                                     │
 * │ Author:  Claude                                                         │
 * │ Deps:    PhotoCaptureViewModel, CameraX, Compose, Material 3            │
 * │                                                                         │
 * │ Usage Example:                                                          │
 * │   // In NavGraph:                                                       │
 * │   composable("photo_capture/{incidentId}") { backStackEntry ->          │
 * │       val incidentId = backStackEntry.arguments?.getString("incidentId")│
 * │       PhotoCaptureScreen(                                               │
 * │           navController = navController,                                │
 * │           incidentId = incidentId ?: ""                                 │
 * │       )                                                                 │
 * │   }                                                                     │
 * │                                                                         │
 * │ CameraX Integration Notes:                                              │
 * │   - PreviewView requires AndroidView composable wrapper                 │
 * │   - ImageCapture use case for still photos                              │
 * │   - ProcessCameraProvider binds to lifecycle owner                       │
 * │   - In mock/development mode, shows placeholder instead of live camera  │
 * └──────────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.ui.screens.evidence

import androidx.compose.foundation.background
import androidx.compose.foundation.border
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
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.filled.ArrowBack
import androidx.compose.material.icons.filled.Camera
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.Close
import androidx.compose.material.icons.filled.Edit
import androidx.compose.material.icons.filled.FlashAuto
import androidx.compose.material.icons.filled.FlipCameraAndroid
import androidx.compose.material.icons.filled.LocationOn
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
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
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.navigation.NavController
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun PhotoCaptureScreen(
    navController: NavController,
    incidentId: String,
    latitude: Double = 0.0,
    longitude: Double = 0.0,
    viewModel: PhotoCaptureViewModel = hiltViewModel()
) {
    val uiState by viewModel.uiState.collectAsState()
    val annotation by viewModel.annotation.collectAsState()
    val capturedPhotos by viewModel.capturedPhotos.collectAsState()
    var showAnnotationInput by remember { mutableStateOf(false) }

    LaunchedEffect(Unit) {
        viewModel.onCameraReady()
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Photo Capture") },
                navigationIcon = {
                    IconButton(onClick = { navController.popBackStack() }) {
                        Icon(Icons.Filled.ArrowBack, contentDescription = "Back")
                    }
                },
                colors = TopAppBarDefaults.topAppBarColors(
                    containerColor = Color(0xFF1A1A2E),
                    titleContentColor = Color.White,
                    navigationIconContentColor = Color.White
                ),
                actions = {
                    // Photo count badge
                    if (capturedPhotos.isNotEmpty()) {
                        Text(
                            text = "${capturedPhotos.size} captured",
                            color = Color(0xFF00E676),
                            fontSize = 12.sp,
                            modifier = Modifier.padding(end = 16.dp)
                        )
                    }
                }
            )
        }
    ) { padding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
                .background(Color.Black)
        ) {
            when (val state = uiState) {
                is PhotoCaptureUiState.Idle,
                is PhotoCaptureUiState.CameraReady -> {
                    // Camera viewfinder area
                    Box(
                        modifier = Modifier
                            .fillMaxWidth()
                            .weight(1f)
                            .background(Color(0xFF2A2A3E)),
                        contentAlignment = Alignment.Center
                    ) {
                        // Placeholder for CameraX PreviewView
                        // In production: AndroidView wrapping PreviewView
                        Column(horizontalAlignment = Alignment.CenterHorizontally) {
                            Icon(
                                Icons.Filled.Camera,
                                contentDescription = null,
                                tint = Color.White.copy(alpha = 0.5f),
                                modifier = Modifier.size(64.dp)
                            )
                            Spacer(modifier = Modifier.height(8.dp))
                            Text(
                                text = "Camera Preview",
                                color = Color.White.copy(alpha = 0.5f),
                                fontSize = 14.sp
                            )
                        }

                        // Geotag overlay (top-left)
                        Row(
                            modifier = Modifier
                                .align(Alignment.TopStart)
                                .padding(12.dp)
                                .background(
                                    Color.Black.copy(alpha = 0.6f),
                                    RoundedCornerShape(8.dp)
                                )
                                .padding(horizontal = 8.dp, vertical = 4.dp),
                            verticalAlignment = Alignment.CenterVertically
                        ) {
                            Icon(
                                Icons.Filled.LocationOn,
                                contentDescription = null,
                                tint = Color(0xFF00E676),
                                modifier = Modifier.size(14.dp)
                            )
                            Spacer(modifier = Modifier.width(4.dp))
                            Text(
                                text = "%.4f, %.4f".format(latitude, longitude),
                                color = Color.White,
                                fontSize = 10.sp
                            )
                        }

                        // Timestamp overlay (top-right)
                        Text(
                            text = SimpleDateFormat("yyyy-MM-dd HH:mm:ss", Locale.US)
                                .format(Date()),
                            color = Color.White,
                            fontSize = 10.sp,
                            modifier = Modifier
                                .align(Alignment.TopEnd)
                                .padding(12.dp)
                                .background(
                                    Color.Black.copy(alpha = 0.6f),
                                    RoundedCornerShape(8.dp)
                                )
                                .padding(horizontal = 8.dp, vertical = 4.dp)
                        )

                        // Annotation overlay (bottom)
                        if (annotation.isNotEmpty()) {
                            Text(
                                text = annotation,
                                color = Color.Yellow,
                                fontSize = 16.sp,
                                fontWeight = FontWeight.Bold,
                                modifier = Modifier
                                    .align(Alignment.BottomCenter)
                                    .padding(bottom = 16.dp)
                                    .background(
                                        Color.Black.copy(alpha = 0.7f),
                                        RoundedCornerShape(8.dp)
                                    )
                                    .padding(horizontal = 12.dp, vertical = 6.dp)
                            )
                        }
                    }

                    // Annotation input
                    if (showAnnotationInput) {
                        OutlinedTextField(
                            value = annotation,
                            onValueChange = { viewModel.updateAnnotation(it) },
                            label = { Text("Text Annotation") },
                            placeholder = { Text("e.g., Entry point, Damage area...") },
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(horizontal = 16.dp, vertical = 8.dp),
                            singleLine = true
                        )
                    }

                    // Controls bar
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .background(Color(0xFF1A1A2E))
                            .padding(vertical = 24.dp, horizontal = 32.dp),
                        horizontalArrangement = Arrangement.SpaceEvenly,
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        // Flash toggle
                        IconButton(onClick = { /* Toggle flash mode */ }) {
                            Icon(
                                Icons.Filled.FlashAuto,
                                contentDescription = "Flash",
                                tint = Color.White,
                                modifier = Modifier.size(28.dp)
                            )
                        }

                        // Capture button
                        IconButton(
                            onClick = {
                                viewModel.capturePhoto(incidentId, latitude, longitude)
                            },
                            modifier = Modifier
                                .size(72.dp)
                                .clip(CircleShape)
                                .background(Color.White)
                                .border(4.dp, Color(0xFF00E676), CircleShape)
                        ) {
                            Icon(
                                Icons.Filled.Camera,
                                contentDescription = "Capture Photo",
                                tint = Color(0xFF1A1A2E),
                                modifier = Modifier.size(36.dp)
                            )
                        }

                        // Annotation toggle
                        IconButton(onClick = { showAnnotationInput = !showAnnotationInput }) {
                            Icon(
                                Icons.Filled.Edit,
                                contentDescription = "Annotate",
                                tint = if (showAnnotationInput) Color(0xFF00E676) else Color.White,
                                modifier = Modifier.size(28.dp)
                            )
                        }

                        // Flip camera
                        IconButton(onClick = { /* Toggle front/rear camera */ }) {
                            Icon(
                                Icons.Filled.FlipCameraAndroid,
                                contentDescription = "Flip Camera",
                                tint = Color.White,
                                modifier = Modifier.size(28.dp)
                            )
                        }
                    }
                }

                is PhotoCaptureUiState.Capturing -> {
                    Box(
                        modifier = Modifier.fillMaxSize(),
                        contentAlignment = Alignment.Center
                    ) {
                        CircularProgressIndicator(color = Color(0xFF00E676))
                    }
                }

                is PhotoCaptureUiState.Preview -> {
                    // Photo preview with accept/retake
                    Box(
                        modifier = Modifier
                            .fillMaxWidth()
                            .weight(1f)
                            .background(Color(0xFF2A2A3E)),
                        contentAlignment = Alignment.Center
                    ) {
                        Column(horizontalAlignment = Alignment.CenterHorizontally) {
                            Text(
                                text = "Photo Captured",
                                color = Color(0xFF00E676),
                                fontSize = 20.sp,
                                fontWeight = FontWeight.Bold
                            )
                            Spacer(modifier = Modifier.height(8.dp))
                            Text(
                                text = state.evidence.filePath ?: "",
                                color = Color.White.copy(alpha = 0.7f),
                                fontSize = 12.sp
                            )
                            Spacer(modifier = Modifier.height(4.dp))
                            Text(
                                text = "Hash: ${state.evidence.hash.take(24)}...",
                                color = Color.White.copy(alpha = 0.5f),
                                fontSize = 10.sp
                            )
                        }
                    }

                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .background(Color(0xFF1A1A2E))
                            .padding(vertical = 24.dp, horizontal = 32.dp),
                        horizontalArrangement = Arrangement.SpaceEvenly
                    ) {
                        Button(
                            onClick = { viewModel.resetState(); viewModel.onCameraReady() },
                            colors = ButtonDefaults.buttonColors(
                                containerColor = Color(0xFFFF5252)
                            )
                        ) {
                            Icon(Icons.Filled.Close, contentDescription = null)
                            Spacer(modifier = Modifier.width(8.dp))
                            Text("Retake")
                        }

                        Button(
                            onClick = { viewModel.submitAndContinue() },
                            colors = ButtonDefaults.buttonColors(
                                containerColor = Color(0xFF00E676)
                            )
                        ) {
                            Icon(Icons.Filled.Check, contentDescription = null)
                            Spacer(modifier = Modifier.width(8.dp))
                            Text("Accept")
                        }
                    }
                }

                is PhotoCaptureUiState.Submitted -> {
                    // Brief confirmation, then return to camera
                    Box(
                        modifier = Modifier.fillMaxSize(),
                        contentAlignment = Alignment.Center
                    ) {
                        Text(
                            text = "Photo submitted to evidence chain",
                            color = Color(0xFF00E676),
                            fontSize = 16.sp
                        )
                    }
                }

                is PhotoCaptureUiState.Error -> {
                    Box(
                        modifier = Modifier.fillMaxSize(),
                        contentAlignment = Alignment.Center
                    ) {
                        Column(horizontalAlignment = Alignment.CenterHorizontally) {
                            Text(
                                text = state.message,
                                color = Color(0xFFFF5252),
                                fontSize = 16.sp,
                                textAlign = TextAlign.Center
                            )
                            Spacer(modifier = Modifier.height(16.dp))
                            Button(
                                onClick = { viewModel.resetState(); viewModel.onCameraReady() }
                            ) {
                                Text("Try Again")
                            }
                        }
                    }
                }
            }
        }
    }
}
