package com.thewatch.app.ui.screens.history

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.thewatch.app.data.repository.HistoryRepository
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import java.time.LocalDate
import javax.inject.Inject

data class HistoryEvent(
    val id: String,
    val eventType: String,
    val severity: String,
    val status: String,
    val timestamp: String,
    val location: String,
    val responderName: String
)

sealed class HistoryUiState {
    object Idle : HistoryUiState()
    object Loading : HistoryUiState()
    data class Success(val events: List<HistoryEvent>) : HistoryUiState()
    data class Error(val message: String) : HistoryUiState()
}

@HiltViewModel
class HistoryViewModel @Inject constructor(
    private val historyRepository: HistoryRepository
) : ViewModel() {
    private val _uiState = MutableStateFlow<HistoryUiState>(HistoryUiState.Idle)
    val uiState: StateFlow<HistoryUiState> = _uiState.asStateFlow()

    private val _selectedEventType = MutableStateFlow<String?>(null)
    val selectedEventType: StateFlow<String?> = _selectedEventType.asStateFlow()

    private val _selectedSeverity = MutableStateFlow<String?>(null)
    val selectedSeverity: StateFlow<String?> = _selectedSeverity.asStateFlow()

    private val _selectedStatus = MutableStateFlow<String?>(null)
    val selectedStatus: StateFlow<String?> = _selectedStatus.asStateFlow()

    private val _dateRangeStart = MutableStateFlow<LocalDate?>(null)
    val dateRangeStart: StateFlow<LocalDate?> = _dateRangeStart.asStateFlow()

    private val _dateRangeEnd = MutableStateFlow<LocalDate?>(null)
    val dateRangeEnd: StateFlow<LocalDate?> = _dateRangeEnd.asStateFlow()

    init {
        loadHistory()
    }

    fun loadHistory() {
        viewModelScope.launch {
            _uiState.value = HistoryUiState.Loading
            try {
                val events = historyRepository.getHistory(
                    eventType = _selectedEventType.value,
                    severity = _selectedSeverity.value,
                    status = _selectedStatus.value,
                    startDate = _dateRangeStart.value,
                    endDate = _dateRangeEnd.value
                )
                _uiState.value = HistoryUiState.Success(events)
            } catch (e: Exception) {
                _uiState.value = HistoryUiState.Error(e.message ?: "Failed to load history")
            }
        }
    }

    fun filterByEventType(eventType: String?) {
        _selectedEventType.value = eventType
        loadHistory()
    }

    fun filterBySeverity(severity: String?) {
        _selectedSeverity.value = severity
        loadHistory()
    }

    fun filterByStatus(status: String?) {
        _selectedStatus.value = status
        loadHistory()
    }

    fun filterByDateRange(start: LocalDate, end: LocalDate) {
        _dateRangeStart.value = start
        _dateRangeEnd.value = end
        loadHistory()
    }

    fun clearFilters() {
        _selectedEventType.value = null
        _selectedSeverity.value = null
        _selectedStatus.value = null
        _dateRangeStart.value = null
        _dateRangeEnd.value = null
        loadHistory()
    }
}
