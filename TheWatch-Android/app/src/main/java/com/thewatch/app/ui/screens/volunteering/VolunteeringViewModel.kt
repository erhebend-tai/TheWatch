package com.thewatch.app.ui.screens.volunteering

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.thewatch.app.data.repository.VolunteerRepository
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import javax.inject.Inject

sealed class VolunteeringUiState {
    object Idle : VolunteeringUiState()
    object Loading : VolunteeringUiState()
    object Success : VolunteeringUiState()
    data class Error(val message: String) : VolunteeringUiState()
}

@HiltViewModel
class VolunteeringViewModel @Inject constructor(
    private val volunteerRepository: VolunteerRepository
) : ViewModel() {
    private val _uiState = MutableStateFlow<VolunteeringUiState>(VolunteeringUiState.Idle)
    val uiState: StateFlow<VolunteeringUiState> = _uiState.asStateFlow()

    private val _isEnrolled = MutableStateFlow(false)
    val isEnrolled: StateFlow<Boolean> = _isEnrolled.asStateFlow()

    private val _selectedRole = MutableStateFlow("First Responder")
    val selectedRole: StateFlow<String> = _selectedRole.asStateFlow()

    private val _responseHistory = MutableStateFlow(0)
    val responseHistory: StateFlow<Int> = _responseHistory.asStateFlow()

    init {
        loadVolunteerStatus()
    }

    fun loadVolunteerStatus() {
        viewModelScope.launch {
            _uiState.value = VolunteeringUiState.Loading
            try {
                val status = volunteerRepository.getVolunteerStatus()
                _isEnrolled.value = status.isEnrolled
                _selectedRole.value = status.role
                _responseHistory.value = status.responseCount
                _uiState.value = VolunteeringUiState.Success
            } catch (e: Exception) {
                _uiState.value = VolunteeringUiState.Error(e.message ?: "Failed to load volunteer status")
            }
        }
    }

    fun updateEnrollmentStatus(isEnrolled: Boolean) {
        viewModelScope.launch {
            _uiState.value = VolunteeringUiState.Loading
            try {
                volunteerRepository.updateEnrollmentStatus(isEnrolled)
                _isEnrolled.value = isEnrolled
                _uiState.value = VolunteeringUiState.Success
            } catch (e: Exception) {
                _uiState.value = VolunteeringUiState.Error(e.message ?: "Failed to update enrollment")
            }
        }
    }

    fun updateRole(role: String) {
        viewModelScope.launch {
            _uiState.value = VolunteeringUiState.Loading
            try {
                volunteerRepository.updateRole(role)
                _selectedRole.value = role
                _uiState.value = VolunteeringUiState.Success
            } catch (e: Exception) {
                _uiState.value = VolunteeringUiState.Error(e.message ?: "Failed to update role")
            }
        }
    }

    fun updateSkills(skills: List<String>) {
        viewModelScope.launch {
            _uiState.value = VolunteeringUiState.Loading
            try {
                volunteerRepository.updateSkills(skills)
                _uiState.value = VolunteeringUiState.Success
            } catch (e: Exception) {
                _uiState.value = VolunteeringUiState.Error(e.message ?: "Failed to update skills")
            }
        }
    }

    fun updateWeeklySchedule(schedule: Map<String, List<String>>) {
        viewModelScope.launch {
            _uiState.value = VolunteeringUiState.Loading
            try {
                volunteerRepository.updateWeeklySchedule(schedule)
                _uiState.value = VolunteeringUiState.Success
            } catch (e: Exception) {
                _uiState.value = VolunteeringUiState.Error(e.message ?: "Failed to update schedule")
            }
        }
    }
}
