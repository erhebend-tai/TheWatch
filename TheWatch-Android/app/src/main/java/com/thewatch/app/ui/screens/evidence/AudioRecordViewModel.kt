/**
 * ┌──────────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                         │
 * │ File:    AudioRecordViewModel.kt                                        │
 * │ Purpose: ViewModel for audio recording screen. Manages MediaRecorder    │
 * │          lifecycle, waveform amplitude sampling, recording duration,     │
 * │          and submission via EvidencePort.                                │
 * │ Date:    2026-03-24                                                     │
 * │ Author:  Claude                                                         │
 * │ Deps:    EvidencePort, Hilt, ViewModel, MediaRecorder                   │
 * │                                                                         │
 * │ Usage Example:                                                          │
 * │   val viewModel: AudioRecordViewModel = hiltViewModel()                 │
 * │   viewModel.startRecording("incident-001", 30.27, -97.74)              │
 * │   // Observe waveform amplitudes for visualization:                     │
 * │   val amplitudes by viewModel.waveformAmplitudes.collectAsState()       │
 * │   viewModel.stopRecording()                                             │
 * │                                                                         │
 * │ Auto-Transcription:                                                     │
 * │   Placeholder for future integration with:                              │
 * │   - Android SpeechRecognizer (on-device)                                │
 * │   - Google Cloud Speech-to-Text                                         │
 * │   - Whisper (OpenAI) via local ONNX Runtime                             │
 * │   - Azure Cognitive Services Speech                                     │
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
import kotlin.random.Random

sealed class AudioRecordUiState {
    object Idle : AudioRecordUiState()
    object Ready : AudioRecordUiState()
    data class Recording(val elapsedMillis: Long) : AudioRecordUiState()
    object Processing : AudioRecordUiState()
    data class Preview(val evidence: Evidence, val transcription: String?) : AudioRecordUiState()
    data class Submitted(val evidence: Evidence) : AudioRecordUiState()
    data class Error(val message: String) : AudioRecordUiState()
}

@HiltViewModel
class AudioRecordViewModel @Inject constructor(
    private val evidencePort: EvidencePort
) : ViewModel() {

    companion object {
        private const val AMPLITUDE_SAMPLE_INTERVAL_MS = 50L
        private const val MAX_WAVEFORM_SAMPLES = 300
    }

    private val _uiState = MutableStateFlow<AudioRecordUiState>(AudioRecordUiState.Idle)
    val uiState: StateFlow<AudioRecordUiState> = _uiState.asStateFlow()

    private val _elapsedMillis = MutableStateFlow(0L)
    val elapsedMillis: StateFlow<Long> = _elapsedMillis.asStateFlow()

    /** Normalized amplitude values (0.0 - 1.0) for waveform visualization */
    private val _waveformAmplitudes = MutableStateFlow<List<Float>>(emptyList())
    val waveformAmplitudes: StateFlow<List<Float>> = _waveformAmplitudes.asStateFlow()

    private var recordingJob: Job? = null
    private var recordingIncidentId: String? = null
    private var recordingLat: Double = 0.0
    private var recordingLng: Double = 0.0

    fun onReady() {
        _uiState.value = AudioRecordUiState.Ready
    }

    fun startRecording(
        incidentId: String,
        latitude: Double,
        longitude: Double
    ) {
        recordingIncidentId = incidentId
        recordingLat = latitude
        recordingLng = longitude
        _elapsedMillis.value = 0L
        _waveformAmplitudes.value = emptyList()
        _uiState.value = AudioRecordUiState.Recording(0L)

        // Simulate recording with amplitude sampling
        // In native impl, this reads MediaRecorder.getMaxAmplitude()
        recordingJob = viewModelScope.launch {
            var elapsed = 0L
            val amplitudes = mutableListOf<Float>()
            while (true) {
                delay(AMPLITUDE_SAMPLE_INTERVAL_MS)
                elapsed += AMPLITUDE_SAMPLE_INTERVAL_MS
                _elapsedMillis.value = elapsed

                // Mock amplitude — native impl reads actual MediaRecorder amplitude
                val mockAmplitude = (Random.nextFloat() * 0.6f + 0.1f)
                    .coerceIn(0f, 1f)
                amplitudes.add(mockAmplitude)
                if (amplitudes.size > MAX_WAVEFORM_SAMPLES) {
                    amplitudes.removeAt(0)
                }
                _waveformAmplitudes.value = amplitudes.toList()
                _uiState.value = AudioRecordUiState.Recording(elapsed)
            }
        }
    }

    fun stopRecording() {
        recordingJob?.cancel()
        recordingJob = null

        val incidentId = recordingIncidentId ?: return

        viewModelScope.launch {
            _uiState.value = AudioRecordUiState.Processing
            val result = evidencePort.recordAudio(
                incidentId = incidentId,
                latitude = recordingLat,
                longitude = recordingLng
            )
            result.fold(
                onSuccess = { evidence ->
                    // Placeholder auto-transcription
                    val transcription = evidence.metadata["transcription"]
                    _uiState.value = AudioRecordUiState.Preview(evidence, transcription)
                },
                onFailure = { e ->
                    _uiState.value = AudioRecordUiState.Error(
                        e.message ?: "Audio recording failed"
                    )
                }
            )
        }
    }

    fun submitAudio() {
        val current = _uiState.value
        if (current is AudioRecordUiState.Preview) {
            _uiState.value = AudioRecordUiState.Submitted(current.evidence)
        }
    }

    fun resetState() {
        recordingJob?.cancel()
        recordingJob = null
        _uiState.value = AudioRecordUiState.Idle
        _elapsedMillis.value = 0L
        _waveformAmplitudes.value = emptyList()
        recordingIncidentId = null
    }
}
