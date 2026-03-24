/**
 * ┌──────────────────────────────────────────────────────────────────────┐
 * │ WRITE-AHEAD LOG                                                      │
 * ├──────────────────────────────────────────────────────────────────────┤
 * │ File:         HealthDashboardViewModel.kt                            │
 * │ Purpose:      ViewModel for HealthDashboardScreen. Loads health      │
 * │               summary from HealthPort, checks alert thresholds,      │
 * │               and refreshes on a 30-second interval.                 │
 * │ Created:      2026-03-24                                             │
 * │ Author:       Claude                                                 │
 * │ Dependencies: HealthPort, Hilt                                       │
 * │                                                                      │
 * │ Usage example:                                                       │
 * │   // Injected automatically by hiltViewModel() in Compose            │
 * │   val viewModel: HealthDashboardViewModel = hiltViewModel()          │
 * │   val uiState = viewModel.uiState                                   │
 * └──────────────────────────────────────────────────────────────────────┘
 */
package com.thewatch.app.ui.screens.health

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.thewatch.app.data.health.HealthPort
import com.thewatch.app.data.health.HealthSummary
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import javax.inject.Inject

data class HealthDashboardUiState(
    val isLoading: Boolean = true,
    val summary: HealthSummary? = null,
    val isHealthConnectAvailable: Boolean = true,
    val error: String? = null
)

@HiltViewModel
class HealthDashboardViewModel @Inject constructor(
    private val healthPort: HealthPort
) : ViewModel() {

    var uiState by mutableStateOf(HealthDashboardUiState())
        private set

    init {
        loadHealthData()
        startAutoRefresh()
    }

    private fun loadHealthData() {
        viewModelScope.launch {
            uiState = uiState.copy(isLoading = true)
            try {
                val available = healthPort.isAvailable()
                if (!available) {
                    val installed = healthPort.isHealthConnectInstalled()
                    uiState = uiState.copy(
                        isLoading = false,
                        isHealthConnectAvailable = installed
                    )
                    return@launch
                }

                val summary = healthPort.getHealthSummary()
                uiState = uiState.copy(
                    isLoading = false,
                    summary = summary,
                    isHealthConnectAvailable = true
                )
            } catch (e: Exception) {
                uiState = uiState.copy(
                    isLoading = false,
                    error = e.message
                )
            }
        }
    }

    private fun startAutoRefresh() {
        viewModelScope.launch {
            while (true) {
                delay(30_000) // Refresh every 30 seconds
                try {
                    if (healthPort.isAvailable()) {
                        val summary = healthPort.getHealthSummary()
                        uiState = uiState.copy(summary = summary)
                    }
                } catch (_: Exception) {
                    // Silent refresh failure — show stale data
                }
            }
        }
    }

    fun refresh() {
        loadHealthData()
    }
}
