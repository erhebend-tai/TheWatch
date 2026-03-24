/**
 * ┌──────────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                         │
 * │ File:    AudioRecordScreen.kt                                           │
 * │ Purpose: Audio recording UI with waveform visualization, duration       │
 * │          display, record/stop controls, auto-transcription placeholder, │
 * │          and submission to evidence chain.                              │
 * │ Date:    2026-03-24                                                     │
 * │ Author:  Claude                                                         │
 * │ Deps:    AudioRecordViewModel, MediaRecorder, Compose, Material 3       │
 * │                                                                         │
 * │ Usage Example:                                                          │
 * │   composable("audio_record/{incidentId}") { backStackEntry ->           │
 * │       AudioRecordScreen(                                                │
 * │           navController = navController,                                │
 * │           incidentId = backStackEntry.arguments?.getString("incidentId")│
 * │       )                                                                 │
 * │   }                                                                     │
 * │                                                                         │
 * │ Waveform Visualization:                                                 │
 * │   - Draws vertical amplitude bars in a Canvas                           │
 * │   - Scrolls left as new samples arrive                                  │
 * │   - Green bars for normal, yellow for loud, red for clipping            │
 * │   - In native impl, reads MediaRecorder.getMaxAmplitude() every 50ms   │
 * └──────────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.ui.screens.evidence

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
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
import androidx.compose.material.icons.filled.Check
import androidx.compose.material.icons.filled.Mic
import androidx.compose.material.icons.filled.Stop
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.hilt.navigation.compose.hiltViewModel
import androidx.navigation.NavController

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun AudioRecordScreen(
    navController: NavController,
    incidentId: String,
    latitude: Double = 0.0,
    longitude: Double = 0.0,
    viewModel: AudioRecordViewModel = hiltViewModel()
) {
    val uiState by viewModel.uiState.collectAsState()
    val elapsedMillis by viewModel.elapsedMillis.collectAsState()
    val waveformAmplitudes by viewModel.waveformAmplitudes.collectAsState()

    LaunchedEffect(Unit) {
        viewModel.onReady()
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Audio Recording") },
                navigationIcon = {
                    IconButton(onClick = {
                        viewModel.resetState()
                        navController.popBackStack()
                    }) {
                        Icon(Icons.Filled.ArrowBack, contentDescription = "Back")
                    }
                },
                colors = TopAppBarDefaults.topAppBarColors(
                    containerColor = Color(0xFF1A1A2E),
                    titleContentColor = Color.White,
                    navigationIconContentColor = Color.White
                )
            )
        }
    ) { padding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
                .background(Color(0xFF0D0D1A)),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            when (val state = uiState) {
                is AudioRecordUiState.Idle,
                is AudioRecordUiState.Ready -> {
                    Spacer(modifier = Modifier.weight(1f))

                    // Mic icon
                    Icon(
                        Icons.Filled.Mic,
                        contentDescription = null,
                        tint = Color.White.copy(alpha = 0.3f),
                        modifier = Modifier.size(80.dp)
                    )
                    Spacer(modifier = Modifier.height(16.dp))
                    Text(
                        text = "Tap to start recording",
                        color = Color.White.copy(alpha = 0.5f),
                        fontSize = 16.sp
                    )
                    Text(
                        text = "Voice memo for incident evidence",
                        color = Color.White.copy(alpha = 0.3f),
                        fontSize = 12.sp
                    )

                    Spacer(modifier = Modifier.weight(1f))

                    // Record button
                    IconButton(
                        onClick = {
                            viewModel.startRecording(incidentId, latitude, longitude)
                        },
                        modifier = Modifier
                            .size(80.dp)
                            .clip(CircleShape)
                            .background(Color(0xFFFF1744))
                    ) {
                        Icon(
                            Icons.Filled.Mic,
                            contentDescription = "Start Recording",
                            tint = Color.White,
                            modifier = Modifier.size(40.dp)
                        )
                    }

                    Spacer(modifier = Modifier.height(48.dp))
                }

                is AudioRecordUiState.Recording -> {
                    Spacer(modifier = Modifier.height(32.dp))

                    // Duration display
                    Text(
                        text = formatAudioDuration(state.elapsedMillis),
                        color = Color.White,
                        fontSize = 48.sp,
                        fontWeight = FontWeight.Light,
                        fontFamily = FontFamily.Monospace
                    )

                    Spacer(modifier = Modifier.height(32.dp))

                    // Waveform visualization
                    WaveformVisualizer(
                        amplitudes = waveformAmplitudes,
                        modifier = Modifier
                            .fillMaxWidth()
                            .height(120.dp)
                            .padding(horizontal = 16.dp)
                    )

                    Spacer(modifier = Modifier.height(16.dp))

                    // Recording indicator
                    Text(
                        text = "Recording...",
                        color = Color(0xFFFF1744),
                        fontSize = 14.sp,
                        fontWeight = FontWeight.Bold
                    )

                    Spacer(modifier = Modifier.weight(1f))

                    // Stop button
                    IconButton(
                        onClick = { viewModel.stopRecording() },
                        modifier = Modifier
                            .size(80.dp)
                            .clip(CircleShape)
                            .background(Color(0xFFFF1744))
                    ) {
                        Icon(
                            Icons.Filled.Stop,
                            contentDescription = "Stop Recording",
                            tint = Color.White,
                            modifier = Modifier.size(40.dp)
                        )
                    }

                    Spacer(modifier = Modifier.height(48.dp))
                }

                is AudioRecordUiState.Processing -> {
                    Box(
                        modifier = Modifier.fillMaxSize(),
                        contentAlignment = Alignment.Center
                    ) {
                        Column(horizontalAlignment = Alignment.CenterHorizontally) {
                            CircularProgressIndicator(color = Color(0xFF00E676))
                            Spacer(modifier = Modifier.height(16.dp))
                            Text(
                                text = "Processing audio...",
                                color = Color.White,
                                fontSize = 14.sp
                            )
                        }
                    }
                }

                is AudioRecordUiState.Preview -> {
                    Spacer(modifier = Modifier.height(32.dp))

                    Text(
                        text = "Audio Recorded",
                        color = Color(0xFF00E676),
                        fontSize = 24.sp,
                        fontWeight = FontWeight.Bold
                    )

                    Spacer(modifier = Modifier.height(8.dp))

                    Text(
                        text = "Duration: ${formatAudioDuration(state.evidence.durationMillis ?: 0)}",
                        color = Color.White,
                        fontSize = 16.sp
                    )

                    Spacer(modifier = Modifier.height(24.dp))

                    // Auto-transcription card
                    if (state.transcription != null) {
                        Card(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(horizontal = 16.dp),
                            colors = CardDefaults.cardColors(
                                containerColor = Color(0xFF1A1A2E)
                            )
                        ) {
                            Column(modifier = Modifier.padding(16.dp)) {
                                Text(
                                    text = "Auto-Transcription (Preview)",
                                    color = Color(0xFF00E676),
                                    fontSize = 12.sp,
                                    fontWeight = FontWeight.Bold
                                )
                                Spacer(modifier = Modifier.height(8.dp))
                                Text(
                                    text = state.transcription,
                                    color = Color.White.copy(alpha = 0.7f),
                                    fontSize = 14.sp
                                )
                            }
                        }
                    }

                    Spacer(modifier = Modifier.height(16.dp))

                    // Hash info
                    Text(
                        text = "Hash: ${state.evidence.hash.take(24)}...",
                        color = Color.White.copy(alpha = 0.4f),
                        fontSize = 10.sp
                    )

                    Spacer(modifier = Modifier.weight(1f))

                    // Action buttons
                    Row(
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(horizontal = 32.dp, vertical = 24.dp),
                        horizontalArrangement = Arrangement.SpaceEvenly
                    ) {
                        Button(
                            onClick = { viewModel.resetState(); viewModel.onReady() },
                            colors = ButtonDefaults.buttonColors(
                                containerColor = Color(0xFFFF5252)
                            )
                        ) {
                            Text("Discard")
                        }

                        Button(
                            onClick = {
                                viewModel.submitAudio()
                                navController.popBackStack()
                            },
                            colors = ButtonDefaults.buttonColors(
                                containerColor = Color(0xFF00E676)
                            )
                        ) {
                            Icon(Icons.Filled.Check, contentDescription = null)
                            Spacer(modifier = Modifier.width(8.dp))
                            Text("Submit")
                        }
                    }
                }

                is AudioRecordUiState.Submitted -> {
                    Box(
                        modifier = Modifier.fillMaxSize(),
                        contentAlignment = Alignment.Center
                    ) {
                        Text(
                            text = "Audio submitted to evidence chain",
                            color = Color(0xFF00E676),
                            fontSize = 16.sp
                        )
                    }
                }

                is AudioRecordUiState.Error -> {
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
                            Button(onClick = { viewModel.resetState(); viewModel.onReady() }) {
                                Text("Try Again")
                            }
                        }
                    }
                }
            }
        }
    }
}

/**
 * Waveform visualization composable.
 * Draws vertical bars representing audio amplitude values (0.0 - 1.0).
 * Color-coded: green (normal), yellow (loud), red (clipping).
 */
