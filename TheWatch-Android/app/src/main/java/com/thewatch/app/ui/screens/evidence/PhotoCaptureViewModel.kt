/**
 * ┌──────────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                         │
 * │ File:    PhotoCaptureViewModel.kt                                       │
 * │ Purpose: ViewModel for photo capture screen. Manages CameraX lifecycle, │
 * │          capture state, annotation input, and submission to evidence     │
 * │          chain via EvidencePort.                                         │
 * │ Date:    2026-03-24                                                     │
 * │ Author:  Claude                                                         │
 * │ Deps:    EvidencePort, Hilt, ViewModel, CameraX                         │
 * │                                                                         │
 * │ Usage Example:                                                          │
 * │   // In PhotoCaptureScreen composable:                                  │
 * │   val viewModel: PhotoCaptureViewModel = hiltViewModel()                │
 * │   viewModel.capturePhoto("incident-001", 30.27, -97.74, "Entry point") │
 * │   val state by viewModel.uiState.collectAsState()                       │
 * └──────────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.ui.screens.evidence

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.thewatch.app.data.evidence.EvidencePort
import com.thewatch.app.data.model.Evidence
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import javax.inject.Inject

sealed class PhotoCaptureUiState {
    object Idle : PhotoCaptureUiState()
    object CameraReady : PhotoCaptureUiState()
    object Capturing : PhotoCaptureUiState()
    data class Preview(val evidence: Evidence) : PhotoCaptureUiState()
    data class Submitted(val evidence: Evidence) : PhotoCaptureUiState()
    data class Error(val message: String) : PhotoCaptureUiState()
}

@HiltViewModel
class PhotoCaptureViewModel @Inject constructor(
    private val evidencePort: EvidencePort
) : ViewModel() {

    private val _uiState = MutableStateFlow<PhotoCaptureUiState>(PhotoCaptureUiState.Idle)
    val uiState: StateFlow<PhotoCaptureUiState> = _uiState.asStateFlow()

    private val _annotation = MutableStateFlow("")
    val annotation: StateFlow<String> = _annotation.asStateFlow()

    private val _capturedPhotos = MutableStateFlow<List<Evidence>>(emptyList())
    val capturedPhotos: StateFlow<List<Evidence>> = _capturedPhotos.asStateFlow()

    fun onCameraReady() {
        _uiState.value = PhotoCaptureUiState.CameraReady
    }

    fun updateAnnotation(text: String) {
        _annotation.value = text
    }

    fun capturePhoto(
        incidentId: String,
        latitude: Double,
        longitude: Double,
        annotation: String? = null
    ) {
        viewModelScope.launch {
            _uiState.value = PhotoCaptureUiState.Capturing
            val result = evidencePort.capturePhoto(
                incidentId = incidentId,
                latitude = latitude,
                longitude = longitude,
                annotation = annotation ?: _annotation.value.ifEmpty { null }
            )
            result.fold(
                onSuccess = { evidence ->
                    _capturedPhotos.value = _capturedPhotos.value + evidence
                    _uiState.value = PhotoCaptureUiState.Preview(evidence)
                    _annotation.value = ""
                },
                onFailure = { e ->
                    _uiState.value = PhotoCaptureUiState.Error(
                        e.message ?: "Photo capture failed"
                    )
                }
            )
        }
    }

    fun submitAndContinue() {
        val current = _uiState.value
        if (current is PhotoCaptureUiState.Preview) {
            _uiState.value = PhotoCaptureUiState.Submitted(current.evidence)
            // Return to camera ready for next capture
            _uiState.value = PhotoCaptureUiState.CameraReady
        }
    }

    fun resetState() {
        _uiState.value = PhotoCaptureUiState.Idle
        _annotation.value = ""
    }
}
