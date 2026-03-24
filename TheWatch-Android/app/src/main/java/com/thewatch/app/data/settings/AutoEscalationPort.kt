/**
 * ═══════════════════════════════════════════════════════════════════════════════
 * WRITE-AHEAD LOG — AutoEscalationPort.kt
 * ═══════════════════════════════════════════════════════════════════════════════
 * Purpose:   Hexagonal port (interface) for the auto-escalation timer system.
 *            When a user fails to respond to a check-in within the configured
 *            timer window (5-120 minutes), the system auto-escalates:
 *              Step 1: Notify emergency contacts in priority order
 *              Step 2: If NG911 is enabled, auto-dial 911
 *            This is the core safety mechanism of TheWatch.
 * Date:      2026-03-24
 * Author:    Claude (Anthropic)
 * Deps:      kotlinx.coroutines.flow.Flow
 * Package:   com.thewatch.app.data.settings
 *
 * Usage Example:
 *   // In a ViewModel or Service:
 *   @Inject lateinit var escalationPort: AutoEscalationPort
 *
 *   // Configure timer
 *   escalationPort.setTimerMinutes("user_001", 15)
 *
 *   // Start monitoring after check-in sent
 *   escalationPort.startEscalationTimer("user_001")
 *
 *   // User responds — cancel escalation
 *   escalationPort.cancelEscalation("user_001")
 *
 *   // User does NOT respond — observe escalation events
 *   escalationPort.escalationEvents.collect { event ->
 *       when (event) {
 *           is EscalationEvent.ContactsNotified -> { /* contacts pinged */ }
 *           is EscalationEvent.EmergencyServicesDialed -> { /* 911 called */ }
 *       }
 *   }
 *
 * Escalation Flow:
 *   1. Check-in sent to user
 *   2. Timer starts (configurable 5-120 min)
 *   3. At 50% elapsed: reminder notification to user
 *   4. At 75% elapsed: urgent reminder with vibration
 *   5. At 100% elapsed:
 *      a. Notify all emergency contacts (priority order)
 *      b. Share user location + medical profile summary with contacts
 *      c. If autoEscalateTo911 == true, initiate NG911 call
 *   6. Log entire chain to audit trail (LoggingPort)
 *
 * Related:
 *   - NG911Port (data/emergency/NG911Port.kt) — 911 auto-dial integration
 *   - CheckInSchedulePort (data/checkin/CheckInSchedulePort.kt) — triggers check-ins
 *   - NotificationService (service/NotificationService.kt) — delivers notifications
 * ═══════════════════════════════════════════════════════════════════════════════
 */
package com.thewatch.app.data.settings

import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.SharedFlow

/**
 * Events emitted during an escalation sequence.
 */
sealed class EscalationEvent {
    /** Timer started for a user check-in response window */
    data class TimerStarted(val userId: String, val durationMinutes: Int) : EscalationEvent()

    /** Reminder sent at 50% or 75% of timer elapsed */
    data class ReminderSent(val userId: String, val percentElapsed: Int) : EscalationEvent()

    /** User responded in time — escalation cancelled */
    data class Cancelled(val userId: String) : EscalationEvent()

    /** Timer expired — emergency contacts have been notified */
    data class ContactsNotified(
        val userId: String,
        val contactIds: List<String>,
        val timestamp: Long
    ) : EscalationEvent()

    /** 911 has been auto-dialed (only if NG911 toggle is on) */
    data class EmergencyServicesDialed(
        val userId: String,
        val timestamp: Long,
        val ng911SessionId: String? = null
    ) : EscalationEvent()

    /** Escalation failed (network error, permissions missing, etc.) */
    data class Failed(val userId: String, val reason: String) : EscalationEvent()
}

/**
 * Escalation configuration for a user.
 */
data class EscalationConfig(
    val userId: String,
    val timerMinutes: Int = 30,
    val autoEscalateTo911: Boolean = false,
    val enabled: Boolean = true,
    val reminderAt50Percent: Boolean = true,
    val reminderAt75Percent: Boolean = true
) {
    init {
        require(timerMinutes in 5..120) {
            "Escalation timer must be between 5 and 120 minutes, got $timerMinutes"
        }
    }
}

/**
 * Hexagonal port for auto-escalation timer management.
 *
 * Adapters:
 *   - MockAutoEscalationAdapter  — in-memory for dev/testing
 *   - (Future) WorkManagerAutoEscalationAdapter — production with WorkManager
 *   - (Future) FirebaseAutoEscalationAdapter — cloud-backed with FCM triggers
 */
interface AutoEscalationPort {

    /** Observable stream of escalation events */
    val escalationEvents: SharedFlow<EscalationEvent>

    /** Get current escalation config for a user */
    suspend fun getConfig(userId: String): EscalationConfig

    /** Save escalation configuration */
    suspend fun saveConfig(config: EscalationConfig): Result<Unit>

    /** Set the timer duration in minutes (5-120) */
    suspend fun setTimerMinutes(userId: String, minutes: Int): Result<Unit>

    /** Enable or disable auto-escalation to 911 */
    suspend fun setAutoEscalateTo911(userId: String, enabled: Boolean): Result<Unit>

    /** Start the escalation timer (called when a check-in is sent) */
    suspend fun startEscalationTimer(userId: String): Result<Unit>

    /** Cancel an active escalation (user responded to check-in) */
    suspend fun cancelEscalation(userId: String): Result<Unit>

    /** Check if an escalation timer is currently running for this user */
    suspend fun isTimerActive(userId: String): Boolean

    /** Get remaining seconds on the current timer, or null if not active */
    suspend fun getRemainingSeconds(userId: String): Long?

    /** Force-trigger escalation immediately (e.g., SOS button pressed) */
    suspend fun forceEscalate(userId: String): Result<Unit>
}
