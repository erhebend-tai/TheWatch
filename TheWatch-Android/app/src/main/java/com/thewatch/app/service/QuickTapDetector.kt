// QuickTapDetector — detects rapid multi-tap patterns for covert SOS triggering.
// Default: 4 taps within 5 seconds triggers SOS.
// "Taps" can be: volume button presses, power button presses, or screen taps.
// This is app-scoped — runs regardless of which screen is active.
//
// On Android, volume key events are the most reliable for pocket activation.
// The Activity dispatches key events to this detector; the detector maintains
// a rolling window of timestamps and fires when the threshold is met.
//
// Design: deterministic, no ML. Configurable tap count and window duration.

package com.thewatch.app.service

import android.util.Log
import android.view.KeyEvent
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.asSharedFlow
import javax.inject.Inject
import javax.inject.Singleton

/**
 * Result emitted when a quick-tap pattern is detected.
 */
data class QuickTapEvent(
    val tapCount: Int,
    val windowMs: Long,
    val triggerType: TapTriggerType,
    val timestamp: Long = System.currentTimeMillis()
)

enum class TapTriggerType {
    /** Volume button (up or down) rapid presses. */
    VOLUME_BUTTON,
    /** Power/lock button rapid presses. */
    POWER_BUTTON,
    /** Screen tap (for when app is in foreground). */
    SCREEN_TAP
}

/**
 * Configurable quick-tap detection engine.
 *
 * Architecture:
 * - Activity/Service forwards key events via [onKeyEvent] / [onScreenTap]
 * - Detector maintains a rolling window of timestamps per trigger type
 * - When [requiredTaps] occur within [windowDurationMs], emits a [QuickTapEvent]
 * - Cooldown period prevents double-firing
 *
 * This is a pure state machine — deterministic, no side effects beyond emission.
 */
@Singleton
class QuickTapDetector @Inject constructor() {

    companion object {
        private const val TAG = "QuickTapDetector"
        private const val DEFAULT_REQUIRED_TAPS = 4
        private const val DEFAULT_WINDOW_MS = 5000L
        private const val COOLDOWN_MS = 3000L // Prevent double-fire
    }

    // Configuration — can be updated from user settings
    var requiredTaps: Int = DEFAULT_REQUIRED_TAPS
    var windowDurationMs: Long = DEFAULT_WINDOW_MS

    // Whether detection is enabled
    var isEnabled: Boolean = false
        private set

    // Event emission
    private val _tapEvents = MutableSharedFlow<QuickTapEvent>(
        replay = 0,
        extraBufferCapacity = 4
    )
    val tapEvents: SharedFlow<QuickTapEvent> = _tapEvents.asSharedFlow()

    // Rolling timestamp windows per trigger type
    private val volumeTaps = mutableListOf<Long>()
    private val powerTaps = mutableListOf<Long>()
    private val screenTaps = mutableListOf<Long>()

    // Cooldown tracking
    private var lastTriggerTime = 0L

    fun enable() {
        isEnabled = true
        Log.i(TAG, "Enabled: $requiredTaps taps in ${windowDurationMs}ms")
    }

    fun disable() {
        isEnabled = false
        volumeTaps.clear()
        powerTaps.clear()
        screenTaps.clear()
        Log.i(TAG, "Disabled")
    }

    /**
     * Called from Activity.dispatchKeyEvent() or onKeyDown().
     * Returns true if the event was consumed (i.e., it contributed to a tap pattern).
     */
    fun onKeyEvent(event: KeyEvent): Boolean {
        if (!isEnabled) return false
        if (event.action != KeyEvent.ACTION_DOWN) return false

        return when (event.keyCode) {
            KeyEvent.KEYCODE_VOLUME_UP, KeyEvent.KEYCODE_VOLUME_DOWN -> {
                recordTap(volumeTaps, TapTriggerType.VOLUME_BUTTON)
                true // Consume the event so volume doesn't actually change
            }
            KeyEvent.KEYCODE_POWER -> {
                // Power button is tricky — system often intercepts it.
                // This works when the app has focus.
                recordTap(powerTaps, TapTriggerType.POWER_BUTTON)
                false // Don't consume power button
            }
            else -> false
        }
    }

    /**
     * Called when the screen is tapped (for foreground use).
     * Useful as an accessibility alternative.
     */
    fun onScreenTap() {
        if (!isEnabled) return
        recordTap(screenTaps, TapTriggerType.SCREEN_TAP)
    }

    /**
     * Core tap recording and pattern detection.
     * Thread-safe via synchronized block (tap events come from UI thread).
     */
    @Synchronized
    private fun recordTap(tapList: MutableList<Long>, type: TapTriggerType) {
        val now = System.currentTimeMillis()

        // Check cooldown
        if (now - lastTriggerTime < COOLDOWN_MS) return

        // Add timestamp
        tapList.add(now)

        // Prune expired timestamps outside the window
        val cutoff = now - windowDurationMs
        tapList.removeAll { it < cutoff }

        Log.d(TAG, "${type.name}: ${tapList.size}/$requiredTaps taps in window")

        // Check if threshold is met
        if (tapList.size >= requiredTaps) {
            Log.i(TAG, "TRIGGERED: $requiredTaps ${type.name} taps in ${windowDurationMs}ms")

            lastTriggerTime = now
            tapList.clear() // Reset after trigger

            val event = QuickTapEvent(
                tapCount = requiredTaps,
                windowMs = windowDurationMs,
                triggerType = type
            )
            _tapEvents.tryEmit(event)
        }
    }

    /**
     * Reset all tap state. Call when SOS is cancelled or app state changes.
     */
    fun reset() {
        volumeTaps.clear()
        powerTaps.clear()
        screenTaps.clear()
    }
}
