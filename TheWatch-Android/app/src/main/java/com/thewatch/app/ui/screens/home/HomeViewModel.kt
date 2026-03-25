package com.thewatch.app.ui.screens.home

import android.util.Log
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import com.thewatch.app.data.model.Responder
import com.thewatch.app.data.repository.AlertRepository
import com.thewatch.app.data.repository.PhraseDetectionRepository
import com.thewatch.app.data.repository.PhraseMatchResult
import com.thewatch.app.data.repository.PhraseType
import com.thewatch.app.data.signalr.WatchHubConnection
import dagger.hilt.android.lifecycle.HiltViewModel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import javax.inject.Inject

sealed class AlertUiState {
    object Idle : AlertUiState()
    object Loading : AlertUiState()
    object AlertActive : AlertUiState()
    object AlertCancelled : AlertUiState()
    data class Error(val message: String) : AlertUiState()
}

@HiltViewModel
class HomeViewModel @Inject constructor(
    private val alertRepository: AlertRepository,
    private val phraseDetectionRepository: PhraseDetectionRepository,
    private val hubConnection: WatchHubConnection
) : ViewModel() {
    private val _alertUiState = MutableStateFlow<AlertUiState>(AlertUiState.Idle)
    val alertUiState: StateFlow<AlertUiState> = _alertUiState.asStateFlow()

    private val _isAlertActive = MutableStateFlow(false)
    val isAlertActive: StateFlow<Boolean> = _isAlertActive.asStateFlow()

    private val _unreadNotifications = MutableStateFlow(2)
    val unreadNotifications: StateFlow<Int> = _unreadNotifications.asStateFlow()

    /** Nearby responders fetched from API / SignalR. */
    private val _nearbyResponders = MutableStateFlow<List<Responder>>(emptyList())
    val nearbyResponders: StateFlow<List<Responder>> = _nearbyResponders.asStateFlow()

    /** Active response request ID, if any. */
    private val _activeRequestId = MutableStateFlow<String?>(null)
    val activeRequestId: StateFlow<String?> = _activeRequestId.asStateFlow()

    /** Whether phrase detection is currently listening. */
    val isPhraseDetectionActive: StateFlow<Boolean> = phraseDetectionRepository.isListening

    /** Last phrase match result (for debug/status UI). */
    private val _lastPhraseMatch = MutableStateFlow<PhraseMatchResult?>(null)
    val lastPhraseMatch: StateFlow<PhraseMatchResult?> = _lastPhraseMatch.asStateFlow()

    init {
        // Collect phrase match results and route to SOS pipeline
        viewModelScope.launch {
            phraseDetectionRepository.matchResults.collect { result ->
                handlePhraseMatch(result)
            }
        }
    }

    /**
     * Route phrase match results to the appropriate action:
     * - Duress → silent SOS (no visible alert on screen)
     * - ClearWord → cancel active alert
     * - Custom → standard SOS with countdown
     */
    private fun handlePhraseMatch(result: PhraseMatchResult) {
        _lastPhraseMatch.value = result
        val phrase = result.matchedPhrase ?: return

        Log.i("HomeViewModel", "Phrase match: type=${phrase.type}, text=\"${phrase.phraseText}\", confidence=${result.confidence}")

        when (phrase.type) {
            PhraseType.DURESS -> {
                // Silent SOS — no visible countdown, no screen alert
                activateAlert(alertType = "Duress", description = "Duress phrase detected: silent SOS", silent = true)
            }
            PhraseType.CLEAR_WORD -> {
                // Cancel active alert — user confirmed safe
                if (_isAlertActive.value) {
                    cancelAlert()
                }
            }
            PhraseType.CUSTOM -> {
                // Standard SOS trigger
                activateAlert(alertType = "Emergency", description = "Emergency phrase detected")
            }
        }
    }

    fun activateAlert(
        alertType: String = "Emergency",
        description: String = "Emergency assistance needed",
        silent: Boolean = false
    ) {
        viewModelScope.launch {
            _alertUiState.value = if (silent) AlertUiState.AlertActive else AlertUiState.Loading
            try {
                // Alert.kt has default params for all fields
                val alert = com.thewatch.app.data.model.Alert(
                    userId = "current_user", // TODO: inject actual user ID from auth
                    latitude = 40.7128,
                    longitude = -74.0060,
                    description = description,
                    severity = alertType
                )
                val result = alertRepository.activateAlert(alert)
                result.onSuccess { activated ->
                    _activeRequestId.value = activated.id
                    _alertUiState.value = AlertUiState.AlertActive
                    _isAlertActive.value = true
                    // Start loading nearby responders for map overlays
                    loadNearbyResponders(activated.latitude, activated.longitude)
                }
                result.onFailure { e ->
                    _alertUiState.value = AlertUiState.Error(e.message ?: "Unknown error")
                }
            } catch (e: Exception) {
                _alertUiState.value = AlertUiState.Error(e.message ?: "Unknown error")
            }
        }
    }

    fun cancelAlert() {
        viewModelScope.launch {
            _alertUiState.value = AlertUiState.Loading
            try {
                val requestId = _activeRequestId.value ?: ""
                alertRepository.cancelAlert(requestId)
                _alertUiState.value = AlertUiState.AlertCancelled
                _isAlertActive.value = false
                _activeRequestId.value = null
                _nearbyResponders.value = emptyList()
            } catch (e: Exception) {
                _alertUiState.value = AlertUiState.Error(e.message ?: "Unknown error")
            }
        }
    }

    /**
     * Load nearby responders from the API. During an active alert, this also
     * starts a polling flow that updates every 5 seconds (SignalR provides
     * instant updates when the real hub connection is active).
     */
    fun loadNearbyResponders(latitude: Double = 40.7128, longitude: Double = -74.0060) {
        viewModelScope.launch {
            try {
                alertRepository.getNearbyResponders(latitude, longitude, 5000.0)
                    .collect { responders ->
                        _nearbyResponders.value = responders
                    }
            } catch (e: Exception) {
                Log.w("HomeViewModel", "Failed to load nearby responders: ${e.message}")
            }
        }
    }

    fun startPhraseDetection() {
        phraseDetectionRepository.startListening()
    }

    fun stopPhraseDetection() {
        phraseDetectionRepository.stopListening()
    }

    fun markNotificationsAsRead() {
        _unreadNotifications.value = 0
    }
}
