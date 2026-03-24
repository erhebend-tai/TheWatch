/**
 * ┌──────────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                         │
 * │ File:    SitrepViewModel.kt                                             │
 * │ Purpose: ViewModel for structured situation report (SITREP) submission.  │
 * │          Manages form state: situation type, severity, description,      │
 * │          attached photos, and location. Submits via EvidencePort.        │
 * │ Date:    2026-03-24                                                     │
 * │ Author:  Claude                                                         │
 * │ Deps:    EvidencePort, Hilt, ViewModel                                  │
 * │                                                                         │
 * │ Usage Example:                                                          │
 * │   val viewModel: SitrepViewModel = hiltViewModel()                      │
 * │   viewModel.updateSituationType(SituationType.MEDICAL_EMERGENCY)        │
 * │   viewModel.updateSeverity(SitrepSeverity.HIGH)                         │
 * │   viewModel.updateDescription("Victim conscious but bleeding")          │
 * │   viewModel.submitSitrep("incident-001", 30.27, -97.74)                │
 * └──────────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.ui.screens.evidence

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.thewatch.app.data.evidence.EvidencePort
import com.thewatch.app.data.model.Evidence
import com.thewatch.app.data.model.SitrepSeverity
import com.thewatch.app.data.model.SituationType
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import javax.inject.Inject

sealed class SitrepUiState {
    object Editing : SitrepUiState()
    object Submitting : SitrepUiState()
    data class Submitted(val evidence: Evidence) : SitrepUiState()
    data class Error(val message: String) : SitrepUiState()
}

data class SitrepFormState(
    val situationType: SituationType = SituationType.OTHER,
    val severity: SitrepSeverity = SitrepSeverity.MEDIUM,
    val description: String = "",
    val attachedEvidenceIds: List<String> = emptyList()
)

@HiltViewModel
class SitrepViewModel @Inject constructor(
    private val evidencePort: EvidencePort
) : ViewModel() {

    private val _uiState = MutableStateFlow<SitrepUiState>(SitrepUiState.Editing)
    val uiState: StateFlow<SitrepUiState> = _uiState.asStateFlow()

    private val _formState = MutableStateFlow(SitrepFormState())
    val formState: StateFlow<SitrepFormState> = _formState.asStateFlow()

    private val _submittedSitreps = MutableStateFlow<List<Evidence>>(emptyList())
    val submittedSitreps: StateFlow<List<Evidence>> = _submittedSitreps.asStateFlow()

    fun updateSituationType(type: SituationType) {
        _formState.value = _formState.value.copy(situationType = type)
    }

    fun updateSeverity(severity: SitrepSeverity) {
        _formState.value = _formState.value.copy(severity = severity)
    }

    fun updateDescription(description: String) {
        _formState.value = _formState.value.copy(description = description)
    }

    fun attachEvidence(evidenceId: String) {
        val current = _formState.value.attachedEvidenceIds
        if (evidenceId !in current) {
            _formState.value = _formState.value.copy(
                attachedEvidenceIds = current + evidenceId
            )
        }
    }

    fun detachEvidence(evidenceId: String) {
        _formState.value = _formState.value.copy(
            attachedEvidenceIds = _formState.value.attachedEvidenceIds - evidenceId
        )
    }

    fun submitSitrep(
        incidentId: String,
        latitude: Double? = null,
        longitude: Double? = null
    ) {
        val form = _formState.value

        if (form.description.isBlank()) {
            _uiState.value = SitrepUiState.Error("Description is required")
            return
        }

        viewModelScope.launch {
            _uiState.value = SitrepUiState.Submitting
            val result = evidencePort.submitSitrep(
                incidentId = incidentId,
                situationType = form.situationType,
                severity = form.severity,
                description = form.description,
                attachedEvidenceIds = form.attachedEvidenceIds,
                latitude = latitude,
                longitude = longitude
            )
            result.fold(
                onSuccess = { evidence ->
                    _submittedSitreps.value = _submittedSitreps.value + evidence
                    _uiState.value = SitrepUiState.Submitted(evidence)
                },
                onFailure = { e ->
                    _uiState.value = SitrepUiState.Error(
                        e.message ?: "Sitrep submission failed"
                    )
                }
            )
        }
    }

    fun resetForm() {
        _formState.value = SitrepFormState()
        _uiState.value = SitrepUiState.Editing
    }
}
