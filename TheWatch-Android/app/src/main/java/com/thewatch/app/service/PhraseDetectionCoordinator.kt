// PhraseDetectionCoordinator — app-level coordinator for phrase detection.
// Runs independently of any screen or ViewModel. Active as long as the user
// has phrase detection enabled, regardless of which Activity/Fragment is visible.
// Collects match results from PhraseDetectionRepository and dispatches to SOSService.

package com.thewatch.app.service

import android.content.Context
import android.content.Intent
import android.util.Log
import com.thewatch.app.data.repository.PhraseDetectionRepository
import com.thewatch.app.data.repository.PhraseMatchResult
import com.thewatch.app.data.repository.PhraseType
import dagger.hilt.android.qualifiers.ApplicationContext
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import javax.inject.Inject
import javax.inject.Singleton

/**
 * Application-scoped coordinator that bridges phrase detection → SOS trigger.
 *
 * This is NOT a ViewModel — it's a Singleton injected by Hilt into the Application.
 * It runs as long as the app process is alive, independent of any screen.
 *
 * Responsibilities:
 * - Start/stop phrase detection based on user preference
 * - Collect match results from PhraseDetectionRepository
 * - Dispatch to SOSService based on phrase type (Duress/ClearWord/Custom)
 * - Track state for any screen that wants to display detection status
 */
@Singleton
class PhraseDetectionCoordinator @Inject constructor(
    @ApplicationContext private val context: Context,
    private val phraseDetectionRepository: PhraseDetectionRepository,
    private val locationCoordinator: LocationCoordinator
) {
    companion object {
        private const val TAG = "PhraseCoordinator"
    }

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)

    /** Whether phrase detection is enabled by the user (persisted preference). */
    private val _isEnabled = MutableStateFlow(false)
    val isEnabled: StateFlow<Boolean> = _isEnabled.asStateFlow()

    /** Whether the service is currently listening. */
    val isListening: StateFlow<Boolean> = phraseDetectionRepository.isListening

    /** Whether on-device recognition is available on this device. */
    val isAvailable: StateFlow<Boolean> = phraseDetectionRepository.isAvailable

    /** Last match result — observable from any screen. */
    private val _lastMatch = MutableStateFlow<PhraseMatchResult?>(null)
    val lastMatch: StateFlow<PhraseMatchResult?> = _lastMatch.asStateFlow()

    /** Whether there's an active SOS from phrase detection. */
    private val _isSOSActive = MutableStateFlow(false)
    val isSOSActive: StateFlow<Boolean> = _isSOSActive.asStateFlow()

    init {
        // Collect phrase match results at app scope — independent of any screen
        scope.launch {
            phraseDetectionRepository.matchResults.collect { result ->
                handlePhraseMatch(result)
            }
        }
    }

    /**
     * Enable phrase detection. Call this when the user toggles the feature on
     * (from Settings, Profile, or after permissions are granted).
     */
    fun enable() {
        _isEnabled.value = true
        phraseDetectionRepository.startListening()
        Log.i(TAG, "Phrase detection enabled — listening started")
    }

    /**
     * Disable phrase detection. Call when user toggles off or permissions revoked.
     */
    fun disable() {
        _isEnabled.value = false
        phraseDetectionRepository.stopListening()
        Log.i(TAG, "Phrase detection disabled — listening stopped")
    }

    /**
     * Restart if enabled — call after app returns from background,
     * or after permissions are re-granted.
     */
    fun restartIfEnabled() {
        if (_isEnabled.value) {
            phraseDetectionRepository.startListening()
        }
    }

    /**
     * Route phrase match to the appropriate SOS action.
     * This runs regardless of which screen is active.
     */
    private fun handlePhraseMatch(result: PhraseMatchResult) {
        _lastMatch.value = result
        val phrase = result.matchedPhrase ?: return

        Log.i(TAG, "MATCH: type=${phrase.type}, phrase=\"${phrase.phraseText}\", confidence=${result.confidence}")

        when (phrase.type) {
            PhraseType.DURESS -> {
                // Silent SOS — no visible UI, no countdown
                _isSOSActive.value = true
                locationCoordinator.escalateToEmergency()
                val intent = Intent(context, SOSService::class.java).apply {
                    action = SOSService.ACTION_START_SOS
                    putExtra("alert_type", "Duress")
                    putExtra("description", "Duress phrase detected — silent SOS activated")
                    putExtra("silent", true)
                }
                context.startForegroundService(intent)
            }

            PhraseType.CLEAR_WORD -> {
                // Cancel active SOS — user confirmed safe
                if (_isSOSActive.value) {
                    _isSOSActive.value = false
                    locationCoordinator.deescalateToNormal()
                    val intent = Intent(context, SOSService::class.java).apply {
                        action = SOSService.ACTION_CANCEL_SOS
                    }
                    context.startService(intent)
                    Log.i(TAG, "Clear word detected — SOS cancelled")
                }
            }

            PhraseType.CUSTOM -> {
                // Standard SOS trigger
                _isSOSActive.value = true
                locationCoordinator.escalateToEmergency()
                val intent = Intent(context, SOSService::class.java).apply {
                    action = SOSService.ACTION_START_SOS
                    putExtra("alert_type", "Emergency")
                    putExtra("description", "Emergency phrase detected")
                }
                context.startForegroundService(intent)
            }
        }
    }
}
