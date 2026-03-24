/**
 * ═══════════════════════════════════════════════════════════════════════════════
 * WRITE-AHEAD LOG — CheckInScheduleViewModel.kt
 * ═══════════════════════════════════════════════════════════════════════════════
 * Purpose:   ViewModel for the Check-In Schedule configuration screen.
 *            Manages UI state for interval selection (daily/12h/6h/custom),
 *            quiet hours, enable/disable, and preview of next check-in time.
 * Date:      2026-03-24
 * Author:    Claude (Anthropic)
 * Deps:      Hilt, CheckInSchedulePort, ViewModel, StateFlow
 * Package:   com.thewatch.app.ui.screens.checkin
 *
 * Usage Example:
 *   // In CheckInScheduleScreen composable:
 *   val viewModel: CheckInScheduleViewModel = hiltViewModel()
 *   val uiState by viewModel.uiState.collectAsState()
 * ═══════════════════════════════════════════════════════════════════════════════
 */
package com.thewatch.app.ui.screens.checkin

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.thewatch.app.data.checkin.CheckInInterval
import com.thewatch.app.data.checkin.CheckInSchedule
import com.thewatch.app.data.checkin.CheckInSchedulePort
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import javax.inject.Inject

data class CheckInScheduleUiState(
    val schedule: CheckInSchedule = CheckInSchedule(userId = ""),
    val isLoading: Boolean = false,
    val error: String? = null,
    val saveSuccess: Boolean = false
)

@HiltViewModel
class CheckInScheduleViewModel @Inject constructor(
    private val checkInSchedulePort: CheckInSchedulePort
) : ViewModel() {

    private val _uiState = MutableStateFlow(CheckInScheduleUiState())
    val uiState: StateFlow<CheckInScheduleUiState> = _uiState.asStateFlow()

    private val userId = "user_001" // TODO: get from auth session

    init {
        loadSchedule()
    }

    private fun loadSchedule() {
        viewModelScope.launch {
            _uiState.value = _uiState.value.copy(isLoading = true)
            try {
                val schedule = checkInSchedulePort.getSchedule(userId)
                _uiState.value = _uiState.value.copy(
                    schedule = schedule,
                    isLoading = false
                )
            } catch (e: Exception) {
                _uiState.value = _uiState.value.copy(
                    isLoading = false,
                    error = e.message
                )
            }
        }
    }

    fun setInterval(interval: CheckInInterval) {
        viewModelScope.launch {
            checkInSchedulePort.setSchedule(userId, interval)
            loadSchedule()
        }
    }

    fun setCustomMinutes(minutes: Int) {
        viewModelScope.launch {
            checkInSchedulePort.setCustomIntervalMinutes(userId, minutes)
            loadSchedule()
        }
    }

    fun setQuietHours(start: Int, end: Int) {
        viewModelScope.launch {
            checkInSchedulePort.setQuietHours(userId, start, end)
            loadSchedule()
        }
    }

    fun toggleEnabled(enabled: Boolean) {
        viewModelScope.launch {
            if (enabled) {
                checkInSchedulePort.enable(userId)
            } else {
                checkInSchedulePort.disable(userId)
            }
            loadSchedule()
        }
    }

    fun clearError() {
        _uiState.value = _uiState.value.copy(error = null)
    }
}
