// QuickTapCoordinator — app-level coordinator for tap-to-SOS.
// Collects QuickTapEvents from QuickTapDetector and dispatches to SOSService.
// Runs at application scope, independent of any screen.

package com.thewatch.app.service

import android.content.Context
import android.content.Intent
import android.util.Log
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
 * Application-scoped coordinator for quick-tap SOS triggering.
 *
 * Flow:
 * 1. User rapidly taps volume button (4x in 5s)
 * 2. QuickTapDetector emits QuickTapEvent
 * 3. This coordinator catches it, escalates location, fires SOSService
 * 4. LocationCoordinator switches to EMERGENCY mode
 */
@Singleton
class QuickTapCoordinator @Inject constructor(
    @ApplicationContext private val context: Context,
    private val quickTapDetector: QuickTapDetector,
    private val locationCoordinator: LocationCoordinator
) {
    companion object {
        private const val TAG = "QuickTapCoordinator"
    }

    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main)

    private val _isSOSActive = MutableStateFlow(false)
    val isSOSActive: StateFlow<Boolean> = _isSOSActive.asStateFlow()

    private val _lastTapEvent = MutableStateFlow<QuickTapEvent?>(null)
    val lastTapEvent: StateFlow<QuickTapEvent?> = _lastTapEvent.asStateFlow()

    init {
        // Collect tap events at app scope
        scope.launch {
            quickTapDetector.tapEvents.collect { event ->
                handleTapEvent(event)
            }
        }
    }

    fun enable() {
        quickTapDetector.enable()
        Log.i(TAG, "Quick-tap SOS enabled")
    }

    fun disable() {
        quickTapDetector.disable()
        Log.i(TAG, "Quick-tap SOS disabled")
    }

    private fun handleTapEvent(event: QuickTapEvent) {
        _lastTapEvent.value = event
        _isSOSActive.value = true

        Log.i(TAG, "SOS triggered via ${event.triggerType}: ${event.tapCount} taps in ${event.windowMs}ms")

        // Escalate location to emergency mode
        locationCoordinator.escalateToEmergency()

        // Fire SOS service
        val intent = Intent(context, SOSService::class.java).apply {
            action = SOSService.ACTION_START_SOS
            putExtra("alert_type", "Emergency")
            putExtra("description", "Quick-tap SOS: ${event.tapCount} ${event.triggerType.name} taps")
            putExtra("trigger_source", "QUICK_TAP")
        }
        context.startForegroundService(intent)
    }

    /**
     * Cancel SOS triggered by quick-tap.
     * Called from UI or by clear-word phrase detection.
     */
    fun cancelSOS() {
        if (_isSOSActive.value) {
            _isSOSActive.value = false
            locationCoordinator.deescalateToNormal()
            quickTapDetector.reset()

            val intent = Intent(context, SOSService::class.java).apply {
                action = SOSService.ACTION_CANCEL_SOS
            }
            context.startService(intent)
            Log.i(TAG, "Quick-tap SOS cancelled")
        }
    }
}
