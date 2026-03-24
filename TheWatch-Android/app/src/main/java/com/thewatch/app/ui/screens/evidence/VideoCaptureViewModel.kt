/**
 * ┌──────────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                         │
 * │ File:    VideoCaptureViewModel.kt                                       │
 * │ Purpose: ViewModel for video capture screen. Manages recording state,   │
 * │          60s max duration timer, auto-stop, and submission via           │
 * │          EvidencePort.                                                   │
 * │ Date:    2026-03-24                                                     │
 * │ Author:  Claude                                                         │
 * │ Deps:    EvidencePort, Hilt, ViewModel, CameraX VideoCapture            │
 * │                                                                         │
 * │ Usage Example:                                                          │
 * │   val viewModel: VideoCaptureViewModel = hiltViewModel()                │
 * │   viewModel.startRecording("incident-001", 30.27, -97.74)              │
 * │   // ... after 60s or manual stop:                                      │
 * │   viewModel.stopRecording()                                             │
 * │   val state by viewModel.uiState.collectAsState()                       │
 * └──────────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.ui.screens.evidence

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.thewatch.app.data.evidence.EvidencePort
import com.thewatch.app.data.model.Evidence
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import javax.inject.Inject

sealed class VideoCaptureUiState {
    object Idle : VideoCaptureUiState()
    object CameraReady : VideoCaptureUiState()
    data class Recording(val elapsedMillis: Long, val maxMillis: Long) : VideoCaptureUiState()
    object Processing : VideoCaptureUiState()
    data class Preview(val evidence: Evidence) : VideoCaptureUiState()
    data class Submitted(val evidence: Evidence) : VideoCaptureUiState()
    data class Error(val message: String) : VideoCaptureUiState()
}

@HiltViewModel
class VideoCaptureViewModel @Inject constructor(
    private val evidencePort: EvidencePort
) : ViewModel() {

    companion object {
        const val MAX_DURATION_MILLIS = 60_000L
        private const val TIMER_TICK_MILLIS = 100L
    }

    private val _uiState = MutableStateFlow<VideoCaptureUiState>(VideoCaptureUiState.Idle)
    val uiState: StateFlow<VideoCaptureUiState> = _uiState.asStateFlow()

    private val _elapsedMillis = MutableStateFlow(0L)
    val elapsedMillis: StateFlow<Long> = _elapsedMillis.asStateFlow()

    private var timerJob: Job? = null
    private var recordingIncidentId: String? = null
    private var recordingLat: Double = 0.0
    private var recordingLng: Double = 0.0

    fun onCameraReady() {
        _uiState.value = VideoCaptureUiState.CameraReady
    }

    fun startRecording(
        incidentId: String,
        latitude: Double,
        longitude: Double,
        maxDurationMillis: Long = MAX_DURATION_MILLIS
    ) {
        recordingIncidentId = incidentId
        recordingLat = latitude
        recordingLng = longitude
        _elapsedMillis.value = 0L
        _uiState.value = VideoCaptureUiState.Recording(0L, maxDurationMillis)

        // Timer countdown — auto-stops at max duration
        timerJob = viewModelScope.launch {
            var elapsed = 0L
            while (elapsed < maxDurationMillis) {
                delay(TIMER_TICK_MILLIS)
                elapsed += TIMER_TICK_MILLIS
                _elapsedMillis.value = elapsed
                _uiState.value = VideoCaptureUiState.Recording(elapsed, maxDurationMillis)
            }
            // Auto-stop at max duration
            stopRecording()
        }
    }

    fun stopRecording() {
        timerJob?.cancel()
        timerJob = null

        val incidentId = recordingIncidentId ?: return

        viewModelScope.launch {
            _uiState.value = VideoCaptureUiState.Processing
            val result = evidencePort.captureVideo(
                incidentId = incidentId,
                latitude = recordingLat,
                longitude = recordingLng,
                maxDurationMillis = _elapsedMillis.value
            )
            result.fold(
                onSuccess = { evidence ->
                    _uiState.value = VideoCaptureUiState.Preview(evidence)
                },
                onFailure = { e ->
                    _uiState.value = VideoCaptureUiState.Error(
                        e.message ?: "Video capture failed"
                    )
                }
            )
        }
    }

    fun submitVideo() {
        val current = _uiState.value
        if (current is VideoCaptureUiState.Preview) {
            _uiState.value = VideoCaptureUiState.Submitted(current.evidence)
        }
    }

    fun resetState() {
        timerJob?.cancel()
        timerJob = null
        _uiState.value = VideoCaptureUiState.Idle
        _elapsedMillis.value = 0L
        recordingIncidentId = null
    }
}
