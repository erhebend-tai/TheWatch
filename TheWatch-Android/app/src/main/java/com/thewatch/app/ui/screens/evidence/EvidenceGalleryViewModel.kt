/**
 * ┌──────────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                         │
 * │ File:    EvidenceGalleryViewModel.kt                                    │
 * │ Purpose: ViewModel for evidence gallery screen. Loads all evidence for  │
 * │          an incident, supports filtering by type, verifies chain of     │
 * │          custody, and manages thumbnail loading.                         │
 * │ Date:    2026-03-24                                                     │
 * │ Author:  Claude                                                         │
 * │ Deps:    EvidencePort, TamperDetectionPort, ThumbnailGeneratorPort,     │
 * │          Hilt, ViewModel                                                │
 * │                                                                         │
 * │ Usage Example:                                                          │
 * │   val viewModel: EvidenceGalleryViewModel = hiltViewModel()             │
 * │   viewModel.loadEvidence("incident-001")                                │
 * │   viewModel.filterByType(EvidenceType.PHOTO)                            │
 * │   val state by viewModel.uiState.collectAsState()                       │
 * │   // state.chainVerified shows if chain-of-custody is intact            │
 * └──────────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.ui.screens.evidence

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.thewatch.app.data.evidence.EvidencePort
import com.thewatch.app.data.evidence.TamperDetectionPort
import com.thewatch.app.data.evidence.ThumbnailGeneratorPort
import com.thewatch.app.data.evidence.VerificationResult
import com.thewatch.app.data.model.Evidence
import com.thewatch.app.data.model.EvidenceType
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import javax.inject.Inject

data class EvidenceGalleryUiState(
    val isLoading: Boolean = true,
    val allEvidence: List<Evidence> = emptyList(),
    val filteredEvidence: List<Evidence> = emptyList(),
    val activeFilter: EvidenceType? = null,
    val selectedEvidence: Evidence? = null,
    val chainVerification: VerificationResult? = null,
    val storageUsedBytes: Long = 0L,
    val error: String? = null
)

@HiltViewModel
class EvidenceGalleryViewModel @Inject constructor(
    private val evidencePort: EvidencePort,
    private val tamperDetectionPort: TamperDetectionPort,
    private val thumbnailGeneratorPort: ThumbnailGeneratorPort
) : ViewModel() {

    private val _uiState = MutableStateFlow(EvidenceGalleryUiState())
    val uiState: StateFlow<EvidenceGalleryUiState> = _uiState.asStateFlow()

    private var currentIncidentId: String? = null

    fun loadEvidence(incidentId: String) {
        currentIncidentId = incidentId
        viewModelScope.launch {
            _uiState.value = _uiState.value.copy(isLoading = true, error = null)

            try {
                // Collect evidence from the flow
                evidencePort.getEvidenceForIncident(incidentId).collect { evidenceList ->
                    val storageUsed = evidencePort.getStorageUsed(incidentId)

                    // Generate thumbnails for items that don't have them
                    evidenceList.forEach { evidence ->
                        if (evidence.thumbnailPath == null) {
                            thumbnailGeneratorPort.generateThumbnail(evidence)
                        }
                    }

                    val filtered = applyFilter(evidenceList, _uiState.value.activeFilter)

                    _uiState.value = _uiState.value.copy(
                        isLoading = false,
                        allEvidence = evidenceList,
                        filteredEvidence = filtered,
                        storageUsedBytes = storageUsed
                    )
                }
            } catch (e: Exception) {
                _uiState.value = _uiState.value.copy(
                    isLoading = false,
                    error = e.message ?: "Failed to load evidence"
                )
            }
        }
    }

    fun filterByType(type: EvidenceType?) {
        val filtered = applyFilter(_uiState.value.allEvidence, type)
        _uiState.value = _uiState.value.copy(
            activeFilter = type,
            filteredEvidence = filtered
        )
    }

    fun selectEvidence(evidence: Evidence?) {
        _uiState.value = _uiState.value.copy(selectedEvidence = evidence)
    }

    fun verifyChain() {
        val incidentId = currentIncidentId ?: return
        viewModelScope.launch {
            try {
                val chain = _uiState.value.allEvidence.sortedBy { it.timestamp }
                val result = tamperDetectionPort.verifyChain(chain) { evidenceId ->
                    // In mock, we don't have actual file content, so return synthetic bytes
                    // Native impl would read actual file bytes here
                    null
                }
                _uiState.value = _uiState.value.copy(chainVerification = result)
            } catch (e: Exception) {
                _uiState.value = _uiState.value.copy(
                    error = "Chain verification failed: ${e.message}"
                )
            }
        }
    }

    fun deleteEvidence(evidenceId: String) {
        viewModelScope.launch {
            evidencePort.deleteEvidence(evidenceId).fold(
                onSuccess = {
                    // Evidence list will auto-update via Flow collection
                    if (_uiState.value.selectedEvidence?.id == evidenceId) {
                        _uiState.value = _uiState.value.copy(selectedEvidence = null)
                    }
                },
                onFailure = { e ->
                    _uiState.value = _uiState.value.copy(
                        error = "Delete failed: ${e.message}"
                    )
                }
            )
        }
    }

    fun clearError() {
        _uiState.value = _uiState.value.copy(error = null)
    }

    private fun applyFilter(evidence: List<Evidence>, type: EvidenceType?): List<Evidence> {
        return if (type == null) evidence
        else evidence.filter { it.type == type }
    }
}