@Composable
fun WaveformVisualizer(
    amplitudes: List<Float>,
    modifier: Modifier = Modifier
) {
    Canvas(
        modifier = modifier
            .clip(RoundedCornerShape(8.dp))
            .background(Color(0xFF1A1A2E))
    ) {
        val barWidth = 3.dp.toPx()
        val gap = 1.dp.toPx()
        val totalBarWidth = barWidth + gap
        val maxBars = (size.width / totalBarWidth).toInt()
        val centerY = size.height / 2f

        val displayAmplitudes = if (amplitudes.size > maxBars) {
            amplitudes.takeLast(maxBars)
        } else {
            amplitudes
        }

        displayAmplitudes.forEachIndexed { index, amplitude ->
            val barHeight = amplitude * size.height * 0.8f
            val x = index * totalBarWidth

            val color = when {
                amplitude > 0.85f -> Color(0xFFFF1744) // Red — clipping
                amplitude > 0.6f -> Color(0xFFFFD600) // Yellow — loud
                else -> Color(0xFF00E676) // Green — normal
            }

            drawLine(
                color = color,
                start = Offset(x + barWidth / 2, centerY - barHeight / 2),
                end = Offset(x + barWidth / 2, centerY + barHeight / 2),
                strokeWidth = barWidth
            )
        }

        // Center line
        drawLine(
            color = Color.White.copy(alpha = 0.2f),
            start = Offset(0f, centerY),
            end = Offset(size.width, centerY),
            strokeWidth = 1.dp.toPx()
        )
    }
}

private fun formatAudioDuration(millis: Long): String {
    val totalSeconds = millis / 1000
    val minutes = totalSeconds / 60
    val seconds = totalSeconds % 60
    return "%02d:%02d".format(minutes, seconds)
}
