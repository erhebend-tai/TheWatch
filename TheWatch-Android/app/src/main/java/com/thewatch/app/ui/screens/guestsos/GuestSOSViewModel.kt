/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         GuestSOSViewModel.kt                                   │
 * │ Purpose:      ViewModel for the Guest SOS screen. Manages            │
 * │               unauthenticated emergency alert dispatch. This screen  │
 * │               bypasses login entirely — anyone who picks up the      │
 * │               phone should be able to send an SOS.                   │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: AlertRepository, LocationRepository, Hilt              │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   // In GuestSOSScreen composable:                                   │
 * │   val viewModel: GuestSOSViewModel = hiltViewModel()                 │
 * │   val state by viewModel.uiState.collectAsState()                    │
 * │   GuestSOSContent(                                                   │
 * │       state = state,                                                 │
 * │       onActivateSOS = { viewModel.activateGuestSOS() },             │
 * │       onCancel = { viewModel.cancelSOS() }                           │
 * │   )                                                                  │
 * │                                                                      │
 * │ IMPORTANT: This feature exists because in a true emergency, auth     │
 * │ should NEVER be a barrier. A bystander, a child, or someone in       │
 * │ distress must be able to summon help with zero friction.             │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.ui.screens.guestsos

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.thewatch.app.data.model.Alert
import com.thewatch.app.data.repository.AlertRepository
import com.thewatch.app.data.repository.LocationRepository
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import javax.inject.Inject

@HiltViewModel
class GuestSOSViewModel @Inject constructor(
    private val alertRepository: AlertRepository,
    private val locationRepository: LocationRepository
) : ViewModel() {

    private val _uiState = MutableStateFlow(GuestSOSUiState())
    val uiState: StateFlow<GuestSOSUiState> = _uiState.asStateFlow()

    init {
        // Start fetching location immediately when screen opens
        fetchLocation()
    }

    private fun fetchLocation() {
        viewModelScope.launch {
            try {
                val location = locationRepository.getLastKnownLocation()
                _uiState.value = _uiState.value.copy(
                    latitude = location?.latitude ?: 0.0,
                    longitude = location?.longitude ?: 0.0,
                    locationAvailable = location != null
                )
            } catch (e: Exception) {
                _uiState.value = _uiState.value.copy(
                    locationAvailable = false,
                    errorMessage = "Could not determine location"
                )
            }
        }
    }

    fun activateGuestSOS() {
        viewModelScope.launch {
            _uiState.value = _uiState.value.copy(
                sosActive = true,
                isLoading = true,
                errorMessage = null
            )
            try {
                val guestAlert = Alert(
                    id = "guest_sos_${System.currentTimeMillis()}",
                    userId = "GUEST_ANONYMOUS",
                    severity = "CRITICAL",
                    type = "GUEST_SOS",
                    latitude = _uiState.value.latitude,
                    longitude = _uiState.value.longitude,
                    timestamp = System.currentTimeMillis(),
                    status = "ACTIVE",
                    description = "Guest emergency SOS -- unauthenticated user",
                    triggeredBy = "USER"
                )
                alertRepository.activateAlert(guestAlert)
                _uiState.value = _uiState.value.copy(
                    isLoading = false,
                    sosDispatched = true
                )
            } catch (e: Exception) {
                _uiState.value = _uiState.value.copy(
                    isLoading = false,
                    errorMessage = e.message ?: "Failed to dispatch SOS"
                )
            }
        }
    }

    fun cancelSOS() {
        _uiState.value = _uiState.value.copy(
            sosActive = false,
            sosDispatched = false,
            errorMessage = null
        )
    }

    fun refreshLocation() {
        fetchLocation()
    }
}

data class GuestSOSUiState(
    val sosActive: Boolean = false,
    val sosDispatched: Boolean = false,
    val isLoading: Boolean = false,
    val latitude: Double = 0.0,
    val longitude: Double = 0.0,
    val locationAvailable: Boolean = false,
    val errorMessage: String? = null
)
